# Project & Task Tracker — POC Build Specification

> **Status (2026-11-02):** merged into `SETUP_PRO.md`, which is now the single source of truth.
> Everything below is implemented, with the deliberate deviations listed in SETUP_PRO.md §19
> (global Admin role kept, `{success,data,errors}` envelope instead of ProblemDetails,
> FluentValidation, 4-project layout, soft delete). All §10 acceptance scenarios are automated
> in `tests/TaskTracker.Api.Tests/Acceptance/TasksMdAcceptanceTests.cs`.

> **Instructions for the AI assistant:** This file is the single source of truth.
> Build the application phase by phase (Section 10). Do not invent endpoints,
> fields, or roles not defined here. If something is ambiguous, ask before coding.
> After each phase, list the files created and how to run/test them.

---

## 1. Overview & Goals

A small ASP.NET Core Web API for managing **Projects**, **Tasks**, **Comments**,
and **Time Logs**, with:

- **Authentication**: JWT bearer (access + refresh token)
- **Authorization**: **Resource-based** — every action is checked against the
  user's role *within a specific project* (Owner / Member / Viewer)
- **Full CRUD** for all four resources
- **Search, filtering, sorting, and pagination** on list endpoints

This POC deliberately demonstrates three authorization layers:

1. **Authenticated** (any valid JWT)
2. **Resource-based role check** (membership in a project)
3. **Ownership-based check** (author of a comment/time log, or project Owner)

---

## 2. Tech Stack & Solution Structure

| Concern        | Choice                                                        |
|----------------|---------------------------------------------------------------|
| Runtime        | .NET 10 (LTS)                                                 |
| API style      | ASP.NET Core Web API with **Controllers**                     |
| ORM            | EF Core 10 + **SQLite** (file `app.db`, zero setup)           |
| Auth           | JWT Bearer (`Microsoft.AspNetCore.Authentication.JwtBearer`)  |
| Password hash  | `Microsoft.AspNetCore.Identity.PasswordHasher<User>` (no global Identity roles) |
| Docs           | Swagger / OpenAPI with "Authorize" button enabled for JWT     |
| Validation     | DataAnnotations on request DTOs                               |
| Errors         | RFC 7807 `ProblemDetails`                                     |
| JSON           | camelCase; enums serialized as **strings**                    |

### Folder structure

```
src/ProjectTracker.Api/
  Program.cs
  appsettings.json
  Data/AppDbContext.cs
  Data/DbSeeder.cs
  Domain/Entities/         (User, Project, ProjectMember, TaskItem, Comment, TimeLog)
  Domain/Enums/            (ProjectRole, TaskStatus, TaskPriority)
  Auth/                    (JwtTokenService, AuthPolicies, Handlers/, Requirements/)
  Contracts/               (request/response DTOs)
  Services/                (ProjectService, TaskService, CommentService, TimeLogService)
  Controllers/             (AuthController, ProjectsController, TasksController,
                            CommentsController, TimeLogsController, MeController)
  Migrations/
tests/ProjectTracker.Tests/   (optional but recommended, xUnit)
```

> **Naming note:** name the task entity `TaskItem`, **never** `Task`
> (collides with `System.Threading.Tasks.Task`).

---

## 3. Domain Model

### Enums

```csharp
enum ProjectRole  { Viewer = 1, Member = 2, Owner = 3 }   // rank matters
enum TaskStatus   { Todo, InProgress, InReview, Done, Cancelled }
enum TaskPriority { Low, Medium, High, Critical }
```

### Entities

**User**
| Field | Type | Rules |
|---|---|---|
| Id | Guid | PK |
| Email | string | required, unique, valid email, lowercased |
| DisplayName | string | required, ≤ 100 |
| PasswordHash | string | required |
| CreatedAt | DateTime (UTC) | |

**Project**
| Field | Type | Rules |
|---|---|---|
| Id | Guid | PK |
| Name | string | required, ≤ 100 |
| Description | string? | ≤ 1000 |
| CreatedByUserId | Guid | FK → User |
| CreatedAt / UpdatedAt | DateTime (UTC) | |

**ProjectMember** (drives ALL resource-based authorization)
| Field | Type | Rules |
|---|---|---|
| Id | Guid | PK |
| ProjectId | Guid | FK, cascade delete |
| UserId | Guid | FK |
| Role | ProjectRole | |
| JoinedAt | DateTime (UTC) | |
| — | — | **unique index (ProjectId, UserId)** |

**TaskItem**
| Field | Type | Rules |
|---|---|---|
| Id | Guid | PK |
| ProjectId | Guid | FK, cascade delete |
| Title | string | required, ≤ 200 |
| Description | string? | ≤ 4000 |
| Status | TaskStatus | default `Todo` |
| Priority | TaskPriority | default `Medium` |
| AssigneeId | Guid? | must be a **Member or Owner** of the project (not a Viewer) |
| CreatedByUserId | Guid | FK |
| DueDate | DateOnly? | |
| CreatedAt / UpdatedAt | DateTime (UTC) | |
| CompletedAt | DateTime? | auto-set when Status → Done, cleared when moved away |

**Comment**
| Field | Type | Rules |
|---|---|---|
| Id | Guid | PK |
| TaskId | Guid | FK, cascade delete |
| AuthorId | Guid | FK |
| Body | string | required, ≤ 2000 |
| CreatedAt / UpdatedAt | DateTime (UTC) | |

**TimeLog**
| Field | Type | Rules |
|---|---|---|
| Id | Guid | PK |
| TaskId | Guid | FK, cascade delete |
| UserId | Guid | FK — always the caller (you cannot log time for someone else) |
| Minutes | int | 1–1440 |
| WorkDate | DateOnly | defaults to today (UTC) |
| Note | string? | ≤ 500 |
| CreatedAt | DateTime (UTC) | |

**RefreshToken**
| Field | Type | Rules |
|---|---|---|
| Id | Guid | PK |
| UserId | Guid | FK |
| Token | string | random 64-byte base64, stored hashed or plain (POC: plain) |
| ExpiresAt | DateTime (UTC) | 7 days |
| RevokedAt | DateTime? | null = active |

---

## 4. Roles & Permission Matrix

**Hierarchy:** `Owner (3) > Member (2) > Viewer (1)`.
A higher role implies all permissions of lower roles.

**General rules**
- Every project MUST have at least **one Owner** at all times.
- A project is only visible to its members (non-members get **404**, not 403).
- Any authenticated user can **create** a project → becomes its **Owner**.
- Assignees on tasks must be `Owner` or `Member` of that project.

### 4.1 Projects

| Operation | Owner | Member | Viewer | Non-member / Anonymous |
|---|---|---|---|---|
| List my projects (with search) | ✅ | ✅ | ✅ | 401 |
| View project | ✅ | ✅ | ✅ | 404 |
| Create project | any authenticated user (becomes Owner) ||| 401 |
| Update name/description | ✅ | ❌ 403 | ❌ 403 | 404 |
| Delete project | ✅ | ❌ 403 | ❌ 403 | 404 |
| List members | ✅ | ✅ | ✅ | 404 |
| Add member (by email, role Member/Viewer) | ✅ | ❌ 403 | ❌ 403 | 404 |
| Change member role | ✅ | ❌ 403 | ❌ 403 | 404 |
| Remove member | ✅ | ❌ 403 | ❌ 403 | 404 |
| Transfer ownership | ✅ | ❌ 403 | ❌ 403 | 404 |

**Member-management invariants** (return `409 Conflict` when violated):
- Cannot remove or demote the **last Owner**.
- Cannot add a user who is already a member.
- Cannot set a member's role to `Owner` directly — use *transfer ownership*
  (old Owner becomes `Member`).

### 4.2 Tasks

| Operation | Owner | Member | Viewer | Non-member / Anonymous |
|---|---|---|---|---|
| List / search / filter tasks | ✅ | ✅ | ✅ | 404 |
| View task | ✅ | ✅ | ✅ | 404 |
| Create task | ✅ | ✅ | ❌ 403 | 404 |
| Edit task (title, description, status, priority, due date, assignee) | ✅ | ✅ | ❌ 403 | 404 |
| Assign / reassign task | ✅ | ✅ | ❌ 403 | 404 |
| Delete task | ✅ | ❌ 403 | ❌ 403 | 404 |
| View my tasks across projects (`/api/me/tasks`) | any authenticated user (only sees tasks in projects they belong to) ||| 401 |

> **Why this matters:** "Member can edit but not delete" is the clearest way to
> prove that `ProjectContributor` and `ProjectOwner` are *distinct policies*.

### 4.3 Comments

| Operation | Owner | Member | Viewer | Non-author w/ role | Non-member |
|---|---|---|---|---|---|
| List comments | ✅ | ✅ | ✅ | ✅ | 404 |
| Add comment | ✅ | ✅ | ❌ 403 | — | 404 |
| Edit comment | ✅ (any) | only if author | only if author | ❌ 403 | 404 |
| Delete comment | ✅ (any) | only if author | only if author | ❌ 403 | 404 |

> Owner may edit/delete **any** comment (moderation). Members/Viewers only their own.
> Viewers may **not** create comments (read-only role).

### 4.4 Time Logs

| Operation | Owner | Member | Viewer | Non-author w/ role | Non-member |
|---|---|---|---|---|---|
| List time logs | ✅ | ✅ | ✅ | ✅ | 404 |
| Log time | ✅ | ✅ | ❌ 403 | — | 404 |
| Edit time log | ✅ (any) | only if author | only if author | ❌ 403 | 404 |
| Delete time log | ✅ (any) | only if author | only if author | ❌ 403 | 404 |

Additional rule: `UserId` is always taken from the JWT — clients cannot log
time on behalf of another user.

---

## 5. API Surface — All CRUD + Search Operations

Base route: `/api`. All endpoints require `Authorization: Bearer <accessToken>`
unless marked **public**. All IDs are GUIDs.

### 5.1 Auth — `/api/auth`

| Method | Route | Purpose | Access |
|---|---|---|---|
| POST | `/register` | Create account `{email, displayName, password}` → 201 | public |
| POST | `/login` | `{email, password}` → `{accessToken, refreshToken, expiresIn}` | public |
| POST | `/refresh` | `{refreshToken}` → new token pair (rotate refresh token) | public |
| POST | `/logout` | `{refreshToken}` → revokes it → 204 | authenticated |
| GET | `/me` | Current user `{id, email, displayName}` | authenticated |

JWT claims: `sub` (userId), `email`, `name`. Access token lifetime: **15 min**.

### 5.2 Projects — `/api/projects`

| Method | Route | Purpose | Required policy |
|---|---|---|---|
| GET | `/` | List **my** projects (search/filter/page — see §6.1) | Authenticated |
| POST | `/` | Create project → 201 + `Location` header | Authenticated |
| GET | `/{projectId}` | Get one project | `ProjectMember` |
| PATCH | `/{projectId}` | Partial update `{name?, description?}` | `ProjectOwner` |
| DELETE | `/{projectId}` | Delete project + cascade | `ProjectOwner` |
| GET | `/{projectId}/members` | List members with roles | `ProjectMember` |
| POST | `/{projectId}/members` | Add member `{email, role: Member|Viewer}` → 201 | `ProjectOwner` |
| PATCH | `/{projectId}/members/{userId}` | Change role `{role: Member|Viewer}` | `ProjectOwner` |
| DELETE | `/{projectId}/members/{userId}` | Remove member → 204 | `ProjectOwner` |
| POST | `/{projectId}/transfer-ownership` | `{newOwnerUserId}` | `ProjectOwner` |

### 5.3 Tasks — `/api/projects/{projectId}/tasks`

| Method | Route | Purpose | Required policy |
|---|---|---|---|
| GET | `/` | List tasks in project (search/filter/sort/page — §6.2) | `ProjectMember` |
| POST | `/` | Create task → 201 + `Location` | `ProjectContributor` |
| GET | `/{taskId}` | Get one task | `ProjectMember` |
| PATCH | `/{taskId}` | Partial update (see §7.2) | `ProjectContributor` |
| DELETE | `/{taskId}` | Delete task → 204 | `ProjectOwner` |

### 5.4 My Tasks — `/api/me/tasks`

| Method | Route | Purpose | Required policy |
|---|---|---|---|
| GET | `/` | Tasks assigned to me across all my projects (filter/page — §6.3) | Authenticated |

### 5.5 Comments — `/api/projects/{projectId}/tasks/{taskId}/comments`

| Method | Route | Purpose | Required policy |
|---|---|---|---|
| GET | `/` | List comments (newest first) | `ProjectMember` |
| POST | `/` | Add comment `{body}` → 201 | `ProjectContributor` |
| PATCH | `/{commentId}` | Edit `{body}` | `CommentAuthorOrProjectOwner` |
| DELETE | `/{commentId}` | Delete → 204 | `CommentAuthorOrProjectOwner` |

### 5.6 Time Logs — `/api/projects/{projectId}/tasks/{taskId}/timelogs`

| Method | Route | Purpose | Required policy |
|---|---|---|---|
| GET | `/` | List time logs (newest first) | `ProjectMember` |
| POST | `/` | Log time `{minutes, workDate?, note?}` → 201 | `ProjectContributor` |
| PATCH | `/{logId}` | Edit `{minutes?, workDate?, note?}` | `TimeLogAuthorOrProjectOwner` |
| DELETE | `/{logId}` | Delete → 204 | `TimeLogAuthorOrProjectOwner` |

> In every endpoint with `{projectId}`, resolve the project **for the current user**
> first. If no membership row exists → return **404** (do not reveal the project exists).

---

## 6. Search, Filtering, Sorting & Pagination

All list endpoints return the envelope:

```json
{ "items": [ ... ], "page": 1, "pageSize": 20, "totalCount": 42 }
```

Defaults: `page=1`, `pageSize=20`, max `pageSize=100`. `page < 1` or
`pageSize > 100` → `400`.

### 6.1 `GET /api/projects`

| Param | Type | Behaviour |
|---|---|---|
| `search` | string | case-insensitive contains on **Name OR Description** |
| `role` | `Owner\|Member\|Viewer` | only projects where I have this role |
| `sortBy` | `name\|createdAt\|updatedAt` | default `createdAt` |
| `sortDir` | `asc\|desc` | default `desc` |
| `page`, `pageSize` | int | see above |

Always implicitly filtered to `ProjectMember.UserId == currentUserId`.

### 6.2 `GET /api/projects/{projectId}/tasks`

| Param | Type | Behaviour |
|---|---|---|
| `search` | string | case-insensitive contains on **Title OR Description** |
| `status` | TaskStatus (repeatable) | `?status=Todo&status=InProgress` |
| `priority` | TaskPriority (repeatable) | |
| `assigneeId` | Guid? | use `assigneeId=none` for unassigned |
| `dueBefore` / `dueAfter` | DateOnly | inclusive range on `DueDate` |
| `sortBy` | `title\|status\|priority\|dueDate\|createdAt\|updatedAt` | default `createdAt` |
| `sortDir` | `asc\|desc` | default `desc` |
| `page`, `pageSize` | int | |

### 6.3 `GET /api/me/tasks`

Same filters as §6.2 minus `assigneeId` (implicitly `= currentUserId`), but
scoped to **all projects where I have any membership**. Each item includes
`projectId` and `projectName`.

---

## 7. Task Lifecycle Walkthrough (add / edit / delete / search)

### 7.1 How to ADD a task
1. `POST /api/projects/{projectId}/tasks`
2. **Authorize:** caller must be `Owner` or `Member` of the project (`ProjectContributor`).
3. **Request body:**
   ```json
   {
     "title": "Write acceptance tests",       // required, ≤200
     "description": "Cover authZ edge cases", // optional, ≤4000
     "priority": "High",                      // optional, default Medium
     "assigneeId": "…guid…",                  // optional; must be Owner/Member of project
     "dueDate": "2026-11-15"                  // optional
   }
   ```
4. **Validation:** title required; `assigneeId` must exist, be a project member,
   and NOT be a Viewer → otherwise `400`/`422` (`422` for "assignee must be a contributor").
5. **Result:** `TaskItem` created with `Status=Todo`, `CreatedByUserId=caller`,
   `CreatedAt=UtcNow`. Returns `201` + `Location: /api/projects/{projectId}/tasks/{taskId}`.

### 7.2 How to EDIT a task
1. `PATCH /api/projects/{projectId}/tasks/{taskId}` — all fields optional (partial update;
   only provided fields are applied). Send `"assigneeId": null` to unassign.
2. **Authorize:** caller must be `Owner` or `Member` (`ProjectContributor`).
   Viewers → `403`.
3. **Request body (all optional):**
   ```json
   {
     "title": "…", "description": "…",
     "status": "InProgress",
     "priority": "Critical",
     "assigneeId": null,
     "dueDate": "2026-12-01"
   }
   ```
4. **Rules:**
   - `title` if present must be non-empty, ≤200.
   - New `assigneeId` must be a project Owner/Member (not Viewer).
   - Status transitions are **free-form** in this POC (any → any), **but**:
     entering `Done` sets `CompletedAt = UtcNow`; leaving `Done` clears it.
   - Always update `UpdatedAt = UtcNow`.
5. **Result:** `200` with updated task DTO.

### 7.3 How to DELETE a task
1. `DELETE /api/projects/{projectId}/tasks/{taskId}`
2. **Authorize:** caller must be **`Owner`** (`ProjectOwner`). Members and Viewers → `403`.
3. Task, its comments, and time logs are cascade-deleted.
4. **Result:** `204 No Content`. (Idempotent: second call → `404`.)

### 7.4 How to SEARCH tasks
1. `GET /api/projects/{projectId}/tasks?search=login&status=Todo&priority=High,Medium&assigneeId=…&dueBefore=2026-12-31&sortBy=dueDate&sortDir=asc&page=1&pageSize=20`
2. **Authorize:** any project member (including Viewer) — read access.
3. Implementation: build an `IQueryable<TaskItem>` filtered by `ProjectId`,
   then apply only the provided filters (compose `Where` clauses; multi-value
   params via `Contains`). Use `EF.Functions.Like` or
   `t.Title.ToLower().Contains(term)` for case-insensitive search.
4. Cross-project search for the current user: `GET /api/me/tasks?search=login`.

---

## 8. Authorization Design (the showcase)

### 8.1 Policies (registered in `Program.cs`)

| Policy name | Applies to | Requirement |
|---|---|---|
| `ProjectMember` | Project, Task, Comment, TimeLog reads | membership role rank ≥ `Viewer` |
| `ProjectContributor` | Task create/edit, Comment create, TimeLog create | role rank ≥ `Member` |
| `ProjectOwner` | Project update/delete, member management, Task delete | role rank ≥ `Owner` |
| `CommentAuthorOrProjectOwner` | Comment edit/delete | author **or** project Owner |
| `TimeLogAuthorOrProjectOwner` | TimeLog edit/delete | author **or** project Owner |

### 8.2 Implementation pattern (resource-based)

- `Auth/Requirements/ProjectRoleRequirement.cs` — holds `MinimumRole`.
- `Auth/Handlers/ProjectRoleHandler.cs` : `AuthorizationHandler<ProjectRoleRequirement, Project>`
  - Loads `ProjectMember` for `(context.User.GetUserId(), resource.Id)` via `AppDbContext`.
  - Succeeds if found and `member.Role >= requirement.MinimumRole`.
- `Auth/Requirements/CommentAuthorRequirement.cs` + handler over `Comment`:
  succeeds if `comment.AuthorId == userId` **or** the caller is project Owner.
- `Auth/Requirements/TimeLogAuthorRequirement.cs` + handler over `TimeLog`: same idea.

**Invocation pattern** (inside controllers/endpoints):

```csharp
var project = await _projects.GetForUserAsync(projectId, User.GetUserId()); // null if not a member
if (project is null) return NotFound();                                     // 404 — no leak
if (!(await _authz.AuthorizeAsync(User, project, "ProjectContributor")).Succeeded)
    return Forbid();                                                        // 403
```

For nested resources, **load the entity first** (task/comment/time log),
verify it belongs to `{projectId}`, then authorize against the entity or its project.

Rules of thumb:
- **Not a member → 404.** **Member but insufficient role → 403.**
- Do not rely solely on `[Authorize(Policy=...)]` attributes for these endpoints —
  resource-based checks require the loaded resource, so call `IAuthorizationService`
  explicitly (attributes can still guard the "Authenticated" baseline).

---

## 9. Cross-Cutting Concerns

### 9.1 Validation rules (return `400` with ProblemDetails `errors`)
| Rule | Where |
|---|---|
| email format + uniqueness | register |
| password ≥ 8 chars | register |
| project name required ≤100 | create/update project |
| task title required ≤200; description ≤4000 | create/update task |
| comment body required ≤2000 | create/update comment |
| time log minutes 1–1440; workDate not in the future | create/update time log |
| `page ≥ 1`, `1 ≤ pageSize ≤ 100` | all list endpoints |
| invalid enum string | model binding → 400 |

### 9.2 Status codes
| Code | When |
|---|---|
| 200 / 201 / 204 | success (204 for DELETE and logout) |
| 400 | validation / malformed query |
| 401 | missing/expired token |
| 403 | authenticated, is a member, but role too low |
| 404 | resource missing **or caller not a member** |
| 409 | last-owner violation, duplicate membership, already-member |
| 422 | business rule (e.g., assignee is a Viewer) |

### 9.3 Seed data (`DbSeeder`, runs on startup in Development)
Users (password `Passw0rd!` for all):
- `alice@example.com` — Owner of project "Website Redesign"
- `bob@example.com` — Member of "Website Redesign"
- `carol@example.com` — Viewer of "Website Redesign"
- `dave@example.com` — not a member (for 404 tests)

Project "Website Redesign" with 5 tasks (mixed statuses/priorities/assignees),
2 comments, 2 time logs. This makes Swagger testing instant.

### 9.4 Swagger
Enable Bearer auth in Swagger UI (Authorize button), call `/api/auth/login`
with `alice@…` / `Passw0rd!`, paste the access token, test all endpoints.

---

## 10. Definition of Done & Build Order

### Build order (implement and verify one phase at a time)
1. **Scaffold**: solution, Web API project, SQLite `AppDbContext`, entities + enums, initial migration.
2. **Auth**: User entity, register/login/refresh/logout/me, JWT service, `PasswordHasher`, Swagger JWT support.
3. **Projects + membership**: CRUD + member management + `ProjectMember`/`ProjectOwner` policies and `ProjectRoleHandler`. Seed data.
4. **Tasks**: nested CRUD + search/filter/sort/paging + `ProjectContributor` policy + `ProjectOwner` delete.
5. **Comments** and **TimeLogs**: nested CRUD + author-or-owner policies (`CommentAuthorHandler`, `TimeLogAuthorHandler`).
6. **`/api/me/tasks`** cross-project endpoint.
7. **Polish**: ProblemDetails everywhere, validation messages, README with curl examples.
8. **(Optional)** xUnit integration tests for the scenarios below.

### Acceptance test scenarios (must pass via Swagger/curl)

| # | Scenario | Expected |
|---|---|---|
| 1 | Anonymous calls `GET /api/projects` | 401 |
| 2 | Alice creates project | 201, she is Owner |
| 3 | Alice adds Bob as `Member`, Carol as `Viewer` | 201 / 200 |
| 4 | Dave (non-member) `GET /api/projects/{id}` | **404** |
| 5 | Carol (Viewer) `POST …/tasks` | **403** |
| 6 | Bob (Member) `POST …/tasks` | 201 |
| 7 | Bob `PATCH …/tasks/{id}` (change status to Done) | 200, `completedAt` set |
| 8 | Bob `DELETE …/tasks/{id}` | **403** (only Owner) |
| 9 | Alice `DELETE …/tasks/{id}` | 204 |
| 10 | Carol `POST …/comments` | 403 |
| 11 | Bob creates comment; edits own | 201 → 200 |
| 12 | Bob edits Alice's comment | 403 |
| 13 | Alice edits Bob's comment | 200 (Owner moderation) |
| 14 | Bob edits own time log | 200 |
| 15 | Alice edits Bob's time log | 200 (Owner) |
| 16 | Search: `?search=login&status=Todo` returns only matches | filtered correctly |
| 17 | Alice tries to remove herself as sole Owner | **409** |
| 18 | Alice transfers ownership to Bob → Alice becomes Member | 200; Alice `DELETE` task now → 403 |
| 19 | `GET /api/me/tasks` as Bob | only tasks assigned to Bob, incl. `projectName` |
| 20 | Expired access token → `POST /api/auth/refresh` → new tokens work | 200 |