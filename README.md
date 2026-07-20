# VS Help Desk

Staj eğitim projesi: destek e-postalarından otomatik ticket oluşturma ve destek personeli portalı.

Kaynak: **VS Help Desk — SRD & Sistem Tasarımı** (`VSHD-SRD-001` v1.0).

## Production (şirket içi)

İç kullanım deploy: [docs/deploy-production.md](docs/deploy-production.md)  
(`docker compose -f docker-compose.prod.yml`, secrets env, CI, TLS proxy).

Sonraki fazlar (cookie auth, K8s, multi-tenant): design roadmap in  
`docs/superpowers/specs/2026-07-20-production-hardening-design.md`.  
UC-010 parametre yönetimi (Faz 1) bu dalda: `GET/PUT /api/parameters` + portal **Parametreler**.

## Hızlı başlangıç (sırayla)

```bash
# 1) Altyapı (PostgreSQL + Mailpit) — veriyi silmez; down -v kullanma
docker compose up -d postgres mailpit

# 2) Connection string (şifreyi notlara yazma; örnek placeholder)
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=VS_HelpDesk_DB;Username=stajyer;Password=..."

# 3) Migration + seed login (SeedUser:Password user-secrets / env)
dotnet tool restore
dotnet ef database update \
  --project src/VSHelpDesk.Infrastructure \
  --startup-project src/VSHelpDesk.WebAPI

# 4) API (Auth:SigningKey, Jobs:ApiKey, SeedUser:Password)
dotnet restore VSHelpDesk.slnx
dotnet run --project src/VSHelpDesk.WebAPI

# 5) Portal SPA (ayrı terminal)
cd frontend && npm install && npm run dev -- --host 127.0.0.1
```

Seed kullanıcı: username `support` (Development `SeedUser`); parola yalnız `SeedUser:Password` kaynağından.

| Servis | Adres |
|--------|--------|
| API | `http://localhost:5154` / `https://localhost:7269` (launchSettings) |
| Health | `GET http://localhost:5154/health` |
| Portal (dev) | http://127.0.0.1:5173 (Vite; CORS: `Cors:AllowedOrigins`) |
| Portal (prod) | Same-origin with API via reverse proxy; relative `/api/...` when `VITE_API_BASE_URL` is unset |
| PostgreSQL | `localhost:5432` — db: `VS_HelpDesk_DB`, user: `stajyer` |
| Mailpit UI | http://localhost:8025 |
| Mailpit SMTP | `localhost:1025` |
| GreenMail (profile `imap-test`) | SMTP `localhost:3025`, IMAP `localhost:3143` |

### Test komutları

```bash
# Backend
dotnet restore VSHelpDesk.slnx
dotnet build VSHelpDesk.slnx --no-restore
dotnet test VSHelpDesk.slnx --no-build

# Frontend (repo kökünden)
cd frontend
npm ci
npm run lint
npm test
env -u VITE_API_BASE_URL npm run build
npx playwright test   # Week 2 + 3 + 4, dört viewport projesi
```

### Portal SPA

- Local Vite development uses `.env.development` → `VITE_API_BASE_URL=http://localhost:5154`.
- `VITE_API_BASE_URL` is an optional build-time override.
- When absent, the production bundle calls relative `/api/...` URLs and expects a same-origin reverse proxy.
- Routes: `/login`, `/tickets` (list), `/tickets/:ticketId` (detail + timeline + reply + resolve), `/parameters` (UC-010 list/edit allowlisted keys).
- Detail messages render as literal text; attachments download via authenticated Blob + Bearer header (no token in URL).
- Support reply: `POST /api/tickets/{id}/replies` with `{ content }` only; max **65,536** characters; saved-vs-delivered outcomes include SMTP failure warning without status change.
- Manual resolve (detail): confirm dialog → `POST /api/tickets/{id}/resolve` (no body); server-confirmed **Çözüldü** state; reply composer hidden while resolved.
- Frontend scripts: `npm run lint`, `npm test`, `npm run build`, `npm run test:e2e` (see [frontend/README.md](frontend/README.md)).
- Browser evidence: `portal.smoke.spec.ts` (Week 2), `ticket-detail.smoke.spec.ts` (Week 3), `ticket-resolution.smoke.spec.ts` (Week 4) — four Playwright projects.

## Ticket çözümleme ve reopen (Hafta 4)

| Endpoint / kural | Davranış |
|------------------|----------|
| `POST /api/tickets/{id}/resolve` | Bearer auth; **gövde yok**; açık ticket’ı manuel kapatır; `ClosedByUserId` = oturum kullanıcısı; zaten `Resolved` ise **idempotent** (orijinal closer/timestamp korunur) |
| Resolved + destek yanıtı | Kalıcılık/SMTP öncesi reddedilir (HTTP 409) — müşteri e-postası reopen edene kadar |
| `POST /api/jobs/resolve-inactive-tickets` | `X-Jobs-Api-Key` zorunlu; JWT yok; eşik `AutoResolve.InactiveDays` (varsayılan 3, 1–30); `Status == WaitingCustomerReply` ve `WaitingCustomerSince <= now - days` |
| Otomatik kapanış | `ClosedByUserId` **null** (sistem); `ResolvedAt` set |
| Reopen | Yalnızca müşteri e-postası; kanonik ticket numarası + müşteri kimliği + idempotency (Message-ID / receipt); subject değişmez; manuel reopen UI/API **yok** |
| `GET/PUT /api/parameters` | Bearer auth; allowlist katalog (şimdilik `AutoResolve.InactiveDays`); bilinmeyen key → 404; geçersiz değer → 400; SMTP sırları DB’de **yok** |
| Atama | **Uygulanmadı** — public assign rotası yok |

Zamanlayıcı kurulumu uygulama **dışındadır**. Örnek dış çağrı / cron (yer tutucu anahtar):

```bash
curl -sS -X POST http://localhost:5154/api/jobs/resolve-inactive-tickets \
  -H "X-Jobs-Api-Key: <JOBS_API_KEY>"

curl -sS -X POST http://localhost:5154/api/jobs/process-incoming-emails \
  -H "X-Jobs-Api-Key: <JOBS_API_KEY>"
```

```cron
# Kurulum kapsam dışı — yalnızca örnek
0 * * * * curl -sS -X POST https://<API_HOST>/api/jobs/resolve-inactive-tickets -H "X-Jobs-Api-Key: <JOBS_API_KEY>"
0 * * * * curl -sS -X POST https://<API_HOST>/api/jobs/process-incoming-emails -H "X-Jobs-Api-Key: <JOBS_API_KEY>"
```

Demo akışı ve bilinen kısıtlar: [docs/demo-runbook.md](docs/demo-runbook.md), [docs/known-limitations.md](docs/known-limitations.md).
## Mail / job yapılandırması (Hafta 2 hardening)

### Receiver modes

| `Email:ReceiverMode` | Ortam | Not |
|----------------------|--------|-----|
| `Fake` | yalnızca **Development** veya **Testing** | Deterministik örnekler; Production/Staging startup fail |
| `Imap` | her ortam | `Email:Imap*` zorunlu |

Transport güvenlik modları (`Email:SmtpSecurityMode`, `Email:ImapSecurityMode`): **`None`**, **`StartTls`**, **`SslOnConnect`**. `None` yalnızca Development/Testing’te izinlidir.

### Ortam anahtarları (örnek)

```bash
# Connection (şifreyi shell’e basmadan)
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=VS_HelpDesk_DB;Username=stajyer;Password=..."

# Auth / Jobs
export Auth__SigningKey="..."          # ≥32 UTF-8 byte
export Jobs__ApiKey="..."              # ≥16 karakter

# Fake + Mailpit (Development varsayılanı appsettings.Development.json)
# Email__ReceiverMode=Fake
# Email__SmtpHost=localhost Email__SmtpPort=1025 Email__SmtpSecurityMode=None

# Gerçek IMAP (GreenMail veya operasyonel kutu)
export Email__ReceiverMode=Imap
export Email__ImapHost=localhost
export Email__ImapPort=3143
export Email__ImapSecurityMode=None
export Email__ImapUsername=support@vshelpdesk.test
export Email__ImapPassword=test
export Email__ImapAccountId=greenmail-support
export Email__ImapFolder=INBOX
export Email__SmtpHost=localhost
export Email__SmtpPort=3025
export Email__SmtpSecurityMode=None
export Email__SupportMailboxAddress=support@vshelpdesk.test
```

### Kimlik: receipt handle vs Message-ID

- **Receipt handle** (IMAP mark Seen / Fake mark): `fake\0{fixtureId}` veya `imap\0{accountId}\0{folder}\0{uidValidity}\0{uid}` — mailbox konumunu taşır; log’lanmaz.
- **Idempotency key** (DB unique `UX_ProcessedEmailMessages_IdempotencyKey`): geçerli RFC Message-ID token’ı (`<left@right>`, max 998) varsa o; yoksa `receipt:{kind}:{sha256(handle)}`.

### Acknowledgement (BR-002)

Yeni ticket ack’i ticket commit’ten sonra gider; SMTP hata yolunda ticket kalır, durum `Pending`/`Failed` saklanır. Otomatik retry gecikmeleri: **1 → 5 → 15 → 60 dk** (sonrası 60 dk). Teslimat **at-least-once** (aynı pencerede mükerrer ack mümkün).

### Job lease

`POST /api/jobs/process-incoming-emails` PostgreSQL session advisory lock (`6220394968519887180`) ile tek uçuştur. Contention → **HTTP 409**. Receiver/infra `Result` failure → **HTTP 502**.

### Ticket numarası

`VS-000001` … `VS-999999`; PostgreSQL sequence `MAXVALUE 999999` (cycle yok).

### GreenMail opt-in test

```bash
docker compose --profile imap-test up -d postgres mailpit greenmail
export VSHD_RUN_IMAP_TESTS=true
export ConnectionStrings__DefaultConnection="..."
dotnet test tests/VSHelpDesk.Infrastructure.UnitTests --filter "ImapEmailReceiverIntegration"
dotnet test tests/VSHelpDesk.WebAPI.IntegrationTests --filter "ImapProcessIncomingEmails"
```

`VSHD_RUN_IMAP_TESTS` yoksa GreenMail testleri skip olur. PostgreSQL yoksa yerel suite skip; `CI=true` iken connection zorunlu (fail-loud).

## Solution yapısı

```
vs-help-desk/
├── src/
│   ├── VSHelpDesk.Domain/           # Entity, enum, domain kuralları
│   ├── VSHelpDesk.Application/      # Use case'ler (Features), arayüzler
│   ├── VSHelpDesk.Infrastructure/   # EF, mail, auth, dosya saklama
│   └── VSHelpDesk.WebAPI/           # REST API, middleware
├── tests/                           # Unit + integration
├── frontend/                        # React SPA (3. hafta)
├── storage/                         # Ekler — wwwroot dışı (BR-017)
├── docs/                            # Mimari + haftalık plan notları
└── docker-compose.yml
```

Bağımlılık yönü: **Domain ← Application ← Infrastructure ← WebAPI** (detay: [docs/architecture.md](docs/architecture.md)).

## 4 haftalık plan (SRD §10) — bu iskelet nasıl hizalanır

| Hafta | Odak (SRD) | Bu repoda nereye bak |
|-------|------------|----------------------|
| **1** | Ortam, DB şeması, User + Login (UC-001) | Domain/User, Infrastructure/Persistence + Authentication, Features/Authentication, Controllers |
| **2** | Ticket/Message, e-posta alma (UC-002), eşleştirme (BR-005) | Domain/Ticket*, Features/MailProcessing + Tickets/CreateTicket, Infrastructure/Email |
| **3** | Portal list/detail/reply, ekler (UC-003…005, BR-012) | Features/Tickets/*, Features/Attachments, frontend/, Infrastructure/Storage |
| **4** | Otomatik resolve, manuel resolve/reopen, test/demo (UC-007…009) | `ResolveTicket`, `ResolveInactiveTickets`, mail reopen (UC-009); korumalı Jobs endpoint; portal resolve UI |

- **20 iş günlük şahsi takip planı:** [docs/gunluk-plan.md](docs/gunluk-plan.md)
- **Detaylı 4 haftalık teknik plan (PDF §10 uyumlu):** [docs/haftalik-plan.md](docs/haftalik-plan.md)
- Kısa klasör ↔ hafta haritası: [docs/weekly-plan.md](docs/weekly-plan.md)
- Demo runbook / bilinen kısıtlar: [docs/demo-runbook.md](docs/demo-runbook.md), [docs/known-limitations.md](docs/known-limitations.md)

## Kısıtlar (SRD §2.5)

- Backend: ASP.NET Core (Node/Nest **yok**)
- Frontend: React SPA **veya** SSR; Next/Nuxt **yok**
- Zamanlanmış işler uygulama içinde değil; **dış zamanlayıcı → HTTP endpoint**

## İş kuralları

Geliştirmede ilgili koda `// BR-xxx` yorumu eklenir (SRD §8, BR-001 … BR-022).

## Definition of Done (SRD §10.2)

- Use case uçtan uca çalışıyor ve manuel test edildi
- Kod review + BR doğrulandı / kodda referanslandı
- Kritik hata yok; değişiklik develop/main’e merge
