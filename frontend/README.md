# VS Help Desk — Frontend (React SPA)

React + Vite SPA. **Only REST** to the ASP.NET Core API (no Next.js / Nuxt).

## Auth choice

| Decision | Value |
|---|---|
| Token | JWT from `POST /api/auth/login` |
| Storage | `sessionStorage` (`vshd.accessToken`) — tab-scoped |
| Header | `Authorization: Bearer <token>` |
| Logout | Clears session keys |
| 401 | Clear session → navigate to `/login` |

## API base URL and production same-origin behavior

`.env.development` sets `http://localhost:5154` for local Vite development.
`VITE_API_BASE_URL` is an optional build-time override.
When absent, production calls relative `/api/...` URLs and expects a same-origin reverse proxy.
Routes remain `/login` and `/tickets`; ticket detail/reply remains outside this UI task.

| Env | Local development | Production build |
|---|---|---|
| `VITE_API_BASE_URL` | From `.env.development` → `http://localhost:5154` | Unset → relative `/api/...` behind reverse proxy |

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

Browser smoke covers desktop/tablet/mobile viewports, same-origin `/api` calls, keyboard focus, reduced-motion, session expiry, and document overflow. Run Chromium once with `npx playwright install chromium` if needed.

```bash
env -u VITE_API_BASE_URL npm run build
npx playwright test
```

## Routes

| Path | Screen |
|---|---|
| `/login` | UC-001 |
| `/tickets` | UC-003 list (protected) |

Ticket detail/reply remains outside this UI task.
