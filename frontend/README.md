# VS Help Desk — Frontend (React SPA)

React + Vite SPA. **Only REST** to the ASP.NET Core API (no Next.js / Nuxt).

## Auth choice

| Decision | Value |
|---|---|
| Token | JWT from `POST /api/auth/login` |
| Storage | `sessionStorage` (`vshd.accessToken`) — tab-scoped |
| Header | `Authorization: Bearer <token>` |
| Logout | Clears session keys |
| 401 | Clear session → navigate to `/login?reason=session-expired` |

## API base URL and production same-origin behavior

`.env.development` sets `http://localhost:5154` for local Vite development.
`VITE_API_BASE_URL` is an optional build-time override.
When absent, production calls relative `/api/...` URLs and expects a same-origin reverse proxy.

| Env | Local development | Production build |
|---|---|---|
| `VITE_API_BASE_URL` | From `.env.development` → `http://localhost:5154` | Unset → relative `/api/...` behind reverse proxy |

## Week 3 ticket workspace

Depends on the merged Week 2 portal (login, protected layout, ticket list, four Playwright projects) and the Week 3 backend contracts:

| Route / API | UI behavior |
|---|---|
| `/login` | UC-001 |
| `/tickets` | UC-003 list (protected) |
| `/tickets/:ticketId` | UC-004 detail + timeline + reply slot (protected) |
| `GET /api/tickets/{id}` | Detail + chronological messages + attachment metadata |
| `GET /api/attachments/{id}` | Authenticated Blob download (Bearer header; never token-in-URL) |
| `POST /api/tickets/{id}/replies` | Plain-text support reply body `{ content }` only (no `isHtml`) |

Message bodies render as **literal text** (no HTML injection). Reply limit is **65,536** characters after trim. Saved-versus-delivered outcomes:

| HTTP / notice | Draft | Timeline / status |
|---|---|---|
| Delivered (`emailDelivered` + `ticketStateUpdated`) | cleared | saved Support message; status **Müşteri Bekleniyor** |
| `smtp-delivery-failed` (HTTP 200) | cleared | saved message; status unchanged; delivery warning |
| Pre-send 409 / network / other failures | preserved per composer rules | no false “sent” claim |

## Dev

```bash
# API (repo root)
dotnet run --project src/VSHelpDesk.WebAPI

# SPA
npm install
npm run dev -- --host 127.0.0.1
```

Open http://127.0.0.1:5173 — CORS allows this origin and `http://localhost:5173`.

## Scripts

| Command | Purpose |
|---|---|
| `npm run dev` | Vite dev server |
| `npm run lint` | Lint the SPA sources |
| `npm test` | Unit / component tests (Vitest) |
| `npm run build` | Typecheck + production bundle |
| `npm run preview` | Preview production build |
| `npm run test:e2e` | Production build + Playwright smoke (same-origin preview) |

Browser smoke covers desktop/tablet/mobile viewports (1440×900, 720×900, 390×844, 320×700), same-origin `/api` mocks, keyboard focus, reduced-motion, session expiry, attachment download, reply delivery outcomes, and document overflow. Run Chromium once with `npx playwright install chromium` if needed.

```bash
env -u VITE_API_BASE_URL npm run build
npx playwright test
# Week 3 detail workspace only:
npx playwright test e2e/ticket-detail.smoke.spec.ts
```

## Routes

| Path | Screen |
|---|---|
| `/login` | UC-001 |
| `/tickets` | UC-003 list (protected) |
| `/tickets/:ticketId` | UC-004/005 detail workspace (protected) |
