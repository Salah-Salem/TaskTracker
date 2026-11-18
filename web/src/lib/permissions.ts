import type { ProjectRole } from '../api/types';

/**
 * UI-side mirror of the permission matrix (SETUP_PRO.md §6.2). Purely cosmetic: it only decides
 * which controls to show. The API enforces every rule independently (US-B3).
 * `myRole` comes from ProjectDto — the API already reports Owner for system Admins.
 */
const rank: Record<ProjectRole, number> = { Viewer: 1, Member: 2, Owner: 3 };

export const atLeast = (role: ProjectRole | undefined, min: ProjectRole) => !!role && rank[role] >= rank[min];

export const can = {
  editProject: (r?: ProjectRole) => atLeast(r, 'Owner'),
  deleteProject: (r?: ProjectRole) => atLeast(r, 'Owner'),
  manageMembers: (r?: ProjectRole) => atLeast(r, 'Owner'),
  createTask: (r?: ProjectRole) => atLeast(r, 'Member'),
  editTask: (r?: ProjectRole) => atLeast(r, 'Member'),
  deleteTask: (r?: ProjectRole) => atLeast(r, 'Owner'),
  comment: (r?: ProjectRole) => atLeast(r, 'Member'),
  logTime: (r?: ProjectRole) => atLeast(r, 'Member'),
  /** Edit/delete a comment or time log: its author, or the project Owner (moderation). */
  editAuthored: (r: ProjectRole | undefined, authorId: string, me: string | undefined) =>
    atLeast(r, 'Owner') || (!!me && authorId === me),
  /** Only Owners/Members can be assignees. */
  beAssigned: (r: ProjectRole) => atLeast(r, 'Member'),
};
