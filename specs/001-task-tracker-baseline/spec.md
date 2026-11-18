# Feature Specification: Task Tracker Baseline (As-Built)

**Feature Branch**: `001-task-tracker-baseline`

**Created**: 2026-11-18

**Status**: Draft (retroactive — describes behavior already implemented)

**Input**: User description: "make specs for this project based on implemented"

> **Scope note**: This specification documents the Project Task Tracker as it is currently built
> (milestones 1–5 of `specs/SETUP_PRO.md`). It restates *what* the product does and *why*, without
> implementation detail. `specs/SETUP_PRO.md` remains the technical contract (endpoints, data shapes,
> technology choices); `specs/TASKS.md` is retained as historical reference.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Create an account and sign in securely (Priority: P1)

A person registers with their email, a display name and a password, then signs in. Their session stays
alive while they work, survives brief inactivity through automatic renewal, and ends everywhere when they
sign out.

**Why this priority**: Nothing else in the product is reachable without an identity; every authorization
decision depends on knowing who the caller is.

**Independent Test**: Register a new account, sign in, view "my profile", sign out, and confirm the old
session can no longer be used.

**Acceptance Scenarios**:

1. **Given** no account exists for `new@example.com`, **When** the person registers with a valid email, a display name and an 8+ character password, **Then** the account is created with the standard (non-admin) role and the response never contains the password.
2. **Given** an account already exists for an email, **When** someone registers with that email (in any letter case), **Then** registration is rejected with "Email already registered".
3. **Given** a registered user, **When** they sign in with a wrong password or an unknown email, **Then** they see the same generic "Invalid email or password" message.
4. **Given** a user has failed to sign in 5 times in a row, **When** they try again within 15 minutes, **Then** sign-in is refused even with the correct password.
5. **Given** a deactivated account, **When** its owner signs in with the correct password, **Then** they are told the account is disabled.
6. **Given** a signed-in user whose short-lived session has expired, **When** the client renews using the renewal credential, **Then** a fresh session is issued and the old renewal credential stops working.
7. **Given** a renewal credential that has already been used once, **When** it is presented again, **Then** it is refused and **all** of that user's sessions are revoked (suspected theft).
8. **Given** a signed-in user with sessions on several devices, **When** they sign out, **Then** every session and renewal credential for that user stops working immediately.
9. **Given** an anonymous caller or one with a missing, malformed or expired credential, **When** they access any protected area, **Then** they are refused as unauthenticated.

---

### User Story 2 - Create projects and manage who can do what (Priority: P1)

Any signed-in user can create a project and becomes its sole Owner. The Owner invites existing users by
email as Members (contributors) or Viewers (read-only), changes their roles, removes them, and can hand
ownership to another member.

**Why this priority**: Projects are the container for all work, and per-project roles are the core of the
authorization model this product exists to prove.

**Independent Test**: Alice creates a project, adds Bob as Member and Carol as Viewer, then confirms that
Dave (not a member) cannot even see that the project exists.

**Acceptance Scenarios**:

1. **Given** a signed-in user, **When** they create a project with a name (and optional description), **Then** the project exists and they are its Owner.
2. **Given** a project Owner, **When** they add a registered user by email as Member or Viewer, **Then** that user appears in the member list with that role.
3. **Given** a project Owner, **When** they try to add someone who is already a member, grant the Owner role directly, or remove/demote themselves as Owner, **Then** the action is refused as a conflict with an explanatory message.
4. **Given** a project Owner, **When** they add an email that belongs to no registered user, **Then** they are told the user was not found.
5. **Given** a project Owner, **When** they transfer ownership to an existing member, **Then** that member becomes Owner and the previous Owner becomes a Member.
6. **Given** a project Owner, **When** they transfer ownership to someone who is not a member, **Then** the request is rejected as invalid.
7. **Given** a Member or Viewer, **When** they try to rename/delete the project or manage members, **Then** they are denied.
8. **Given** a user who is not a member of a project, **When** they try to view it or anything inside it, **Then** the system responds as if the project does not exist.
9. **Given** a member is demoted to Viewer or removed, **When** the change is saved, **Then** any tasks assigned to them in that project become unassigned.
10. **Given** a project Owner, **When** they delete the project, **Then** the project and everything inside it disappear for all members.

---

### User Story 3 - Track tasks through their lifecycle (Priority: P1)

Project contributors create tasks with a title, optional description, priority, assignee and due date,
move them through statuses, edit any field independently, and Owners delete tasks that are no longer needed.

**Why this priority**: Tasks are the primary unit of work; this is the everyday value of the product.

**Independent Test**: As a Member, create a task, mark it Done (completion time recorded), reopen it
(completion time cleared); as the Owner, delete it.

**Acceptance Scenarios**:

1. **Given** an Owner or Member, **When** they create a task with a title, **Then** it starts in status "To do" with priority "Medium" unless another priority was chosen.
2. **Given** a Viewer, **When** they try to create or edit a task, **Then** they are denied.
3. **Given** a task, **When** a contributor changes only its status (or any single field), **Then** only that field changes; explicitly clearing the description, assignee or due date removes the value.
4. **Given** a task not in "Done", **When** its status becomes "Done", **Then** its completion time is recorded; **When** it later leaves "Done", **Then** the completion time is cleared.
5. **Given** a contributor assigning a task, **When** the chosen person is not a project member, **Then** the request is rejected as invalid; **When** the person is a Viewer, **Then** it is rejected because Viewers cannot be assigned work.
6. **Given** a Member, **When** they try to delete a task, **Then** they are denied; **Given** the Owner, **When** they delete it, **Then** it disappears, and deleting it again reports it as not found.
7. **Given** a task belonging to project A, **When** it is requested through project B, **Then** it is reported as not found.

---

### User Story 4 - Find tasks and projects quickly (Priority: P2)

Users narrow long lists with free-text search, filters and sorting, and page through results.

**Why this priority**: Makes the product usable once there are more than a handful of items; not needed
for the first task to be created.

**Independent Test**: On the demo project, search "login" with status "To do" and get exactly one task:
"Fix login redirect bug".

**Acceptance Scenarios**:

1. **Given** a project's tasks, **When** a member searches by text, **Then** only tasks whose title or description contains the text (ignoring case) are returned, and special characters such as `%` or `_` are matched literally.
2. **Given** a project's tasks, **When** a member filters by one or more statuses, one or more priorities, an assignee (or "unassigned"), and/or a due-date range, **Then** only tasks matching **all** chosen filters are returned.
3. **Given** a task list, **When** the user sorts by title, status, priority, due date, created or updated time in ascending or descending order, **Then** results follow that order (default: newest first).
4. **Given** the project list, **When** the user searches by name/description, filters by their role, or sorts by name/created/updated, **Then** only their own projects are shown accordingly.
5. **Given** any list, **When** results exceed one page, **Then** they are paged (20 per page by default, at most 100) with the total count reported.
6. **Given** an invalid filter value, sort field, page number or reversed date range, **When** the list is requested, **Then** the request is rejected with a clear validation message (never a system error).
7. **Given** filters that match nothing, **When** the list is requested, **Then** an empty list with a total of 0 is returned.

---

### User Story 5 - Discuss tasks and log time (Priority: P2)

Contributors comment on tasks and record time spent. Authors can edit or delete their own entries; the
project Owner can moderate anyone's.

**Why this priority**: Adds collaboration and effort tracking on top of core task management, and proves
the "author or Owner" permission layer.

**Independent Test**: Bob comments on a task and edits it; Bob cannot edit Alice's comment; Alice (Owner)
can edit Bob's. Repeat for a time log.

**Acceptance Scenarios**:

1. **Given** an Owner or Member, **When** they add a comment (1–2000 characters) to a task, **Then** it appears at the top of the task's comment list (newest first) with their name.
2. **Given** a Viewer, **When** they try to comment or log time, **Then** they are denied, but they can read existing comments and time logs.
3. **Given** a comment or time log, **When** its author edits or deletes it, **Then** the change is applied; **When** another non-Owner member tries, **Then** they are denied; **When** the project Owner tries, **Then** it is applied (moderation).
4. **Given** a contributor logging time, **When** they enter 1–1440 minutes, an optional work date and an optional note (≤ 500 characters), **Then** the entry is recorded against **themselves**, with the work date defaulting to today; any attempt to log time on behalf of someone else is ignored.
5. **Given** a time entry, **When** the work date is in the future or minutes are outside 1–1440, **Then** it is rejected.
6. **Given** a comment or time log that belongs to a different task or project than the one requested, **When** it is accessed, **Then** it is reported as not found.

---

### User Story 6 - See my work across all projects (Priority: P2)

A user opens "My tasks" and sees every task assigned to them across all projects they belong to, each
labelled with its project, using the same search, filter and sort options as a project task list.

**Why this priority**: The personal to-do view is the most common daily entry point but depends on
projects and tasks existing first.

**Independent Test**: Sign in as Bob, open My tasks, and confirm only Bob's assignments appear, each
showing its project name.

**Acceptance Scenarios**:

1. **Given** a user assigned to tasks in several projects, **When** they open My tasks, **Then** they see exactly those tasks, each with its project name.
2. **Given** a user was removed from a project, **When** they open My tasks, **Then** tasks from that project no longer appear.
3. **Given** My tasks, **When** the user applies search, status, priority, due-date filters or sorting, **Then** they behave as in a project task list (assignee filter does not apply).

---

### User Story 7 - Administer user accounts (Priority: P3)

A system Administrator lists all users, promotes or demotes them between Administrator and standard user,
and deactivates or reactivates accounts. Administrators also act as Owner on every project for support.

**Why this priority**: Needed for operating the system and moderation, but not for day-to-day use.

**Independent Test**: As admin, deactivate Dave; Dave's next action and next session renewal are both
refused; reactivate Dave and confirm he can sign in again (old sessions stay dead).

**Acceptance Scenarios**:

1. **Given** an Administrator, **When** they open the user list, **Then** they see all users (paged, ordered by email) with roles and active status.
2. **Given** a non-Administrator, **When** they try any user-administration action, **Then** they are denied.
3. **Given** an Administrator changes a user's role, **When** that user next acts with their existing session, **Then** it is refused; after renewing, their new session carries the new role.
4. **Given** an Administrator deactivates a user, **When** that user next acts or tries to renew, **Then** both are refused; reactivation allows a fresh sign-in but does not revive old sessions.
5. **Given** only one active Administrator remains, **When** someone tries to demote or deactivate them, **Then** the action is refused ("At least one active Admin is required").
6. **Given** an Administrator, **When** they open any project, **Then** they have Owner-level abilities there, and every administrative change is recorded in the audit log.

---

### User Story 8 - Use a web interface that adapts to my role (Priority: P3)

Users work through a browser application: sign in/register, browse and search projects, open a project
to manage tasks and members, open a task to comment and log time, view My tasks, and (for Administrators)
manage users. Controls a user is not allowed to use are hidden.

**Why this priority**: Makes the product usable by non-technical people; all rules are already enforced
by the system itself, so the interface is a convenience layer.

**Independent Test**: Sign in as Carol (Viewer) and confirm no create/edit/delete/comment/log-time controls
are shown on the demo project, while Bob (Member) sees create/edit but not delete.

**Acceptance Scenarios**:

1. **Given** a signed-out visitor, **When** they open any page, **Then** they are taken to sign-in; after signing in they reach the projects list.
2. **Given** a user's session expires mid-use, **When** their next action happens, **Then** the session is renewed silently and the action completes; if renewal fails, they are returned to sign-in.
3. **Given** a Viewer, Member or Owner on a project, **When** they view it, **Then** only the actions their role permits are offered.
4. **Given** a non-Administrator, **When** they navigate to user administration, **Then** they cannot access it.

---

### Edge Cases

- Requests for a project, task, comment or time log the caller cannot see are indistinguishable from requests for ones that do not exist.
- A project always has exactly one Owner: removing/demoting the Owner or granting Owner directly is refused; ownership moves only by transfer.
- Deleted projects and tasks (and their comments/time logs) vanish from every list, search and My tasks view immediately.
- A user removed from a project or demoted to Viewer loses their assignments in that project.
- Text search treats wildcard characters (`%`, `_`, `\`) as plain text.
- Status and priority filters accept repeated values or a comma-separated list.
- Partial edits distinguish "field not sent" (unchanged) from "field sent as empty" (cleared).
- Emails are matched case-insensitively for registration, sign-in and adding members.
- Reusing a renewal credential revokes all of that user's sessions, not just the reused one.
- The last active Administrator cannot be demoted or deactivated.
- Status changes are free-form (any status to any status); only "Done" has a side effect (completion time).

## Requirements *(mandatory)*

### Functional Requirements

**Accounts & sessions**

- **FR-001**: System MUST let a person register with a unique email, a display name (≤ 100 characters) and a password of at least 8 characters; new accounts receive the standard user role.
- **FR-002**: System MUST store passwords only in a non-reversible form and MUST never return passwords, session credentials or internal security values in responses or logs.
- **FR-003**: System MUST authenticate users by email and password, returning a generic failure message that does not reveal whether the email exists.
- **FR-004**: System MUST lock an account for 15 minutes after 5 consecutive failed sign-in attempts.
- **FR-005**: System MUST issue a short-lived session (15 minutes) plus a single-use renewal credential valid for 7 days; each renewal MUST replace the renewal credential.
- **FR-006**: System MUST treat reuse of an already-used renewal credential as theft and revoke all of that user's renewal credentials.
- **FR-007**: System MUST end all of a user's sessions immediately on sign-out, deactivation, or a change of their global role (role change still allows renewal so the next session carries the new role).
- **FR-008**: System MUST verify on every request that the user still exists, is active and that their session has not been invalidated.

**Global roles & administration**

- **FR-009**: System MUST support two global roles — Administrator and standard user.
- **FR-010**: Administrators MUST be able to list all users (paged), change a user's global role, and deactivate/reactivate accounts; no one else may.
- **FR-011**: System MUST prevent demoting or deactivating the last active Administrator.
- **FR-012**: Administrators MUST be treated as Owner on every project.
- **FR-013**: System MUST record an audit log entry for security- and administration-relevant actions (registration, role changes, activation changes, project creation/update/deletion, membership changes, ownership transfer).

**Projects & membership**

- **FR-014**: Any signed-in user MUST be able to create a project (name required, ≤ 100; description optional, ≤ 1000) and becomes its Owner.
- **FR-015**: System MUST support per-project roles ranked Owner > Member > Viewer, where a higher role includes every permission of lower ones.
- **FR-016**: System MUST hide a project and everything inside it from non-members, responding as though it does not exist; members with an insufficient role MUST be explicitly denied.
- **FR-017**: Only the Owner MUST be able to edit or delete the project, add members by email (as Member or Viewer), change member roles, remove members, and transfer ownership.
- **FR-018**: System MUST guarantee exactly one Owner per project: granting Owner directly, removing or demoting the Owner, and adding an existing member MUST be refused as conflicts; transfer to a non-member MUST be rejected as invalid; adding an unknown email MUST report "User not found".
- **FR-019**: Transferring ownership MUST make the new Owner the sole Owner and the previous Owner a Member.
- **FR-020**: Demoting a member to Viewer or removing them MUST unassign their tasks in that project.
- **FR-021**: Deleting a project MUST remove it and all its tasks, comments and time logs from every view.

**Tasks**

- **FR-022**: Owners and Members MUST be able to create tasks (title required, ≤ 200; description ≤ 4000; priority Low/Medium/High/Critical defaulting to Medium; optional assignee; optional due date); new tasks start as "To do".
- **FR-023**: Owners and Members MUST be able to partially edit a task (title, description, status, priority, assignee, due date), where fields not supplied stay unchanged and description, assignee and due date can be explicitly cleared.
- **FR-024**: Task statuses MUST be To do, In progress, In review, Done and Cancelled, with any transition allowed; entering Done MUST record the completion time and leaving Done MUST clear it.
- **FR-025**: A task assignee MUST be an Owner or Member of the task's project; a non-member assignee MUST be rejected as invalid and a Viewer assignee MUST be rejected as a business-rule violation.
- **FR-026**: Only the Owner MUST be able to delete a task; deleted tasks MUST no longer be retrievable.
- **FR-027**: Every task MUST record who created it and when it was created, last updated and completed.

**Search, filtering, sorting & paging**

- **FR-028**: Task lists MUST support case-insensitive text search on title or description with wildcard characters matched literally.
- **FR-029**: Task lists MUST support filtering by multiple statuses, multiple priorities, assignee (including "unassigned", project lists only) and an inclusive due-date range, combined with AND.
- **FR-030**: Task lists MUST support sorting by title, status, priority, due date, created time or updated time, ascending or descending (default: created time, newest first).
- **FR-031**: The project list MUST show only the caller's projects (all projects for Administrators) and support text search on name/description, filtering by the caller's role, and sorting by name, created or updated time.
- **FR-032**: All lists MUST be paged (default 20, maximum 100 per page) and report the total count; invalid paging, filter or sort values MUST be rejected with a validation message.

**Comments & time logs**

- **FR-033**: Owners and Members MUST be able to comment on tasks (1–2000 characters); all project members MUST be able to read comments, newest first.
- **FR-034**: Owners and Members MUST be able to log time on tasks (1–1440 minutes, work date defaulting to today and not in the future, optional note ≤ 500 characters), always recorded against the caller; all members MUST be able to read time logs, newest first.
- **FR-035**: Only the author or the project Owner MUST be able to edit or delete a comment or time log.

**Personal view**

- **FR-036**: Each user MUST be able to list tasks assigned to them across all projects they currently belong to, each labelled with its project name, with the same search, filter, sort and paging options as project task lists (minus the assignee filter).

**Web interface**

- **FR-037**: The product MUST provide a browser interface covering sign-in, registration, project list, project detail (tasks and members), task detail (comments and time logs), My tasks and user administration.
- **FR-038**: The interface MUST renew expired sessions silently and return the user to sign-in when renewal fails.
- **FR-039**: The interface MUST hide actions the user's role does not permit; the system MUST still enforce every rule independently of the interface.

**Demonstration data**

- **FR-040**: In the development environment, the system MUST provide repeatable demo data: one Administrator, four standard users covering Owner, Member, Viewer and non-member of a "Website Redesign" project, five tasks (one per status), two comments and two time logs.

### Key Entities

- **User**: A person who can sign in. Has email (unique), display name, global role (Administrator or standard user), active flag and creation time.
- **Project**: A container of work. Has name, optional description, creator, creation/update times; removed projects are hidden everywhere.
- **Project Membership**: Links a user to a project with a role (Owner, Member or Viewer) and join time; one per user per project; exactly one Owner per project.
- **Task**: A unit of work in one project. Has title, optional description, status, priority, optional assignee (Owner/Member of the project), optional due date, creator, last editor, and created/updated/completed times.
- **Comment**: A message on a task by an author, with created/updated times.
- **Time Log**: Minutes worked on a task by the caller, on a work date, with an optional note.
- **Renewal Credential**: A single-use, expiring credential that lets a user obtain a new session; tracks revocation and which credential replaced it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A new user can register, sign in, create a project and add their first task in under 3 minutes.
- **SC-002**: 100% of the 20 documented acceptance scenarios (project walkthrough from anonymous access through session renewal) pass on every build.
- **SC-003**: In 100% of attempts, a non-member receives the same response for an existing project as for a non-existent one, and a member with too low a role is explicitly denied.
- **SC-004**: After sign-out, deactivation or role change, the user's previous session is refused on its very next use (0 requests accepted afterwards).
- **SC-005**: Searching the demo project for "login" with status "To do" returns exactly one task.
- **SC-006**: Filtered and searched task lists of up to 1,000 tasks return the first page in under 1 second for a typical user.
- **SC-007**: Invalid input on any operation produces a clear validation message, never an unexpected system error (0 system errors across the automated test suite).
- **SC-008**: No password, session credential or internal security value appears in any response body or log line during the automated test suite.
- **SC-009**: Users with a session expiring mid-task complete their action without being asked to sign in again (silent renewal succeeds whenever the renewal credential is still valid).

## Assumptions

- This is a proof of concept: one deployable service plus one browser application, not a production system.
- Password reset by email, single sign-on, multi-tenancy, attachments, activity feed, notifications, real-time updates, reporting, recycle bin/restore, mobile apps and localization are out of scope.
- Change-password (US-A5) is optional and not built.
- Removed projects and tasks are kept internally but are treated as permanently gone by every user-facing operation; comments, time logs and memberships are removed outright.
- Session credentials in the browser are stored locally for convenience (accepted proof-of-concept trade-off).
- Demo accounts all share the password `Passw0rd!` and exist only in the development environment.
- Remaining work from milestone 6 (coverage report ≥ 70%, README with usage examples) is outside this baseline specification.
