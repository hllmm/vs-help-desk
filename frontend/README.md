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

## Dev

```bash
# API (repo root)
dotnet run --project src/VSHelpDesk.WebAPI

# SPA
npm install
npm run dev -- --host 127.0.0.1
```

Open http://127.0.0.1:5173 — CORS allows this origin and `http://localhost:5173`.

| Env | Default |
|---|---|
| `VITE_API_BASE_URL` | `http://localhost:5154` |

## Scripts

| Command | Purpose |
|---|---|
| `npm run dev` | Vite dev server |
| `npm run build` | Typecheck + production bundle |
| `npm run preview` | Preview production build |

## Routes (Day 14)

| Path | Screen |
|---|---|
| `/login` | UC-001 |
| `/tickets` | UC-003 list (protected) |

Detail / reply UI → Day 15.
