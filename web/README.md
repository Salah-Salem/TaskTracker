# Task Tracker — web (React 18 + Vite + TypeScript)

SPA for the Task Tracker POC. Talks to the API through relative `/api/...` URLs; the Vite dev/preview
server proxies them to the API, so no CORS setup is needed.

## Run

```bash
# 1. API (from the repo root) — http://localhost:5050
dotnet run --project src/TaskTracker.Api --launch-profile http

# 2. Web (from web/) — http://localhost:5173
npm install
npm run dev
```

API on another port? Create `web/.env.local` with `VITE_API_TARGET=http://localhost:5051`.

Demo accounts (password `Passw0rd!`): `alice@example.com` (Owner), `bob@example.com` (Member),
`carol@example.com` (Viewer), `dave@example.com` (not a member), `admin@example.com` (Admin).
The login page lists them in dev mode.

## Scripts

| Command | What it does |
|---|---|
| `npm run dev` | Dev server with hot reload |
| `npm run build` | Type-check + production build into `dist/` |
| `npm run preview` | Serve `dist/` (also proxies `/api`) |
| `npm test` | Unit tests (Vitest): API client refresh logic, query building, PATCH diffing, permissions |

## Structure

```
src/
  api/        client.ts (axios + token refresh + envelope unwrapping), endpoints.ts, session.ts, types.ts
  auth/       AuthContext (useAuth), login/register pages, route guards
  components/ Layout, Modal, ConfirmDialog, badges, pagination…
  lib/        permissions.ts (UI mirror of the role matrix), hooks.ts
  pages/      Projects, Project (tasks / members / settings tabs), My tasks, Admin users
  tasks/      Filters + table, create/edit form, detail view with comments & time logs
```

## Notes

- **Role-aware UI is cosmetic.** `lib/permissions.ts` only decides what to show; the API enforces every rule.
- **Session:** tokens are kept in `localStorage` so a reload keeps you signed in. On a 401 the client
  exchanges the refresh token once (shared by concurrent requests) and retries; if that fails you're sent
  to the login page. POC trade-off: production would keep the refresh token in an HttpOnly cookie.
- Filters, sort, page and the open task live in the URL, so views survive a reload and can be shared.
