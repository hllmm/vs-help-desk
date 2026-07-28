# Security Proxy, Container, and CSP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix SEC-005, SEC-007, and SEC-008, clear the non-applicable React Router audit finding, and deliver the complete security branch through a verified pull request.

**Architecture:** Bind and validate an explicit reverse-proxy trust model, align Docker and Kubernetes hop counts, add a restrictive nginx CSP, and pin maintained nginx/curl images. Finish with dependency, container, Kubernetes, browser, and backend verification before merging PR 2.

**Tech Stack:** ASP.NET Core forwarded headers, nginx, Docker/Compose, Kubernetes/Kustomize, React Router, npm audit, GitHub Actions/CLI.

## Global Constraints

- Implement last on `security/hardening`.
- Forwarded headers are never trusted from arbitrary remote addresses.
- Docker path is one trusted hop: fixed web proxy `172.30.0.10`.
- Kubernetes path is two trusted hops: ingress and web proxy within the configured cluster network.
- Production startup fails when no trusted proxy/network is configured.
- CSP contains no `unsafe-inline` or `unsafe-eval`.
- nginx runtime image is `1.28.3-alpine3.23` pinned to digest `sha256:a8b39bd9cf0f83869a2162827a0caf6137ddf759d50a171451b335cecc87d236`.
- curl job image is `8.21.0` pinned to digest `sha256:7c12af72ceb38b7432ab85e1a265cff6ae58e06f95539d539b654f2cfa64bb13`.
- Keep the legacy frontend branch and both merged PR branches on GitHub.
- Merge only after every required local and GitHub check passes.

---

### Task 1: Bind and validate trusted reverse proxies

**Files:**
- Create: `src/VSHelpDesk.WebAPI/Options/ReverseProxyOptions.cs`
- Create: `src/VSHelpDesk.WebAPI/Options/ReverseProxyOptionsValidator.cs`
- Create: `src/VSHelpDesk.WebAPI/Extensions/ForwardedHeadersExtensions.cs`
- Modify: `src/VSHelpDesk.WebAPI/Program.cs`
- Modify: `src/VSHelpDesk.WebAPI/appsettings.json`
- Modify: `src/VSHelpDesk.WebAPI/appsettings.Development.json`
- Test: `tests/VSHelpDesk.WebAPI.IntegrationTests/Infrastructure/ReverseProxyOptionsTests.cs`

**Interfaces:**
- Produces: `ReverseProxy:ForwardLimit`, `KnownProxies[]`, and `KnownNetworks[]`.
- Produces: `AddTrustedForwardedHeaders(IConfiguration, IHostEnvironment)`.

- [ ] **Step 1: Write failing validator tests**

Cover:

```csharp
[Fact]
public void ProductionWithoutTrustList_Fails()
{
    var result = Validate(
        environmentName: Environments.Production,
        new ReverseProxyOptions { ForwardLimit = 1 });

    Assert.True(result.Failed);
}

[Theory]
[InlineData(0)]
[InlineData(5)]
public void ForwardLimitOutsideOneThroughFour_Fails(int value)
{
    var result = Validate(
        Environments.Production,
        new ReverseProxyOptions
        {
            ForwardLimit = value,
            KnownProxies = ["172.30.0.10"]
        });

    Assert.True(result.Failed);
}
```

Also reject malformed IP addresses/CIDRs and accept the Docker/Kubernetes
examples below.

- [ ] **Step 2: Add exact option types and validation**

Define:

```csharp
public sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";
    public int ForwardLimit { get; init; } = 1;
    public string[] KnownProxies { get; init; } = [];
    public string[] KnownNetworks { get; init; } = [];
}
```

Validate limit `1..4`, parse proxies with `IPAddress.TryParse`, parse networks
with `IPNetwork.TryParse`, and require at least one trust entry in Production.

- [ ] **Step 3: Configure forwarded headers from validated options**

The extension binds/validates on startup and configures:

```csharp
options.ForwardedHeaders =
    ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
options.ForwardLimit = reverseProxy.ForwardLimit;
options.KnownIPNetworks.Clear();
options.KnownProxies.Clear();
```

Then add only parsed configured values. Remove the unconditional trust-all
block from `Program.cs` and call the extension before `builder.Build()`.

- [ ] **Step 4: Add fail-closed base and safe development defaults**

In `appsettings.json`:

```json
"ReverseProxy": {
  "ForwardLimit": 1,
  "KnownProxies": [],
  "KnownNetworks": []
}
```

In `appsettings.Development.json` override:

```json
"ReverseProxy": {
  "ForwardLimit": 1,
  "KnownProxies": ["127.0.0.1", "::1"],
  "KnownNetworks": []
}
```

Production therefore starts only when Compose, Kubernetes, or another operator
configuration supplies a trust list.

- [ ] **Step 5: Run tests and commit**

Run:

```bash
dotnet test tests/VSHelpDesk.WebAPI.IntegrationTests \
  --filter 'FullyQualifiedName~ReverseProxyOptionsTests'
git add src tests
git commit -m "fix(proxy): trust only configured forwarding hops"
```

Expected: focused tests pass.

### Task 2: Align Docker and Kubernetes proxy chains

**Files:**
- Modify: `docker-compose.prod.yml`
- Modify: `frontend/nginx.conf`
- Modify: `deploy/k8s/base/configmap.yaml`
- Modify: `deploy/k8s/base/web-nginx-configmap.yaml`
- Create: `deploy/k8s/base/api-networkpolicy.yaml`
- Modify: `deploy/k8s/base/kustomization.yaml`
- Modify: `deploy/k8s/overlays/prod/ingress-patch.yaml`

**Interfaces:**
- Consumes: Reverse-proxy options from Task 1.
- Produces: one-hop Docker and two-hop Kubernetes forwarding chains.

- [ ] **Step 1: Give the Docker web proxy a fixed internal address**

Change the `internal` network to:

```yaml
networks:
  internal:
    ipam:
      config:
        - subnet: 172.30.0.0/24
```

Set the web service:

```yaml
networks:
  internal:
    ipv4_address: 172.30.0.10
```

Set API environment:

```yaml
ReverseProxy__ForwardLimit: "1"
ReverseProxy__KnownProxies__0: "172.30.0.10"
```

- [ ] **Step 2: Discard client-supplied forwarding headers in Docker nginx**

For every API/health proxy location use:

```nginx
proxy_set_header X-Real-IP $remote_addr;
proxy_set_header X-Forwarded-For $remote_addr;
proxy_set_header X-Forwarded-Proto $scheme;
```

This Docker entrypoint is the only public hop, so no incoming chain is needed.

- [ ] **Step 3: Preserve only a normalized ingress scheme in Kubernetes**

Before the Kubernetes `server` block add:

```nginx
map $http_x_forwarded_proto $trusted_forwarded_proto {
    default $scheme;
    "~*^https$" https;
    "~*^http$" http;
}
```

For API/health proxy locations use:

```nginx
proxy_set_header X-Real-IP $remote_addr;
proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
proxy_set_header X-Forwarded-Proto $trusted_forwarded_proto;
```

- [ ] **Step 4: Configure the Kubernetes two-hop trust**

Add:

```yaml
ReverseProxy__ForwardLimit: "2"
ReverseProxy__KnownNetworks__0: "10.244.0.0/16"
```

Document next to the value that the production overlay must replace it with
the actual ingress/web pod network when the cluster does not use
`10.244.0.0/16`.

- [ ] **Step 5: Restrict API ingress to web and job pods**

Create:

```yaml
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: api-ingress
spec:
  podSelector:
    matchLabels:
      app.kubernetes.io/name: api
  policyTypes:
    - Ingress
  ingress:
    - from:
        - podSelector:
            matchLabels:
              app.kubernetes.io/name: web
        - podSelector:
            matchLabels:
              app.kubernetes.io/component: jobs
      ports:
        - protocol: TCP
          port: 8080
```

Add `api-networkpolicy.yaml` to base `kustomization.yaml`.

- [ ] **Step 6: Validate rendered configuration and commit**

Run:

```bash
POSTGRES_PASSWORD='compose-test-password' \
AUTH_SIGNING_KEY='compose-signing-key-at-least-32-bytes' \
JOBS_API_KEY='compose-jobs-api-key-32-characters' \
  docker compose -f docker-compose.prod.yml config >/tmp/vshelpdesk-compose.yml
kubectl kustomize deploy/k8s/base >/tmp/vshelpdesk-k8s-base.yml
kubectl kustomize deploy/k8s/overlays/prod >/tmp/vshelpdesk-k8s-prod.yml
rg -n 'ReverseProxy__|172\\.30\\.0\\.10|trusted_forwarded_proto|kind: NetworkPolicy' \
  /tmp/vshelpdesk-compose.yml \
  /tmp/vshelpdesk-k8s-base.yml \
  /tmp/vshelpdesk-k8s-prod.yml
git add docker-compose.prod.yml frontend/nginx.conf deploy/k8s
git commit -m "fix(proxy): align trusted deployment hops"
```

Expected: all render commands exit `0`; output shows exact limits/trust entries.

### Task 3: Add a restrictive Content Security Policy

**Files:**
- Modify: `frontend/nginx.conf`
- Modify: `deploy/k8s/base/web-nginx-configmap.yaml`
- Test: `frontend/e2e/security-headers.smoke.spec.ts`
- Create: `frontend/playwright.security.config.ts`
- Modify: `frontend/playwright.config.ts`

**Interfaces:**
- Produces: identical CSP and related headers in Docker and Kubernetes nginx.

- [ ] **Step 1: Add the failing header smoke test**

Create a Playwright request-context test against an nginx container and assert:

```ts
const csp = response.headers()['content-security-policy']
expect(csp).toContain("default-src 'self'")
expect(csp).toContain("object-src 'none'")
expect(csp).toContain("frame-ancestors 'none'")
expect(csp).not.toContain("'unsafe-inline'")
expect(csp).not.toContain("'unsafe-eval'")
```

Create a dedicated config with no `webServer`:

```ts
import { defineConfig } from '@playwright/test'

export default defineConfig({
  testDir: './e2e',
  testMatch: 'security-headers.smoke.spec.ts',
  use: {
    baseURL: process.env.PLAYWRIGHT_BASE_URL ?? 'http://127.0.0.1:18080',
  },
  projects: [{ name: 'chromium' }],
})
```

Add this to the normal Playwright config so the nginx-only test is not run
against Vite preview:

```ts
testIgnore: 'security-headers.smoke.spec.ts',
```

- [ ] **Step 2: Add the exact CSP to both nginx configs**

Use one line in each config:

```nginx
add_header Content-Security-Policy "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: blob:; font-src 'self'; connect-src 'self'; manifest-src 'self'" always;
```

Also add:

```nginx
add_header Permissions-Policy "camera=(), microphone=(), geolocation=()" always;
```

Retain existing `nosniff`, frame, and referrer headers.

- [ ] **Step 3: Build and run nginx for the test**

Run:

```bash
docker build -t vshelpdesk-web:security-test frontend
docker run -d --rm \
  --name vshelpdesk-web-security-test \
  --add-host api:127.0.0.1 \
  -p 127.0.0.1:18080:80 \
  vshelpdesk-web:security-test
cd frontend
PLAYWRIGHT_BASE_URL='http://127.0.0.1:18080' npx playwright test \
  --config playwright.security.config.ts
cd ..
docker stop vshelpdesk-web-security-test
```

Expected: the security-header test passes and the container is removed.

- [ ] **Step 4: Commit**

Run:

```bash
git add \
  frontend/nginx.conf \
  deploy/k8s/base/web-nginx-configmap.yaml \
  frontend/e2e/security-headers.smoke.spec.ts \
  frontend/playwright.security.config.ts \
  frontend/playwright.config.ts
git commit -m "fix(web): enforce a restrictive content security policy"
```

### Task 4: Pin maintained nginx and curl images

**Files:**
- Modify: `frontend/Dockerfile`
- Modify: `deploy/k8s/base/cronjob-process-incoming-emails.yaml`
- Modify: `deploy/k8s/base/cronjob-resolve-inactive-tickets.yaml`

**Interfaces:**
- Produces: immutable maintained runtime images verified from official upstream registries.

- [ ] **Step 1: Replace nginx runtime image**

Use:

```dockerfile
FROM nginx:1.28.3-alpine3.23@sha256:a8b39bd9cf0f83869a2162827a0caf6137ddf759d50a171451b335cecc87d236 AS final
```

- [ ] **Step 2: Replace both curl job images**

Use:

```yaml
image: curlimages/curl:8.21.0@sha256:7c12af72ceb38b7432ab85e1a265cff6ae58e06f95539d539b654f2cfa64bb13
```

- [ ] **Step 3: Verify pulls, build, and rendering**

Run:

```bash
docker pull nginx:1.28.3-alpine3.23@sha256:a8b39bd9cf0f83869a2162827a0caf6137ddf759d50a171451b335cecc87d236
docker pull curlimages/curl:8.21.0@sha256:7c12af72ceb38b7432ab85e1a265cff6ae58e06f95539d539b654f2cfa64bb13
docker build -t vshelpdesk-web:security frontend
kubectl kustomize deploy/k8s/base | rg \
  'curlimages/curl:8\\.21\\.0@sha256:7c12af72'
```

Expected: pulls/build succeed and exactly two rendered cronjob image matches
appear.

- [ ] **Step 4: Commit**

Run:

```bash
git add \
  frontend/Dockerfile \
  deploy/k8s/base/cronjob-process-incoming-emails.yaml \
  deploy/k8s/base/cronjob-resolve-inactive-tickets.yaml
git commit -m "chore(containers): pin maintained web and job images"
```

### Task 5: Clear the browser-only React Router audit

**Files:**
- Modify: `frontend/package.json`
- Modify: `frontend/package-lock.json`
- Modify: `frontend/src/**`

**Interfaces:**
- Produces: BrowserRouter-compatible `react-router` `8.3.0` without the RSC-only advisory range.

**Execution update (2026-07-29):** npm published additional advisories after
the initial review, making `7.11.0` vulnerable and leaving `7.18.2` in the
RSC-only advisory range. React Router 8 removes `react-router-dom`; the
supported audit-clean migration is therefore direct `react-router` `8.3.0`.

- [ ] **Step 1: Prove no RSC APIs are used**

Run:

```bash
rg -n 'RSCRouterConfig|routeRSCServerRequest|unstable_RSC|ServerRouter|RSCStaticRouter' \
  frontend/src frontend/e2e
```

Expected: no matches.

- [ ] **Step 2: Migrate to the non-advisory router release**

Run:

```bash
cd frontend
npm uninstall react-router-dom
npm install --save-exact react-router@8.3.0
```

Replace `react-router-dom` imports with `react-router`.

Expected: package and lock file select `8.3.0`, with no
`react-router-dom` dependency.

- [ ] **Step 3: Run audit and frontend regressions**

Run:

```bash
npm audit --omit=dev
npm run lint
npm test
env -u VITE_API_BASE_URL npm run build
```

Expected: production dependency audit reports zero vulnerabilities and all
frontend checks pass.

- [ ] **Step 4: Commit**

Run:

```bash
git add frontend/package.json frontend/package-lock.json frontend/src
git commit -m "chore(frontend): migrate to patched router release"
```

### Task 6: Run final security verification

**Files:**
- Verify: repository-wide.

**Interfaces:**
- Consumes: every security plan.
- Produces: a clean, releasable `security/hardening` branch.

- [ ] **Step 1: Run frontend verification**

Run:

```bash
cd frontend
npm ci
npm run lint
npm test
env -u VITE_API_BASE_URL npm run build
npm run test:e2e
npm audit --omit=dev
cd ..
docker build -t vshelpdesk-web:security-final frontend
docker run -d --rm \
  --name vshelpdesk-web-security-final \
  --add-host api:127.0.0.1 \
  -p 127.0.0.1:18080:80 \
  vshelpdesk-web:security-final
cd frontend
PLAYWRIGHT_BASE_URL='http://127.0.0.1:18080' npx playwright test \
  --config playwright.security.config.ts
cd ..
docker stop vshelpdesk-web-security-final
```

Expected: every command exits `0`.

- [ ] **Step 2: Run backend and dependency verification**

Start a clean PostgreSQL service and apply migrations:

```bash
docker run -d --rm \
  --name vshelpdesk-security-final-postgres \
  -e POSTGRES_USER=stajyer \
  -e POSTGRES_PASSWORD=ci_postgres_password \
  -e POSTGRES_DB=VS_HelpDesk_DB \
  -p 127.0.0.1:5432:5432 \
  postgres:16-alpine
docker exec vshelpdesk-security-final-postgres \
  pg_isready -U stajyer -d VS_HelpDesk_DB
dotnet restore VSHelpDesk.slnx
dotnet build VSHelpDesk.slnx --no-restore -c Release
ConnectionStrings__DefaultConnection='Host=localhost;Port=5432;Database=VS_HelpDesk_DB;Username=stajyer;Password=ci_postgres_password' \
  dotnet ef database update \
  --project src/VSHelpDesk.Infrastructure \
  --startup-project src/VSHelpDesk.WebAPI \
  --configuration Release \
  --no-build
CI=true \
ConnectionStrings__DefaultConnection='Host=localhost;Port=5432;Database=VS_HelpDesk_DB;Username=stajyer;Password=ci_postgres_password' \
Auth__SigningKey='ci-signing-key-with-at-least-32-bytes!!' \
Jobs__ApiKey='ci-jobs-api-key-32-characters!!' \
SeedUser__Enabled=true \
SeedUser__Password='CiSeedPassword123!' \
SeedUser__Username=support \
SeedUser__FullName='CI Support' \
SeedUser__Email='support@vshelpdesk.local' \
  dotnet test VSHelpDesk.slnx --no-build -c Release --nologo
dotnet list VSHelpDesk.slnx package --vulnerable --include-transitive
```

Expected: build/test pass and no vulnerable NuGet packages are reported.

- [ ] **Step 3: Rehearse deployable artifacts**

Run:

```bash
docker build -t vshelpdesk-api:security -f Dockerfile .
docker build -t vshelpdesk-web:security frontend
POSTGRES_PASSWORD='compose-test-password' \
AUTH_SIGNING_KEY='compose-signing-key-at-least-32-bytes' \
JOBS_API_KEY='compose-jobs-api-key-32-characters' \
  docker compose -f docker-compose.prod.yml config >/dev/null
kubectl kustomize deploy/k8s/base >/dev/null
kubectl kustomize deploy/k8s/overlays/prod >/dev/null
```

Expected: all builds/renders exit `0`.

- [ ] **Step 4: Stop the temporary database**

Run:

```bash
docker stop vshelpdesk-security-final-postgres
```

Expected: the container stops and is automatically removed.

- [ ] **Step 5: Review finding coverage and diff**

Run:

```bash
for id in SEC-001 SEC-002 SEC-003 SEC-004 SEC-005 SEC-006 SEC-007 SEC-008 SEC-009; do
  rg -q "$id" security_best_practices_report.md || exit 1
done
git diff --check origin/main...HEAD
git status --short --branch
git log --oneline --reverse origin/main..HEAD
```

Expected: every finding is present in the report, no diff errors, clean
worktree, and finding-focused commits are visible.

### Task 7: Push, review, and merge security PR 2

**Files:**
- Deliver: all `security/hardening` commits.

**Interfaces:**
- Produces: GitHub `main` containing frontend and all confirmed security fixes.

- [ ] **Step 1: Push and create the pull request**

Run:

```bash
git push -u origin security/hardening
gh pr create \
  --base main \
  --head security/hardening \
  --title "fix(security): harden sessions, mail, uploads, and deployment" \
  --body-file security_best_practices_report.md
```

Expected: GitHub returns PR 2 URL.

- [ ] **Step 2: Wait for all checks**

Run:

```bash
PR_NUMBER="$(gh pr view --json number --jq .number)"
gh pr checks "$PR_NUMBER" --watch
```

Expected: every required check passes.

- [ ] **Step 3: Merge without deleting the branch**

Run:

```bash
gh pr merge "$PR_NUMBER" --merge --delete-branch=false
git fetch origin
git merge-base --is-ancestor HEAD origin/main
gh pr view "$PR_NUMBER" --json state,mergedAt,url
```

Expected: ancestry check exits `0` and PR state is `MERGED`.

- [ ] **Step 4: Confirm final GitHub state**

Run:

```bash
gh pr list --state merged --limit 5 \
  --json number,title,headRefName,mergeCommit,url
git log --oneline --decorate -8 origin/main
```

Expected: both frontend and security PRs appear merged; no force-push was used.
