# VS Help Desk

Staj eğitim projesi: destek e-postalarından otomatik ticket oluşturma ve destek personeli portalı.

Kaynak: **VS Help Desk — SRD & Sistem Tasarımı** (`VSHD-SRD-001` v1.0).

## Hızlı başlangıç

```bash
# Altyapı (PostgreSQL + Mailpit)
docker compose up -d

# API
dotnet restore
dotnet run --project src/VSHelpDesk.WebAPI

# Testler
dotnet test
```

| Servis | Adres |
|--------|--------|
| API | `http://localhost:5154` / `https://localhost:7269` (launchSettings) |
| PostgreSQL | `localhost:5432` — db: `VS_HelpDesk_DB`, user: `stajyer` |
| Mailpit UI | http://localhost:8025 |
| Mailpit SMTP | `localhost:1025` |

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
