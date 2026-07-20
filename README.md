# VS Help Desk

Staj eğitim projesi: destek e-postalarından otomatik ticket oluşturma ve destek personeli portalı.

Kaynak: **VS Help Desk — SRD & Sistem Tasarımı** (`VSHD-SRD-001` v1.0).

## Hızlı başlangıç

```bash
# Altyapı (PostgreSQL + Mailpit)
docker compose up -d

# API (user-secrets: ConnectionStrings + SeedUser:Password + Auth/Jobs secrets)
dotnet restore
dotnet run --project src/VSHelpDesk.WebAPI

# Portal SPA (ayrı terminal)
cd frontend && npm install && npm run dev -- --host 127.0.0.1

# Testler
dotnet test
cd frontend && npm run build
```

| Servis | Adres |
|--------|--------|
| API | `http://localhost:5154` / `https://localhost:7269` (launchSettings) |
| Portal | http://127.0.0.1:5173 (Vite; CORS: `Cors:AllowedOrigins`) |
| PostgreSQL | `localhost:5432` — db: `VS_HelpDesk_DB`, user: `stajyer` |
| Mailpit UI | http://localhost:8025 |
| Mailpit SMTP | `localhost:1025` |
| GreenMail (profile `imap-test`) | SMTP `localhost:3025`, IMAP `localhost:3143` |

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
| **4** | Otomatik resolve, manuel resolve/reopen, test/demo (UC-007…009) | Features/ScheduledJobs, ResolveTicket, ReopenTicket; Jobs endpoint |

- **20 iş günlük şahsi takip planı:** [docs/gunluk-plan.md](docs/gunluk-plan.md)
- **Detaylı 4 haftalık teknik plan (PDF §10 uyumlu):** [docs/haftalik-plan.md](docs/haftalik-plan.md)
- Kısa klasör ↔ hafta haritası: [docs/weekly-plan.md](docs/weekly-plan.md)

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
