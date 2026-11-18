# Project Task Tracker — POC Brief (.NET / C#)

> **Merged spec (2026-11-02).** This document merges the original SETUP_PRO brief with `TASKS.md`
> (per-project roles, refresh tokens, comments, time logs, `/api/me/tasks`, search/sort).
> **This file is the source of truth.** `TASKS.md` is kept as the reference for the project-role
> model; where the two differ, this file wins — see Section 19 for the list of deliberate deviations.

## 0. How to use this document with Claude

- Paste this entire file as context (project knowledge or first message).
- Ask Claude to build **one milestone at a time** (Section 11). Do not ask for "the whole app" — quality degrades.
- Ask Claude to output **run-ready code**, including `.csproj` if a new project is added.
- If Claude proposes changes to the model, endpoints, or roles, ask it to update this doc first so context stays in sync.
- Treat Section 14 (Definition of Done) as the merge gate for every story.
- Sections **4, 6, 7, 9, 14** are the "contract" — when Claude drifts, point it back here.

---

## 1. Project summary

A minimal-but-complete **Project Task Tracker** POC proving out:
1. **Authentication** — register, login, refresh, logout, session invalidation
2. **Authorization** — two layers:
   - **Global role** (`Admin` / `User`) — system administration only
   - **Per-project role** (`Owner` / `Member` / `Viewer`) — resource-based checks on every project-scoped action, plus author-or-Owner checks on comments and time logs
3. **CRUD + search** — Projects, Tasks, Comments, Time Logs; search, filter, sort, paginate

**Scope:** thin end-to-end slice. Not production. One deployable API + one SPA.

---

## 2. Technology stack (locked)

| Layer | Choice | Version |
|---|---|---|
| Runtime | .NET | 10.0 (LTS) — changed from 8.0: only the .NET 9/10 SDKs are installed |
| API | ASP.NET Core Web API (controllers) | 10.0 |
| Auth | ASP.NET Core Identity (user store, PBKDF2, lockout) + JWT Bearer + refresh tokens | 10.0 |
| ORM | EF Core | 10.0 |
| DB | SQLite (`app.db` in dev) — swappable to SQL Server | 10.0 |
| Validation | FluentValidation (+ `FluentValidation.AspNetCore` auto-validation) | 11.x |
| Mapping | Manual (LINQ projections) | — |
| Logging | Serilog (console sink for POC) | 10.x (`Serilog.AspNetCore`) |
| JSON | camelCase; **enums as strings** | — |
| Docs | Swagger with Bearer "Authorize" button | Swashbuckle 10.x |
| Tests | xUnit + `WebApplicationFactory` + FluentAssertions | latest |
| Frontend | React 18 + Vite + TypeScript (alt: Blazor WASM) | latest |

**Do not introduce:** MediatR, CQRS, DDD aggregates, mixed minimal-API/controllers, GraphQL, gRPC, Docker/K8s, Redis, message queues. Keep it flat and boring.

**Solution layout** (`TaskTracker.slnx` — .NET 10's default solution format)

```
src/
  TaskTracker.Domain/          Entities, enums, global role names
  TaskTracker.Application/     DTOs/contracts, validators, service interfaces, ApiResponse, ServiceResult, Optional<T>
  TaskTracker.Infrastructure/  AppDbContext, migrations, Identity, JWT/refresh tokens, service implementations, seeder
  TaskTracker.Api/             Program.cs, controllers, Auth/Policies.cs (requirements + handlers), Swagger setup
tests/
  TaskTracker.Api.Tests/       Integration tests via WebApplicationFactory (in-memory SQLite per test class)
```

---

## 3. Personas & roles

**Global roles** (ASP.NET Identity roles):

| Role | Who | Can |
|---|---|---|
| `Admin` | System administrator | Manage users (list, change global role, activate/deactivate). **Acts as Owner on every project** (support/moderation). |
| `User` | Everyone else (default on registration) | Create projects; everything else depends on their per-project role. |

**Per-project roles** (`ProjectMember.Role`), ranked `Owner (3) > Member (2) > Viewer (1)` — a higher role implies all lower permissions:

| Role | Meaning |
|---|---|
| `Owner` | Exactly one per project. Manages the project, its members, and deletes tasks; moderates comments/time logs. |
| `Member` | Contributor: creates/edits tasks, comments, logs time. Cannot delete tasks. |
| `Viewer` | Read-only. Cannot be assigned tasks. |

**Core entities:** `ApplicationUser`, `Project`, `ProjectMember`, `TaskItem`, `Comment`, `TimeLog`, `RefreshToken`.

---

## 4. Domain model (C#)

```csharp
// TaskTracker.Domain/Enums
public enum TaskStatus   { Todo = 0, InProgress = 1, InReview = 2, Done = 3, Cancelled = 4 }
public enum TaskPriority { Low = 0, Medium = 1, High = 2, Critical = 3 }
public enum ProjectRole  { Viewer = 1, Member = 2, Owner = 3 }   // rank matters

// TaskTracker.Domain/Roles.cs — global roles
public static class Roles { public const string Admin = "Admin"; public const string User = "User"; }

public class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; }          // required, ≤ 100
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

public class Project
{
    public Guid Id; public string Name;              // required, ≤ 100
    public string? Description;                      // ≤ 1000
    public Guid CreatedByUserId;
    public DateTime CreatedAt, UpdatedAt;
    public bool IsDeleted;                           // soft delete
    public ICollection<ProjectMember> Members; public ICollection<TaskItem> Tasks;
}

public class ProjectMember                           // drives ALL resource-based authorization
{
    public Guid ProjectId; public Guid UserId;       // composite PK = unique (ProjectId, UserId)
    public ProjectRole Role; public DateTime JoinedAt;
}

public class TaskItem
{
    public Guid Id; public Guid ProjectId;
    public string Title;                             // required, ≤ 200
    public string? Description;                      // ≤ 4000
    public TaskStatus Status { get; private set; }   // default Todo; change via SetStatus(...)
    public TaskPriority Priority;                    // default Medium
    public Guid? AssigneeId;                         // must be project Owner or Member (not Viewer)
    public DateOnly? DueDate;
    public Guid CreatedById; public Guid? UpdatedById;
    public DateTime CreatedAt, UpdatedAt;
    public DateTime? CompletedAt { get; private set; } // set entering Done, cleared leaving Done
    public bool IsDeleted;                           // soft delete
}

public class Comment  { Guid Id; Guid TaskId; Guid AuthorId; string Body /* 1–2000 */; DateTime CreatedAt, UpdatedAt; }
public class TimeLog  { Guid Id; Guid TaskId; Guid UserId /* always the caller */; int Minutes /* 1–1440 */;
                        DateOnly WorkDate /* default today UTC, not future */; string? Note /* ≤ 500 */; DateTime CreatedAt; }
public class RefreshToken { Guid Id; Guid UserId; string TokenHash /* SHA-256 */; DateTime CreatedAt, ExpiresAt;
                            DateTime? RevokedAt; Guid? ReplacedByTokenId; }
```

**Global query filters (soft delete):** `Project.IsDeleted == false`; `TaskItem` hidden if it or its project is deleted;
`ProjectMember`, `Comment`, `TimeLog` hidden if their project/task is deleted. Comments, time logs and memberships are hard-deleted.
**Invariants:** every project has exactly one Owner; Owner is only granted via *transfer ownership*;
demoting a member to Viewer or removing them **unassigns their tasks** in that project.

---

## 5. Authentication architecture

- **ASP.NET Core Identity** for the user store (PBKDF2 hashing, lockout). Emails stored lowercased.
- **Access token:** JWT, **15 min**. Claims: `sub` (user id), `email`, `name` (display name), `role` (global role), `security_stamp`, `jti`.
  - `security_stamp` is a **SHA-256 hash** of the user's Identity stamp — never the raw value (Section 14).
- **Refresh token:** random 64 bytes (base64url), **7 days**, stored as a SHA-256 hash, **single-use with rotation**.
  - Presenting an already-rotated token = suspected theft → **all** of that user's refresh tokens are revoked, 401.
- **Per-request session check** (`JwtBearerEvents.OnTokenValidated` → `IAuthService.IsSessionValidAsync`): user exists, `IsActive`, and stamp hash matches — otherwise 401. This is what makes logout, deactivation and role changes take effect immediately (`AddSecurityStampValidator()` only applies to cookie auth).
- **Registration** assigns global role `User`. **Password policy:** ≥ 8 characters only (Identity's digit/upper/lower/symbol rules are disabled to match US-A1). **Lockout:** 5 failures → 15 min.
- **Logout** rotates the security stamp (kills access tokens) **and** revokes all refresh tokens → every session ends.
- **Role change** (admin) rotates the stamp → the old access token gets 401, but the refresh token still works and the next access token carries the new role.
- **Deactivate** rotates the stamp and revokes refresh tokens; re-activation does not revive old sessions.
- `Jwt:Key` comes from `appsettings.Development.json` for the POC (real deployments: user-secrets / Key Vault).

---

## 6. Authorization & permission matrix

**Rules of thumb:** not a member → **404** (existence is not revealed); member with too low a role → **403**.
Admins are treated as `Owner` on every project.

### 6.1 Policies (`TaskTracker.Api/Auth/Policies.cs`)

| Policy | Requirement | Used for |
|---|---|---|
| `AdminOnly` | global role `Admin` | `/api/users/*` |
| `ProjectMember` | project role ≥ Viewer | all project/task/comment/time-log reads |
| `ProjectContributor` | project role ≥ Member | task create/edit, comment create, time-log create |
| `ProjectOwner` | project role = Owner | project update/delete, member management, transfer, **task delete** |
| `CommentAuthorOrProjectOwner` | author **or** Owner | comment edit/delete |
| `TimeLogAuthorOrProjectOwner` | author **or** Owner | time-log edit/delete |

Project policies are **resource-based**: the controller loads `ProjectAccess` (the caller's effective role; `null` → 404) and calls
`IAuthorizationService.AuthorizeAsync(User, access, policy)` (→ 403). For comments/time logs it then loads the item, verifies it belongs
to `{projectId}/{taskId}` (else 404), and authorizes an `AuthoredResource(AuthorId, access)`.

### 6.2 Matrix

| Operation | Owner | Member | Viewer | Non-member |
|---|:--:|:--:|:--:|:--:|
| List my projects / create a project | ✅ | ✅ | ✅ | ✅ (any authenticated user; creator becomes Owner) |
| View project, list members | ✅ | ✅ | ✅ | 404 |
| Update / delete project | ✅ | 403 | 403 | 404 |
| Add / change role / remove member, transfer ownership | ✅ | 403 | 403 | 404 |
| List / view / search tasks | ✅ | ✅ | ✅ | 404 |
| Create / edit / assign task | ✅ | ✅ | 403 | 404 |
| Delete task | ✅ | 403 | 403 | 404 |
| List comments / time logs | ✅ | ✅ | ✅ | 404 |
| Add comment / log time | ✅ | ✅ | 403 | 404 |
| Edit / delete comment or time log | ✅ any | own only | own only | 404 |
| `/api/me/tasks` | any authenticated user — only tasks assigned to them in projects they belong to |||
| `/api/users/*` | global `Admin` only (everyone else 403) |||

**Member-management invariants → 409 Conflict:** already a member; granting `Owner` directly (use transfer);
removing or demoting the Owner. Transfer to a non-member → 400. Unknown email on add → 404.

---

## 7. API surface

All endpoints under `/api`; all require `Authorization: Bearer <accessToken>` except register/login/refresh.

**Response envelope (all responses):**
```json
{ "success": true, "data": { ... }, "errors": [] }
```
Errors → `{ "success": false, "data": null, "errors": ["..."] }` with `400 / 401 / 403 / 404 / 409 / 422 / 500`.
List endpoints put a page in `data`: `{ items, page, pageSize, totalCount }` (default 20, max 100; out of range → 400).

### Auth — `/api/auth`
| Method | Route | Auth | Notes |
|---|---|---|---|
| POST | `/register` | anon | `{ email, password, displayName }` → 201 `UserDto` |
| POST | `/login` | anon | `{ email, password }` → `AuthResponse` |
| POST | `/refresh` | anon | `{ refreshToken }` → new `AuthResponse`; old refresh token revoked |
| POST | `/logout` | user | ends every session → 204 |
| GET | `/me` | user | `UserDto` |

### Users (global Admin only) — `/api/users`
| Method | Route | Notes |
|---|---|---|
| GET | `/?page=&pageSize=` | `PagedResult<UserDto>` |
| PUT | `/{id}/role` | `{ role: "Admin" \| "User" }` — blocks demoting the last active Admin (400) |
| PUT | `/{id}/deactivate` | next request → 401; refresh tokens revoked; last active Admin protected (400) |
| PUT | `/{id}/activate` | |

### Projects — `/api/projects`
| Method | Route | Policy | Notes |
|---|---|---|---|
| GET | `/` | authenticated | `search` (name/description), `role`, `sortBy=name\|createdAt\|updatedAt`, `sortDir`, paging. Scoped to my memberships (Admin: all) |
| POST | `/` | authenticated | `{ name, description? }` → 201 + `Location`; caller becomes Owner |
| GET | `/{projectId}` | ProjectMember | |
| PATCH | `/{projectId}` | ProjectOwner | `{ name?, description? }` — partial; `"description": null` clears |
| DELETE | `/{projectId}` | ProjectOwner | soft delete → 204 |
| GET | `/{projectId}/members` | ProjectMember | |
| POST | `/{projectId}/members` | ProjectOwner | `{ email, role: Member\|Viewer }` → 201 |
| PATCH | `/{projectId}/members/{userId}` | ProjectOwner | `{ role: Member\|Viewer }` |
| DELETE | `/{projectId}/members/{userId}` | ProjectOwner | → 204 |
| POST | `/{projectId}/transfer-ownership` | ProjectOwner | `{ newOwnerUserId }` → members; old Owner becomes Member |

### Tasks — `/api/projects/{projectId}/tasks`
| Method | Route | Policy | Notes |
|---|---|---|---|
| GET | `/` | ProjectMember | see Section 8 (search/filter/sort/page) |
| POST | `/` | ProjectContributor | `{ title, description?, priority?, assigneeId?, dueDate? }` → 201 + `Location`; status starts `Todo` |
| GET | `/{taskId}` | ProjectMember | task from another project → 404 |
| PATCH | `/{taskId}` | ProjectContributor | partial: `{ title?, description?, status?, priority?, assigneeId?, dueDate? }`; `null` clears assignee/description/dueDate |
| DELETE | `/{taskId}` | ProjectOwner | soft delete → 204; second call → 404 |

**Assignee rules:** not a project member → **400**; a Viewer → **422**.

### My tasks — `GET /api/me/tasks`
Tasks assigned to me across all projects I belong to; same filters as Section 8 minus `assigneeId`. Items include `projectName`.

### Comments — `/api/projects/{projectId}/tasks/{taskId}/comments`
GET (newest first, paged) · POST `{ body }` → 201 (Contributor) · PATCH `/{commentId}` `{ body }` · DELETE `/{commentId}` → 204 (author or Owner).

### Time logs — `/api/projects/{projectId}/tasks/{taskId}/timelogs`
GET (newest first, paged) · POST `{ minutes, workDate?, note? }` → 201 (Contributor; user = caller, a `userId` in the body is ignored) ·
PATCH `/{logId}` `{ minutes?, workDate?, note? }` · DELETE `/{logId}` → 204 (author or Owner).

---

## 8. Search, filtering, sorting & pagination (tasks)

`GET /api/projects/{projectId}/tasks` and `GET /api/me/tasks`:

| Param | Behaviour |
|---|---|
| `search` | case-insensitive contains on **Title OR Description**. Parameterised `LIKE` with `%`, `_`, `\` escaped — never string-concatenated SQL |
| `status` | repeatable or comma-separated: `?status=Todo&status=InProgress` or `?status=Todo,InProgress` |
| `priority` | same as `status` |
| `assigneeId` | user id, or `none` for unassigned (project list only) |
| `dueBefore` / `dueAfter` | inclusive `DateOnly` range |
| `sortBy` | `title \| status \| priority \| dueDate \| createdAt \| updatedAt` (default `createdAt`) |
| `sortDir` | `asc \| desc` (default `desc`) |
| `page`, `pageSize` | default 1 / 20, max 100 |

All filters combine with **AND** and with the membership scope. Invalid enum/sortBy/sortDir/assigneeId/date range → **400** (never 500).
No matches → `{ items: [], totalCount: 0 }`.

---

## 9. DTOs (authoritative shapes)

```csharp
// Auth
public record RegisterRequest(string Email, string Password, string DisplayName);
public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
public record AuthResponse(string AccessToken, DateTime ExpiresAtUtc, string RefreshToken, DateTime RefreshTokenExpiresAtUtc, UserDto User);
public record UserDto(Guid Id, string Email, string DisplayName, string[] Roles, bool IsActive);
public record ChangeRoleRequest(string Role);                       // "Admin" | "User"

// Projects
public record ProjectDto(Guid Id, string Name, string? Description, ProjectRole MyRole, Guid CreatedByUserId, DateTime CreatedAt, DateTime UpdatedAt);
public record MemberDto(Guid UserId, string Email, string DisplayName, ProjectRole Role, DateTime JoinedAt);
public record CreateProjectRequest(string Name, string? Description);
public record UpdateProjectRequest(Optional<string> Name, Optional<string?> Description);
public record AddMemberRequest(string Email, ProjectRole Role);
public record ChangeMemberRoleRequest(ProjectRole Role);
public record TransferOwnershipRequest(Guid NewOwnerUserId);

// Tasks
public record TaskDto(Guid Id, Guid ProjectId, string ProjectName, string Title, string? Description,
    TaskStatus Status, TaskPriority Priority, Guid? AssigneeId, string? AssigneeName,
    DateOnly? DueDate, Guid CreatedById, DateTime CreatedAt, DateTime UpdatedAt, DateTime? CompletedAt);
public record CreateTaskRequest(string Title, string? Description, TaskPriority? Priority, Guid? AssigneeId, DateOnly? DueDate);
public record UpdateTaskRequest(Optional<string> Title, Optional<string?> Description, Optional<TaskStatus> Status,
    Optional<TaskPriority> Priority, Optional<Guid?> AssigneeId, Optional<DateOnly?> DueDate);

// Comments / time logs
public record CommentDto(Guid Id, Guid TaskId, Guid AuthorId, string AuthorName, string Body, DateTime CreatedAt, DateTime UpdatedAt);
public record CommentRequest(string Body);
public record TimeLogDto(Guid Id, Guid TaskId, Guid UserId, string UserName, int Minutes, DateOnly WorkDate, string? Note, DateTime CreatedAt);
public record CreateTimeLogRequest(int Minutes, DateOnly? WorkDate, string? Note);
public record UpdateTimeLogRequest(Optional<int> Minutes, Optional<DateOnly> WorkDate, Optional<string?> Note);

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
```

`Optional<T>` (PATCH bodies): a field **absent** from the JSON is left unchanged; a field **present** — including `null` — is applied.
Swagger documents these as their plain types.

---

## 10. Seed data (Development only, idempotent)

Runs on startup when `app.Environment.IsDevelopment()` and `admin@example.com` does not exist yet. Password for all: `Passw0rd!`.

| Email | Global role | In "Website Redesign" |
|---|---|---|
| `admin@example.com` | Admin | — (acts as Owner everywhere) |
| `alice@example.com` | User | **Owner** |
| `bob@example.com` | User | **Member** |
| `carol@example.com` | User | **Viewer** |
| `dave@example.com` | User | not a member (for 404 checks) |

Project **"Website Redesign"** with 5 tasks — one per status (Todo, InProgress, InReview, Done with `completedAt`, Cancelled),
mixed priorities, assigned to bob/alice/unassigned — plus 2 comments and 2 time logs.
`search=login&status=Todo` returns exactly one task ("Fix login redirect bug").

---

## 11. Implementation milestones

| # | Milestone | Status |
|---|---|---|
| 1 | Skeleton: solution, EF Core + SQLite, Identity, JWT wiring, Swagger, `/health` | ✅ Done |
| 2 | Auth: register / login / logout / me, per-request session check (US-A1..A4) | ✅ Done |
| 3 | Global roles + Admin user management (US-B2), seed data | ✅ Done |
| 4 | **Merge with TASKS.md:** per-project roles + resource-based policies, refresh tokens, projects + membership, tasks with search/filter/sort, comments, time logs, `/api/me/tasks` | ✅ Done |
| 5 | React frontend (`web/`): login/register (with silent refresh), project list, task list + create/edit/delete, search & filters, members screen, comments & time logs, my tasks, admin users, role-aware UI | ✅ Done |
| 6 | Hardening: coverage report (target ≥ 70% on Application + Api), README with curl examples | ⏳ |

Deliverable check for milestone 4: all TASKS.md §10 acceptance scenarios pass — automated in `tests/.../Acceptance/TasksMdAcceptanceTests.cs`.

---

## 12. Demo script (acceptance walkthrough)

Mirrors TASKS.md §10; every step is also an automated test (`S01`–`S20`).

1. Anonymous `GET /api/projects` → **401**.
2. Alice creates a project → 201, she is Owner. She adds Bob as Member and Carol as Viewer → 201 each.
3. Dave `GET /api/projects/{id}` → **404**.
4. Carol `POST …/tasks` → **403**; Bob `POST …/tasks` → 201.
5. Bob `PATCH …/tasks/{id}` `{ "status": "Done" }` → 200 with `completedAt` set.
6. Bob `DELETE …/tasks/{id}` → **403**; Alice → 204.
7. Carol `POST …/comments` → 403. Bob comments and edits his own → 201 → 200. Bob edits Alice's → 403. Alice edits Bob's → 200.
8. Bob edits his own time log → 200; Alice edits Bob's → 200.
9. `?search=login&status=Todo` on "Website Redesign" → only "Fix login redirect bug".
10. Alice removes herself (sole Owner) → **409**. Alice transfers ownership to Bob → Alice is now Member and `DELETE` task → 403.
11. Bob `GET /api/me/tasks` → only his assignments, each with `projectName`.
12. Expired access token → `POST /api/auth/refresh` → new tokens work.
13. Admin promotes a user to `Admin` → their old access token gets 401 → refresh → `/api/users` works. Admin deactivates a user → their next request and refresh both 401.

If any step fails, the corresponding story is not Done.

---

## 13. User stories (summary)

**Epic A — Authentication:** US-A1 register (role `User`, unique email → 400 "Email already registered", field-level validation, password hashed & never returned) ·
US-A2 login (generic "Invalid email or password", "Account disabled", 5-failure lockout) · US-A3 logout (all sessions end) ·
US-A4 protected routes (no/invalid/expired token → 401; secrets never in responses or logs) · **US-A6 refresh** (rotation, reuse detection, 7-day expiry) ·
US-A5 change password — *optional, not built*.

**Epic B — Authorization:** US-B1 resource-based project roles (Section 6) · US-B2 Admin user management (paginated list, change global role, activate/deactivate, last-active-Admin guard, audit-logged) ·
US-B3 role-aware UI (frontend milestone; hiding is cosmetic, API is the source of truth) · US-B4 data scoping (non-members see nothing; lists are scoped in the query).

**Epic C — CRUD:** US-C1 add task · US-C2 view/list · US-C3 edit (partial, `completedAt`) · US-C4 delete (Owner only, soft) · US-C5 search · US-C6 filter & sort ·
US-C7 projects + membership · **US-C8 comments** · **US-C9 time logs** · **US-C10 my tasks across projects**.

---

## 14. Definition of Done (per story)

- [ ] Endpoint(s) implemented and reachable via Swagger.
- [ ] FluentValidation validator exists and is wired (`AddFluentValidationAutoValidation`).
- [ ] At least one happy-path **and** one failure-path integration test using `WebApplicationFactory<Program>`.
- [ ] Role enforcement covered by a test that calls the endpoint with a non-permitted role and asserts `403` (and a non-member asserting `404`).
- [ ] No passwords, JWTs, refresh-token hashes or `SecurityStamp` values appear in response bodies or log output.
- [ ] New/changed code compiles with `dotnet build -warnaserror`.
- [ ] Corresponding section of this doc updated if anything drifted.

---

## 15. Testing strategy

- **Framework:** xUnit + `Microsoft.AspNetCore.Mvc.Testing` + FluentAssertions.
- **DB per test class:** `TaskTrackerWebAppFactory` points the app at a private in-memory SQLite DB (shared-cache, kept open by the fixture), runs migrations + seed. The dev `app.db` is never touched.
- **Helpers:** `factory.LoginAsync(email)`, `AuthenticateAsAdmin/Owner/Member/Viewer/NonMember()`, and `factory.CreateProjectWithCastAsync()` — a fresh project with alice/bob/carol/dave so tests never share mutable data.
- **Layout:** `Auth/` (US-A*, refresh), `Users/` (US-B2), `Projects/`, `Tasks/`, `Activity/` (comments, time logs), `Seeding/`, `Acceptance/` (TASKS.md §10, S01–S20).

---

## 16. Out of scope (do not build)

Password reset by email, SSO/OAuth, multi-tenancy, file attachments, activity feed, notifications, real-time, hard delete/recycle bin for projects/tasks,
reporting, mobile app, i18n, Docker/K8s manifests, CI/CD. *(Refresh tokens, comments and time logs moved **into** scope with the TASKS.md merge.)*

---

## 17. Claude-specific guidance

- **One milestone per prompt.**
- **When adding a new package**, always include the exact `.csproj` `<PackageReference>` line.
- **When you change the domain model or endpoints**, update Section 4 or Section 7 of this document in the same response.
- **Prefer explicit code over clever abstractions.** No patterns beyond what's listed in Section 2.
- **If a story can't be met**, say so and propose the smallest change to scope — do not silently expand.
- **Format responses as:** (1) file tree delta, (2) full file contents, (3) how to run/verify.

---

## 18. Run it

```
dotnet run --project src/TaskTracker.Api --launch-profile http   # API: http://localhost:5050/swagger
cd web && npm install && npm run dev                            # SPA: http://localhost:5173
dotnet test                                                     # API integration tests
cd web && npm test                                              # SPA unit tests
```
Sign in to the SPA (or Swagger → **Authorize**) with `alice@example.com` / `Passw0rd!`. The Vite dev server proxies
`/api` to `:5050` (override with `VITE_API_TARGET` in `web/.env.local`), so the API needs no CORS setup.

**Frontend (`web/`):** React 18 + Vite + TypeScript + React Router 6 + Axios, plain CSS with light/dark tokens.
Tokens live in `localStorage` (POC trade-off); a 401 triggers one shared refresh-and-retry, and a failed refresh
returns to the login page. Role-aware UI (`src/lib/permissions.ts`) is cosmetic; the API enforces every rule.

---

## 19. Deviations from TASKS.md (deliberate)

| TASKS.md | This build | Why |
|---|---|---|
| No global roles | Global `Admin` / `User` + Admin user management; Admin acts as Owner everywhere | Keeps SETUP_PRO's US-B2 |
| RFC 7807 `ProblemDetails` | `{ success, data, errors }` envelope (same status codes incl. 409/422) | Chosen convention |
| DataAnnotations | FluentValidation | Chosen convention |
| Single `ProjectTracker.Api` project | 4-project layered solution | Existing structure kept |
| Hard delete with cascade | Soft delete for projects/tasks (children hidden via query filters); comments/time logs hard-deleted | SETUP_PRO soft-delete rule; API behaviour is identical (gone → 404) |
| `login` → `{ accessToken, refreshToken, expiresIn }` | `{ accessToken, expiresAtUtc, refreshToken, refreshTokenExpiresAtUtc, user }` | Superset; keeps user payload |
| `logout` body `{ refreshToken }` revokes it | No body; revokes **all** refresh tokens + rotates stamp | Logout must also kill access tokens (US-A3) |
| Refresh token stored plain (POC) | Stored as SHA-256 hash, reuse detection | Safer at no extra cost |
| Seed `alice/bob/carol/dave@example.com` | Same, plus `admin@example.com` | Admin needed for US-B2 |
| Add member with unknown email: unspecified | 404 "User not found" | — |
| Transfer to non-member: unspecified | 400 | — |
