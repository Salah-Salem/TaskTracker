# Project Task Tracker — Project Brief

> A one-stop overview of the Task Tracker proof of concept (POC): what it is, who it's for, what it does,
> how it's built, and how to run it.
> The detailed source of truth is [`specs/SETUP_PRO.md`](specs/SETUP_PRO.md). If this brief and that spec ever disagree, the spec wins.

---

## 1. Overview

**Task Tracker** is a small but complete web app for managing projects and their tasks. A team can create projects,
invite members with different permission levels, track tasks from *To do* to *Done*, discuss tasks in comments,
and log the time they spent.

The project is a **proof of concept**. It is meant to show three things working end to end:

1. **Authentication**: register, sign in, silent session renewal, sign out, and sessions that are cut off immediately when needed.
2. **Authorization** on two layers:
   - a **global role** (`Admin` / `User`) for system administration, and
   - a **per-project role** (`Owner` / `Member` / `Viewer`) that is checked on every project action.
3. **CRUD + search**: projects, tasks, comments and time logs, with search, filtering, sorting and paging.

**Deliverables:** one REST API (ASP.NET Core) and one single-page app (React).
The app is not built for production. See *Out of scope* (§11).

| Milestone | Status |
|---|---|
| 1. Skeleton: solution, EF Core + SQLite, Identity, JWT, Swagger, `/health` | ✅ Done |
| 2. Auth: register / login / logout / me, per-request session check | ✅ Done |
| 3. Global roles + admin user management, seed data | ✅ Done |
| 4. Per-project roles, refresh tokens, projects, tasks, search, comments, time logs, my tasks | ✅ Done |
| 5. React frontend with role-aware UI | ✅ Done |
| 6. Hardening: coverage report (≥ 70%), README with curl examples | ⏳ Pending |

---

## 2. Technologies used

### Backend
| Concern | Technology |
|---|---|
| Runtime / language | **.NET 10** (LTS), C# with nullable reference types |
| Web framework | ASP.NET Core Web API (controllers) |
| Identity & passwords | ASP.NET Core Identity (PBKDF2 hashing, account lockout) |
| Authentication | JWT Bearer access tokens + rotating refresh tokens |
| Authorization | Policy-based and resource-based authorization handlers |
| Data access | Entity Framework Core 10 (code-first migrations) |
| Database | SQLite (`app.db`). Can be switched to SQL Server. |
| Validation | FluentValidation 11 (auto-validation) |
| Logging | Serilog (console) |
| API docs | Swagger / Swashbuckle, with a Bearer **Authorize** button |
| JSON | camelCase, enums sent as strings |

### Frontend (`web/`)
| Concern | Technology |
|---|---|
| UI library | React 18 + TypeScript 5 |
| Build tool / dev server | Vite 5 (proxies `/api` to the backend, so the API needs no CORS setup) |
| Routing | React Router 6 |
| HTTP | Axios, with automatic token refresh and response-envelope unwrapping |
| Styling | Plain CSS with light/dark theme tokens |

### Testing
| Layer | Tools |
|---|---|
| API | xUnit + `WebApplicationFactory` + FluentAssertions. Each test class gets its own in-memory SQLite DB. About 87 tests. |
| Web | Vitest unit tests: refresh logic, query building, PATCH diffing, permissions |

### Solution layout
```
TaskTracker.slnx
src/
  TaskTracker.Domain/          Entities, enums, global role names
  TaskTracker.Application/     DTOs, validators, service interfaces, ApiResponse, Optional<T>
  TaskTracker.Infrastructure/  DbContext, migrations, Identity, JWT/refresh tokens, services, seeder
  TaskTracker.Api/             Program.cs, controllers, authorization policies, Swagger
tests/
  TaskTracker.Api.Tests/       Integration + acceptance tests
web/                           React SPA
specs/                         Specifications (SETUP_PRO.md = source of truth, TASKS.md, baseline spec)
```

**Deliberately not used:** MediatR, CQRS, DDD aggregates, GraphQL, gRPC, Docker/Kubernetes, Redis, message queues.
The design is kept flat and simple on purpose.

---

## 3. User types

### Global roles (system level)
| Role | Who | What they can do |
|---|---|---|
| **Admin** | System administrator | List users, change a user's global role, activate or deactivate accounts. **Acts as Owner on every project** for support and moderation. |
| **User** | Everyone else. New registrations get this role. | Create projects. Everything else depends on their role inside each project. |

### Project roles (per project, ranked Owner > Member > Viewer)
| Role | Meaning |
|---|---|
| **Owner** | Exactly one per project. Manages the project and its members, deletes tasks, and moderates comments and time logs. |
| **Member** | Contributor. Creates and edits tasks, comments, and logs time. Cannot delete tasks. |
| **Viewer** | Read-only. Cannot be assigned tasks. |
| *Non-member* | Sees nothing. The project behaves as if it doesn't exist (404). |

### Permission matrix
| Operation | Owner | Member | Viewer | Non-member |
|---|:--:|:--:|:--:|:--:|
| List my projects / create a project | ✅ | ✅ | ✅ | ✅ (creator becomes Owner) |
| View project, list members | ✅ | ✅ | ✅ | 404 |
| Update / delete project | ✅ | 403 | 403 | 404 |
| Add, change or remove members; transfer ownership | ✅ | 403 | 403 | 404 |
| List / view / search tasks | ✅ | ✅ | ✅ | 404 |
| Create / edit / assign tasks | ✅ | ✅ | 403 | 404 |
| Delete tasks | ✅ | 403 | 403 | 404 |
| Read comments and time logs | ✅ | ✅ | ✅ | 404 |
| Add a comment / log time | ✅ | ✅ | 403 | 404 |
| Edit or delete a comment or time log | ✅ any | own only | own only | 404 |
| My tasks (`/api/me/tasks`) | Any signed-in user. Shows only tasks assigned to them. ||||
| User administration (`/api/users`) | Global Admin only. Everyone else gets 403. ||||

**Rule of thumb:** a non-member gets **404**, so the system never reveals that a project exists. A member whose role is too low gets **403**.

---

## 4. User stories

### Epic A — Authentication
| ID | Story |
|---|---|
| US-A1 | As a visitor, I can **register** with email, display name and password so that I get an account with the `User` role. |
| US-A2 | As a user, I can **sign in** with email and password. A failure shows a generic "Invalid email or password". A disabled account is rejected. 5 failed attempts lock the account for 15 minutes. |
| US-A3 | As a user, I can **sign out**, and **all** my sessions end immediately. |
| US-A4 | As the system, I reject requests with a missing, invalid or expired token (401) and never expose secrets in responses or logs. |
| US-A6 | As a user, my session **renews silently** with a single-use refresh token. Reusing an old token revokes all my sessions. |
| US-A5 | *Change password: optional, not built.* |

### Epic B — Authorization
| ID | Story |
|---|---|
| US-B1 | As a project Owner, I control what Members and Viewers can do through per-project roles. |
| US-B2 | As an Admin, I can list users, change their global role, and activate or deactivate accounts. The last active Admin cannot be demoted or deactivated. |
| US-B3 | As a user, the UI only shows actions my role allows. This is cosmetic only; the API enforces every rule. |
| US-B4 | As a user, I only see data from projects I belong to. |

### Epic C — Projects, tasks & collaboration
| ID | Story |
|---|---|
| US-C1 | As a Member/Owner, I can **create a task** (title, description, priority, assignee, due date). It starts as *To do*. |
| US-C2 | As any project member, I can **view and list** tasks. |
| US-C3 | As a Member/Owner, I can **edit** a task partially. Moving it to *Done* records the completion time, and moving it out of *Done* clears it. |
| US-C4 | As an Owner, I can **delete** a task. |
| US-C5 | As a member, I can **search** tasks by text in the title or description. |
| US-C6 | As a member, I can **filter and sort** tasks by status, priority, assignee and due-date range. |
| US-C7 | As a user, I can **create projects** and manage membership: add by email, change roles, remove members, transfer ownership. |
| US-C8 | As a Member/Owner, I can **comment** on tasks and edit or delete my own comments. The Owner can moderate any comment. |
| US-C9 | As a Member/Owner, I can **log time** on tasks and edit or delete my own logs. The Owner can moderate any log. |
| US-C10 | As a user, I can see **my tasks** across all my projects, each showing its project name. |

---

## 5. Requirements

### 5.1 Functional requirements (key points)

**Accounts & sessions**
- Email must be unique and is stored lowercase. Display name has at most 100 characters. Password has **at least 8 characters**, with no other complexity rules.
- Access token (JWT) is valid for **15 minutes**. Refresh token is valid for **7 days**, is single-use and rotates, and is stored only as a SHA-256 hash.
- Every request re-checks that the user exists, is active, and that their session has not been revoked. This makes sign-out, deactivation and role changes take effect immediately.
- Sign-out ends every session. Deactivating a user ends every session. A role change invalidates the current access token, and the next refreshed token carries the new role.

**Projects & membership**
- Name is required (≤ 100 characters). Description is optional (≤ 1000 characters). The creator becomes Owner.
- **Exactly one Owner per project.** The Owner role can only be passed on through *transfer ownership*; the old Owner becomes a Member.
- Conflicts return **409**: adding someone who is already a member, granting Owner directly, or removing or demoting the Owner.
- Transferring to a non-member returns 400. Adding an unknown email returns 404 "User not found".
- Demoting a member to Viewer, or removing them, **unassigns their tasks** in that project.
- Deleting a project is a soft delete. Its tasks, comments and time logs disappear from every view.

**Tasks**
- Title is required (≤ 200 characters). Description has at most 4000 characters.
- Status: `Todo · InProgress · InReview · Done · Cancelled`. Any transition is allowed.
- Priority: `Low · Medium · High · Critical`. The default is Medium.
- The assignee must be an Owner or Member: a non-member returns **400**, and a Viewer returns **422**.
- Edits are partial (PATCH). A missing field is left unchanged. Sending `null` clears the description, assignee or due date.
- Deleting a task is a soft delete and only the Owner can do it.

**Search, filter, sort, paging**
- `search`: case-insensitive match on title or description. Wildcard characters are treated as literal text.
- `status` and `priority` accept several values (repeated, or comma-separated). `assigneeId` accepts a user id or `none`. `dueBefore` / `dueAfter` are inclusive.
- `sortBy`: `title | status | priority | dueDate | createdAt | updatedAt`. `sortDir`: `asc | desc`. The default is `createdAt desc`.
- Paging defaults to page 1 with 20 items, with a maximum of 100. Invalid values return **400** (never 500).

**Comments & time logs**
- A comment is 1–2000 characters. Comments are listed newest first.
- A time log is 1–1440 minutes, has a work date (default today, not in the future), and an optional note (≤ 500 characters). The log always belongs to the caller.

**Administration**
- Admin can see a paged list of users, change a user's role, and activate or deactivate accounts. The last active Admin is protected.
- Security-relevant actions are audit-logged.

### 5.2 Non-functional requirements
- **Security:** passwords, JWTs, refresh-token hashes and security stamps never appear in responses or logs. All SQL is parameterized.
- **Consistent API contract:** every response uses the envelope `{ "success": bool, "data": ..., "errors": [] }`.
  Lists return `{ items, page, pageSize, totalCount }`.
- **Status codes:** `400` validation · `401` unauthenticated · `403` forbidden · `404` not found / not a member · `409` conflict · `422` business rule · `500` unexpected (no stack traces).
- **Quality gate (Definition of Done):** the code builds with `-warnaserror`. Every story has happy-path and failure-path integration tests, plus a 403 role test and a 404 non-member test.

---

## 6. API at a glance

All routes are under `/api`. All require `Authorization: Bearer <token>` except register, login and refresh.
The full interactive docs are at **`/swagger`**.

| Area | Endpoints |
|---|---|
| Auth | `POST /auth/register` · `POST /auth/login` · `POST /auth/refresh` · `POST /auth/logout` · `GET /auth/me` |
| Users (Admin) | `GET /users` · `PUT /users/{id}/role` · `PUT /users/{id}/deactivate` · `PUT /users/{id}/activate` |
| Projects | `GET/POST /projects` · `GET/PATCH/DELETE /projects/{id}` |
| Members | `GET/POST /projects/{id}/members` · `PATCH/DELETE /projects/{id}/members/{userId}` · `POST /projects/{id}/transfer-ownership` |
| Tasks | `GET/POST /projects/{id}/tasks` · `GET/PATCH/DELETE /projects/{id}/tasks/{taskId}` |
| My tasks | `GET /me/tasks` |
| Comments | `GET/POST …/tasks/{taskId}/comments` · `PATCH/DELETE …/comments/{commentId}` |
| Time logs | `GET/POST …/tasks/{taskId}/timelogs` · `PATCH/DELETE …/timelogs/{logId}` |
| Health | `GET /health` (anonymous) |

---

## 7. How to run the project

### Prerequisites
- **.NET 10 SDK**: check with `dotnet --version`
- **Node.js 18+** and npm: check with `node --version`
- Optional: the `dotnet-ef` tool, only if you want to create new migrations

### Step 1: Start the API
From the repository root:
```bash
dotnet run --project src/TaskTracker.Api --launch-profile http
```
- The API listens on **http://localhost:5050**. Swagger is at **http://localhost:5050/swagger**.
- In Development the app **applies migrations and seeds demo data automatically** on startup. No manual database setup is needed.
- To reset the demo data, stop the API, delete the SQLite file `app.db`, and start the API again.

### Step 2: Start the web app
In a second terminal:
```bash
cd web
npm install
npm run dev
```
- Open **http://localhost:5173**.
- The Vite dev server forwards `/api` calls to `http://localhost:5050`. If the API runs on a different port, create `web/.env.local` containing:
  ```
  VITE_API_TARGET=http://localhost:5051
  ```

### Step 3: Sign in with a demo account
All demo accounts use the password **`Passw0rd!`**.

| Email | Global role | Role in the "Website Redesign" project |
|---|---|---|
| `admin@example.com` | Admin | Acts as Owner everywhere |
| `alice@example.com` | User | **Owner** |
| `bob@example.com` | User | **Member** |
| `carol@example.com` | User | **Viewer** |
| `dave@example.com` | User | Not a member. Useful for seeing the 404 behaviour. |

The seeded project **"Website Redesign"** has 5 tasks (one per status), 2 comments and 2 time logs.

### Run the tests
```bash
dotnet test                 # API integration + acceptance tests (from the repo root)
cd web && npm test          # Frontend unit tests (Vitest)
```

### Other useful commands
| Command | Purpose |
|---|---|
| `dotnet build -warnaserror` | Build check used by the Definition of Done |
| `cd web && npm run build` | Type-check and create a production build in `web/dist/` |
| `cd web && npm run preview` | Serve the production build (also proxies `/api`) |
| `cd web && npm run typecheck` | TypeScript check only |

### Configuration
| Setting | Location | Default |
|---|---|---|
| Connection string | `src/TaskTracker.Api/appsettings.Development.json` → `ConnectionStrings:Default` | `Data Source=app.db` |
| JWT issuer / audience / key | same file → `Jwt` section | Development-only key. **Replace it** (user-secrets or Key Vault) for any real deployment. |
| Token lifetimes | `Jwt:AccessTokenMinutes`, `Jwt:RefreshTokenDays` | 15 min / 7 days |
| Log levels | `Serilog` section | Information |

> Swagger and demo-data seeding are enabled **only** when `ASPNETCORE_ENVIRONMENT=Development` (set by the `http`/`https` launch profiles).

---

## 8. How to use the app

1. **Sign in** (or register a new account). The login page lists the demo accounts in dev mode.
2. **Projects** page: see the projects you belong to. You can search, filter by your role, sort, and create a new project. You become its Owner.
3. Open a project. It has three tabs:
   - **Tasks:** search, filter by status, priority, assignee or due date, sort, and page through tasks. Create, edit or delete tasks (depending on your role). Click a task to see its details, **comments** and **time logs**.
   - **Members** (visible to everyone, editable only by the Owner): add people by email as Member or Viewer, change their roles, remove them, or **transfer ownership**.
   - **Settings** (Owner only): rename the project, edit its description, or delete it.
4. **My tasks:** every task assigned to you, across all your projects.
5. **Admin → Users** (Admin only): list users, promote or demote them, and activate or deactivate accounts.

Filters, sort order, page and the open task are stored in the URL, so you can reload or share a view.
The UI hides buttons your role can't use, and the API still enforces every rule.

**Try this walkthrough** to see the permission model in action:
sign in as Carol (Viewer) and notice there are no edit buttons. Sign in as Bob (Member): create a task, mark it Done, and try to delete it (denied).
Sign in as Alice (Owner): delete it, then transfer ownership to Bob. Sign in as Dave: the project doesn't exist for him.

---

## 9. Security notes (POC trade-offs)
- In the SPA, tokens are kept in `localStorage` so a page reload keeps you signed in. A production system would keep the refresh token in an HttpOnly cookie.
- The JWT signing key in `appsettings.Development.json` is for development only.
- Seed data and Swagger are enabled only in Development.

---

## 10. Testing & acceptance
- Tests are organized by area: `Auth/`, `Users/`, `Projects/`, `Tasks/`, `Activity/` (comments and time logs), `Seeding/`, `Acceptance/`.
- `tests/TaskTracker.Api.Tests/Acceptance/TasksMdAcceptanceTests.cs` automates the 20-step demo script (S01–S20): role enforcement, ownership transfer, search, token refresh, admin promotion and deactivation.
- Each test class runs against its own in-memory SQLite database, so the dev `app.db` is never touched.

---

## 11. Out of scope
Password reset by email, SSO/OAuth, multi-tenancy, file attachments, activity feed, notifications, real-time updates,
recycle bin or hard delete for projects and tasks, reporting, mobile app, i18n, Docker/Kubernetes, CI/CD.

---

## 12. Reference documents
| Document | Purpose |
|---|---|
| [`specs/SETUP_PRO.md`](specs/SETUP_PRO.md) | **Source of truth**: full domain model, DTOs, API contract, permissions, Definition of Done |
| [`specs/TASKS.md`](specs/TASKS.md) | Original task/role model spec. Deliberate deviations are listed in SETUP_PRO §19. |
| [`specs/001-task-tracker-baseline/spec.md`](specs/001-task-tracker-baseline/spec.md) | As-built feature spec with user stories, acceptance scenarios and functional requirements FR-001 to FR-040 |
| [`web/README.md`](web/README.md) | Frontend structure and scripts |
