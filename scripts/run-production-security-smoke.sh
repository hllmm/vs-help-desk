#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

command -v curl >/dev/null
command -v docker >/dev/null
command -v dotnet >/dev/null
command -v jq >/dev/null
command -v openssl >/dev/null
command -v python3 >/dev/null
command -v setsid >/dev/null

POSTGRES_IMAGE='postgres:16-alpine@sha256:57c72fd2a128e416c7fcc499958864df5301e940bca0a56f58fddf30ffc07777'
NGINX_IMAGE='nginxinc/nginx-unprivileged:1.30-alpine@sha256:44e36330f74d4f3a1d4e222acca9e23b401fb87811a7597024502bb759c4dd49'

SMOKE_POSTGRES_USER="${SMOKE_POSTGRES_USER:-stajyer}"
SMOKE_POSTGRES_PASSWORD="${SMOKE_POSTGRES_PASSWORD:-ci_security_smoke_postgres_password}"
SMOKE_POSTGRES_DB="${SMOKE_POSTGRES_DB:-VSHelpDesk_SecuritySmoke}"
SMOKE_USERNAME="${SMOKE_USERNAME:-security-smoke}"
SMOKE_PASSWORD="${SMOKE_PASSWORD:-CiSecuritySmokePassword123!}"
SMOKE_FULL_NAME="${SMOKE_FULL_NAME:-Security Smoke User}"
SMOKE_EMAIL="${SMOKE_EMAIL:-security-smoke@example.test}"
SMOKE_SIGNING_KEY="${Auth__SigningKey:-ci-security-smoke-signing-key-with-at-least-32-bytes!!}"
SMOKE_JOBS_API_KEY="${Jobs__ApiKey:-ci-security-smoke-jobs-api-key-32-characters!!}"

TEMP_DIR="$(mktemp -d)"
POSTGRES_CONTAINER="vshd-security-smoke-postgres-$$"
NGINX_CONTAINER="vshd-security-smoke-nginx-$$"
API_PID=""
NGINX_STARTED=0

cleanup() {
    local exit_status=$?
    set +e

    if [[ -n "$API_PID" ]]; then
        kill -- "-$API_PID" 2>/dev/null || true
        wait "$API_PID" 2>/dev/null || true
    fi

    if (( NGINX_STARTED )); then
        docker rm -f "$NGINX_CONTAINER" >/dev/null 2>&1 || true
    fi
    docker rm -f "$POSTGRES_CONTAINER" >/dev/null 2>&1 || true

    if [[ -n "${SMOKE_ARTIFACT_DIR:-}" ]]; then
        mkdir -p "$SMOKE_ARTIFACT_DIR"
        cp -f "$TEMP_DIR"/*.log "$SMOKE_ARTIFACT_DIR"/ 2>/dev/null || true
        cp -f "$TEMP_DIR"/*.headers "$SMOKE_ARTIFACT_DIR"/ 2>/dev/null || true
        cp -f "$TEMP_DIR"/*.body "$SMOKE_ARTIFACT_DIR"/ 2>/dev/null || true
    fi

    rm -rf "$TEMP_DIR"
    exit "$exit_status"
}
trap cleanup EXIT

pick_port() {
    python3 - <<'PY'
import socket

with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
    sock.bind(("127.0.0.1", 0))
    print(sock.getsockname()[1])
PY
}

wait_for_postgres() {
    local port="$1"
    for _ in $(seq 1 60); do
        if docker exec "$POSTGRES_CONTAINER" pg_isready \
            --host 127.0.0.1 \
            --port 5432 \
            --username "$SMOKE_POSTGRES_USER" \
            --dbname "$SMOKE_POSTGRES_DB" >/dev/null 2>&1; then
            echo "PostgreSQL is ready on host port $port"
            return 0
        fi
        sleep 1
    done

    echo "PostgreSQL did not become ready" >&2
    docker logs "$POSTGRES_CONTAINER" >&2 || true
    return 1
}

wait_for_api() {
    local port="$1"
    local log_file="$2"
    local scheme="${3:-http}"
    local url="${scheme}://127.0.0.1:$port/health"
    local -a curl_options=(--noproxy '*' --silent --show-error --fail --max-time 2)
    if [[ "$scheme" == https ]]; then
        curl_options+=(--cacert "$TLS_DIR/ca.crt")
    fi

    for _ in $(seq 1 60); do
        if curl "${curl_options[@]}" --output /dev/null "$url"; then
            echo "API is responding over $scheme on host port $port"
            return 0
        fi
        sleep 1
    done

    echo "API did not become ready on host port $port" >&2
    sed -n '1,240p' "$log_file" >&2 || true
    return 1
}

stop_api() {
    if [[ -n "$API_PID" ]]; then
        kill -- "-$API_PID" 2>/dev/null || true
        wait "$API_PID" 2>/dev/null || true
        API_PID=""
    fi
}

start_api() {
    local environment="$1"
    local port="$2"
    local log_file="$3"

    setsid env \
        ASPNETCORE_ENVIRONMENT="$environment" \
        ASPNETCORE_URLS="http://0.0.0.0:$port;https://0.0.0.0:$API_TLS_PORT" \
        ASPNETCORE_HTTPS_PORT="$EDGE_HTTPS_PORT" \
        HttpsRedirection__HttpsPort="$EDGE_HTTPS_PORT" \
        ASPNETCORE_Kestrel__Certificates__Default__Path="$TLS_DIR/server.pfx" \
        ASPNETCORE_Kestrel__Certificates__Default__Password="$TLS_PASSWORD" \
        ConnectionStrings__DefaultConnection="$CONNECTION_STRING" \
        Auth__SigningKey="$SMOKE_SIGNING_KEY" \
        Auth__Issuer=VSHelpDesk \
        Auth__Audience=VSHelpDesk \
        Auth__ExpirationMinutes=60 \
        Jobs__ApiKey="$SMOKE_JOBS_API_KEY" \
        Email__ReceiverMode="$EMAIL_RECEIVER_MODE" \
        Email__SmtpHost=127.0.0.1 \
        Email__SmtpPort=2525 \
        Email__SmtpSecurityMode="$EMAIL_SMTP_SECURITY" \
        Email__ImapHost=127.0.0.1 \
        Email__ImapPort=3143 \
        Email__ImapSecurityMode=SslOnConnect \
        Email__ImapUsername=security-smoke-unused \
        Email__ImapPassword=security-smoke-unused \
        Email__ImapAccountId=security-smoke-unused \
        Email__SupportMailboxAddress=support@example.test \
        Email__TrustedAuthServId=security-smoke.invalid \
        SeedUser__Enabled="$SEED_USER_ENABLED" \
        SeedUser__Password="$SMOKE_PASSWORD" \
        SeedUser__Username="$SMOKE_USERNAME" \
        SeedUser__FullName="$SMOKE_FULL_NAME" \
        SeedUser__Email="$SMOKE_EMAIL" \
        SeedAdmin__Enabled=false \
        Cors__AllowedOrigins__0="https://127.0.0.1:$EDGE_HTTPS_PORT" \
        ForwardedHeaders__ForwardLimit=2 \
        ForwardedHeaders__TrustedNetworks__0="$TRUSTED_PROXY_CIDR" \
        ForwardedHeaders__TrustedNetworks__1= \
        dotnet run \
        --project "$ROOT_DIR/src/VSHelpDesk.WebAPI" \
        --configuration Release \
        --no-build \
        --no-launch-profile \
        >"$log_file" 2>&1 &
    API_PID=$!
}

assert_status() {
    local expected="$1"
    local actual="$2"
    local description="$3"
    if [[ "$actual" != "$expected" ]]; then
        echo "FAIL: $description (expected HTTP $expected, got $actual)" >&2
        return 1
    fi
    echo "PASS: $description (HTTP $actual)"
}

assert_set_cookie_attributes() {
    local header_file="$1"
    local cookie_name="$2"
    local description="$3"
    local cookie_line
    cookie_line="$(grep -i "^set-cookie: ${cookie_name}=" "$header_file" | head -n 1 || true)"
    [[ -n "$cookie_line" ]] || {
        echo "FAIL: $description (cookie $cookie_name was not set)" >&2
        return 1
    }
    grep -Eiq ';[[:space:]]*Secure([;[:space:]]|$)' <<<"$cookie_line" || {
        echo "FAIL: $description (Secure attribute missing)" >&2
        echo "$cookie_line" >&2
        return 1
    }
    grep -Eiq ';[[:space:]]*SameSite=Lax([;[:space:]]|$)' <<<"$cookie_line" || {
        echo "FAIL: $description (SameSite=Lax attribute missing)" >&2
        echo "$cookie_line" >&2
        return 1
    }
    echo "PASS: $description"
}

cookie_value() {
    local cookie_file="$1"
    local cookie_name="$2"
    awk -v name="$cookie_name" '$6 == name { value = $7 } END { print value }' "$cookie_file"
}

echo "Starting pinned PostgreSQL image"
docker run \
    --detach \
    --rm \
    --name "$POSTGRES_CONTAINER" \
    --publish 127.0.0.1::5432 \
    --env POSTGRES_USER="$SMOKE_POSTGRES_USER" \
    --env POSTGRES_PASSWORD="$SMOKE_POSTGRES_PASSWORD" \
    --env POSTGRES_DB="$SMOKE_POSTGRES_DB" \
    "$POSTGRES_IMAGE" >/dev/null

POSTGRES_PORT="$(docker port "$POSTGRES_CONTAINER" 5432/tcp | awk -F: 'NR == 1 { print $NF }')"
[[ "$POSTGRES_PORT" =~ ^[0-9]+$ ]] || {
    echo "Could not determine PostgreSQL host port" >&2
    exit 1
}
wait_for_postgres "$POSTGRES_PORT"

API_PORT="$(pick_port)"
API_TLS_PORT="$(pick_port)"
EDGE_HTTP_PORT="$(pick_port)"
EDGE_HTTPS_PORT="$(pick_port)"
EDGE_CLIENT_ADDRESS=127.0.0.3
EDGE_CURL_ARGS=(--noproxy '*' --interface "$EDGE_CLIENT_ADDRESS")
CONNECTION_STRING="Host=127.0.0.1;Port=$POSTGRES_PORT;Database=$SMOKE_POSTGRES_DB;Username=$SMOKE_POSTGRES_USER;Password=$SMOKE_POSTGRES_PASSWORD"
# The unprivileged edge uses a dedicated loopback source address in host
# networking. The API trusts only this exact test-proxy address; direct smoke
# requests use 127.0.0.1 and therefore remain outside the trust model.
TRUSTED_PROXY_CIDR=127.0.0.2/32
TLS_DIR="$TEMP_DIR/tls"
TLS_PASSWORD=security-smoke-certificate-password

mkdir -p "$TLS_DIR"
chmod 755 "$TLS_DIR"
printf '%s\n' \
    'basicConstraints=CA:FALSE' \
    'keyUsage=digitalSignature,keyEncipherment' \
    'extendedKeyUsage=serverAuth' \
    'subjectAltName=DNS:localhost,IP:127.0.0.1' \
    >"$TLS_DIR/server.ext"
openssl req -x509 -newkey rsa:2048 -nodes -sha256 -days 1 \
    -keyout "$TLS_DIR/ca.key" \
    -out "$TLS_DIR/ca.crt" \
    -subj '/CN=VS Help Desk smoke CA' \
    >/dev/null 2>&1
openssl req -newkey rsa:2048 -nodes -sha256 \
    -keyout "$TLS_DIR/server.key" \
    -out "$TLS_DIR/server.csr" \
    -subj '/CN=localhost' \
    >/dev/null 2>&1
openssl x509 -req -sha256 -days 1 \
    -in "$TLS_DIR/server.csr" \
    -CA "$TLS_DIR/ca.crt" \
    -CAkey "$TLS_DIR/ca.key" \
    -CAcreateserial \
    -out "$TLS_DIR/server.crt" \
    -extfile "$TLS_DIR/server.ext" \
    >/dev/null 2>&1
openssl pkcs12 -export \
    -out "$TLS_DIR/server.pfx" \
    -inkey "$TLS_DIR/server.key" \
    -in "$TLS_DIR/server.crt" \
    -passout "pass:$TLS_PASSWORD" \
    >/dev/null 2>&1
chmod 644 "$TLS_DIR/server.key" "$TLS_DIR/server.crt" "$TLS_DIR/server.pfx" "$TLS_DIR/ca.crt"

export ASPNETCORE_ENVIRONMENT=Production
export ConnectionStrings__DefaultConnection="$CONNECTION_STRING"
export Auth__SigningKey="$SMOKE_SIGNING_KEY"
export Auth__Issuer=VSHelpDesk
export Auth__Audience=VSHelpDesk
export Auth__ExpirationMinutes=60
export Jobs__ApiKey="$SMOKE_JOBS_API_KEY"
export Email__ReceiverMode=Imap
export Email__SmtpHost=127.0.0.1
export Email__SmtpPort=2525
export Email__SmtpSecurityMode=StartTls
export Email__ImapHost=127.0.0.1
export Email__ImapPort=3143
export Email__ImapSecurityMode=SslOnConnect
export Email__ImapUsername=security-smoke-unused
export Email__ImapPassword=security-smoke-unused
export Email__ImapAccountId=security-smoke-unused
export Email__SupportMailboxAddress=support@example.test
export Email__TrustedAuthServId=security-smoke.invalid
echo "Applying PostgreSQL migrations"
dotnet ef database update \
    --project "$ROOT_DIR/src/VSHelpDesk.Infrastructure" \
    --startup-project "$ROOT_DIR/src/VSHelpDesk.WebAPI" \
    --configuration Release \
    --no-build

# Production intentionally does not seed accounts. Bootstrap the ephemeral smoke
# account through the existing Development-only seeder, then test a fresh
# Production process against the same migrated database.
EMAIL_RECEIVER_MODE=Fake
EMAIL_SMTP_SECURITY=None
SEED_USER_ENABLED=true
start_api Development "$API_PORT" "$TEMP_DIR/api-bootstrap.log"
wait_for_api "$API_PORT" "$TEMP_DIR/api-bootstrap.log"
stop_api

EMAIL_RECEIVER_MODE=Imap
EMAIL_SMTP_SECURITY=StartTls
SEED_USER_ENABLED=false
start_api Production "$API_PORT" "$TEMP_DIR/api-production.log"
wait_for_api "$API_TLS_PORT" "$TEMP_DIR/api-production.log" https

NGINX_CONFIG="$TLS_DIR/default.conf"
{
    printf '%s\n' \
        'server {' \
        "  listen $EDGE_HTTP_PORT;" \
        '  server_name _;'
    printf '  return 308 https://$host:%s$request_uri;\n' "$EDGE_HTTPS_PORT"
    printf '%s\n' \
        '}' \
        'server {' \
        "  listen $EDGE_HTTPS_PORT ssl;" \
        '  server_name _;' \
        '  ssl_certificate /etc/nginx/tls/server.crt;' \
        '  ssl_certificate_key /etc/nginx/tls/server.key;' \
        '  ssl_protocols TLSv1.2 TLSv1.3;' \
        '  location / {'
    printf '    proxy_pass http://127.0.0.1:%s;\n' "$API_PORT"
    printf '%s\n' \
        '    proxy_bind 127.0.0.2;' \
        '    # Discard client-supplied X-Forwarded-For; only the edge observes the peer IP.' \
        '    proxy_set_header X-Forwarded-For $remote_addr;' \
        '    proxy_set_header X-Forwarded-Proto https;' \
        '    proxy_set_header X-Real-IP $remote_addr;'
    printf '%s\n' \
        '    proxy_set_header Host $host;' \
        '  }' \
        '}'
} >"$NGINX_CONFIG"

echo "Starting pinned unprivileged Nginx TLS edge"
docker run \
    --detach \
    --rm \
    --name "$NGINX_CONTAINER" \
    --network host \
    --volume "$NGINX_CONFIG:/etc/nginx/conf.d/default.conf:ro" \
    --volume "$TLS_DIR:/etc/nginx/tls:ro" \
    "$NGINX_IMAGE" >/dev/null
NGINX_STARTED=1

for _ in $(seq 1 30); do
    if curl "${EDGE_CURL_ARGS[@]}" --silent --show-error --fail \
        --cacert "$TLS_DIR/ca.crt" \
        "https://127.0.0.1:$EDGE_HTTPS_PORT/health" >/dev/null; then
        break
    fi
    sleep 1
done
curl "${EDGE_CURL_ARGS[@]}" --silent --show-error --fail \
    --cacert "$TLS_DIR/ca.crt" \
    "https://127.0.0.1:$EDGE_HTTPS_PORT/health" >/dev/null
echo 'PASS: HTTPS edge is reachable'

HTTP_REDIRECT_HEADERS="$TEMP_DIR/http-redirect.headers"
HTTP_STATUS="$(curl "${EDGE_CURL_ARGS[@]}" --silent --show-error \
    --dump-header "$HTTP_REDIRECT_HEADERS" \
    --output /dev/null \
    --write-out '%{http_code}' \
    "http://127.0.0.1:$EDGE_HTTP_PORT/api/auth/csrf")"
case "$HTTP_STATUS" in
    301|302|307|308) echo "PASS: HTTP redirects to HTTPS (HTTP $HTTP_STATUS)" ;;
    *) echo "FAIL: HTTP did not redirect to HTTPS (HTTP $HTTP_STATUS)" >&2; exit 1 ;;
esac
grep -Eiq "^location:[[:space:]]+https://127\.0\.0\.1:$EDGE_HTTPS_PORT/" "$HTTP_REDIRECT_HEADERS" || {
    echo 'FAIL: redirect Location did not point to the HTTPS edge' >&2
    exit 1
}

COOKIE_JAR="$TEMP_DIR/cookies.txt"
CSRF_HEADERS="$TEMP_DIR/csrf.headers"
CSRF_BODY="$TEMP_DIR/csrf.body"
CSRF_STATUS="$(curl "${EDGE_CURL_ARGS[@]}" --silent --show-error \
    --cacert "$TLS_DIR/ca.crt" \
    --cookie-jar "$COOKIE_JAR" \
    --dump-header "$CSRF_HEADERS" \
    --output "$CSRF_BODY" \
    --write-out '%{http_code}' \
    "https://127.0.0.1:$EDGE_HTTPS_PORT/api/auth/csrf")"
assert_status 200 "$CSRF_STATUS" 'Production CSRF endpoint over HTTPS'
assert_set_cookie_attributes "$CSRF_HEADERS" vshd.csrf 'CSRF cookie has Secure and SameSite=Lax'
CSRF_TOKEN="$(jq -er '.csrfToken' "$CSRF_BODY")"

LOGIN_HEADERS="$TEMP_DIR/login.headers"
LOGIN_BODY="$TEMP_DIR/login.body"
LOGIN_JSON="$(jq -cn --arg username "$SMOKE_USERNAME" --arg password "$SMOKE_PASSWORD" '{username:$username,password:$password}')"
LOGIN_STATUS="$(curl "${EDGE_CURL_ARGS[@]}" --silent --show-error \
    --cacert "$TLS_DIR/ca.crt" \
    --cookie "$COOKIE_JAR" \
    --cookie-jar "$COOKIE_JAR" \
    --header 'Content-Type: application/json' \
    --header "X-CSRF-Token: $CSRF_TOKEN" \
    --data "$LOGIN_JSON" \
    --dump-header "$LOGIN_HEADERS" \
    --output "$LOGIN_BODY" \
    --write-out '%{http_code}' \
    "https://127.0.0.1:$EDGE_HTTPS_PORT/api/auth/login")"
assert_status 200 "$LOGIN_STATUS" 'Production login over HTTPS'
assert_set_cookie_attributes "$LOGIN_HEADERS" vshd.auth 'Authentication cookie has Secure and SameSite=Lax'
grep -Eiq '^set-cookie: vshd\.auth=.*HttpOnly' "$LOGIN_HEADERS" || {
    echo 'FAIL: authentication cookie is not HttpOnly' >&2
    exit 1
}

ME_BODY="$TEMP_DIR/me.body"
ME_STATUS="$(curl "${EDGE_CURL_ARGS[@]}" --silent --show-error \
    --cacert "$TLS_DIR/ca.crt" \
    --cookie "$COOKIE_JAR" \
    --output "$ME_BODY" \
    --write-out '%{http_code}' \
    "https://127.0.0.1:$EDGE_HTTPS_PORT/api/auth/me")"
assert_status 200 "$ME_STATUS" '/api/auth/me restores the HTTPS session'
jq -e --arg username "$SMOKE_USERNAME" '.username == $username' "$ME_BODY" >/dev/null
echo 'PASS: /api/auth/me returned the seeded user'

CSRF_AFTER_LOGIN="$(cookie_value "$COOKIE_JAR" vshd.csrf)"
[[ -n "$CSRF_AFTER_LOGIN" ]] || {
    echo 'FAIL: could not read the post-login CSRF cookie' >&2
    exit 1
}

for request_number in $(seq 2 10); do
    RATE_STATUS="$(curl "${EDGE_CURL_ARGS[@]}" --silent --show-error \
        --cacert "$TLS_DIR/ca.crt" \
        --cookie "$COOKIE_JAR" \
        --header 'Content-Type: application/json' \
        --header "X-CSRF-Token: $CSRF_AFTER_LOGIN" \
        --header "X-Login-Username: rotated-$request_number" \
        --header "X-Forwarded-For: 203.0.113.$request_number" \
        --data '{"username":"missing-security-smoke-user","password":"wrong-password"}' \
        --output /dev/null \
        --write-out '%{http_code}' \
        "https://127.0.0.1:$EDGE_HTTPS_PORT/api/auth/login")"
    assert_status 401 "$RATE_STATUS" "same-IP login request $request_number remains an authentication failure"
done

RATE_LIMIT_STATUS="$(curl "${EDGE_CURL_ARGS[@]}" --silent --show-error \
    --cacert "$TLS_DIR/ca.crt" \
    --cookie "$COOKIE_JAR" \
    --header 'Content-Type: application/json' \
    --header "X-CSRF-Token: $CSRF_AFTER_LOGIN" \
    --header 'X-Login-Username: rotated-11' \
    --header 'X-Forwarded-For: 203.0.113.11' \
    --data '{"username":"missing-security-smoke-user","password":"wrong-password"}' \
    --output /dev/null \
    --write-out '%{http_code}' \
    "https://127.0.0.1:$EDGE_HTTPS_PORT/api/auth/login")"
assert_status 429 "$RATE_LIMIT_STATUS" 'eleventh same-IP login is rate limited despite rotating X-Login-Username'

# Exercise the API's own TLS listener so a client-supplied X-Forwarded-For is
# tested independently of the X-Forwarded-Proto redirect assertion below. The
# direct client is outside the dynamically trusted Docker network; if the
# forged address reached the limiter, each request would get a new partition.
for request_number in $(seq 1 10); do
    DIRECT_XFF_STATUS="$(curl --noproxy '*' --silent --show-error \
        --cacert "$TLS_DIR/ca.crt" \
        --header 'Content-Type: application/json' \
        --header "X-Forwarded-For: 198.51.100.$request_number" \
        --data '{"username":"missing-direct-security-smoke-user","password":"wrong-password"}' \
        --output /dev/null \
        --write-out '%{http_code}' \
        "https://127.0.0.1:$API_TLS_PORT/api/auth/login")"
    assert_status 401 "$DIRECT_XFF_STATUS" "direct TLS login with forged X-Forwarded-For $request_number remains an authentication failure"
done
DIRECT_XFF_LIMIT_STATUS="$(curl --noproxy '*' --silent --show-error \
    --cacert "$TLS_DIR/ca.crt" \
    --header 'Content-Type: application/json' \
    --header 'X-Forwarded-For: 198.51.100.11' \
    --data '{"username":"missing-direct-security-smoke-user","password":"wrong-password"}' \
    --output /dev/null \
    --write-out '%{http_code}' \
    "https://127.0.0.1:$API_TLS_PORT/api/auth/login")"
assert_status 429 "$DIRECT_XFF_LIMIT_STATUS" 'direct forged X-Forwarded-For cannot create a new rate-limit partition'

FORGED_FORWARDING_STATUS="$(curl --noproxy '*' --silent --show-error \
    --header 'X-Forwarded-Proto: https' \
    --header 'X-Forwarded-For: 203.0.113.9' \
    --header 'Content-Type: application/json' \
    --data '{"username":"missing-security-smoke-user","password":"wrong-password"}' \
    --output /dev/null \
    --write-out '%{http_code}' \
    "http://127.0.0.1:$API_PORT/api/auth/login")"
case "$FORGED_FORWARDING_STATUS" in
    301|302|307|308) echo "PASS: forged X-Forwarded-Proto and X-Forwarded-For outside the trusted proxy network were ignored (HTTP $FORGED_FORWARDING_STATUS)" ;;
    *) echo "FAIL: forged forwarding headers were accepted by the direct API (HTTP $FORGED_FORWARDING_STATUS)" >&2; exit 1 ;;
esac

echo 'Production TLS authentication smoke: PASS'
