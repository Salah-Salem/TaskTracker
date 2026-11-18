// Mirrors the API contract (SETUP_PRO.md §9). Enums travel as strings.

export type GlobalRole = 'Admin' | 'User';
export type ProjectRole = 'Viewer' | 'Member' | 'Owner';
export type TaskStatus = 'Todo' | 'InProgress' | 'InReview' | 'Done' | 'Cancelled';
export type TaskPriority = 'Low' | 'Medium' | 'High' | 'Critical';

export const TASK_STATUSES: TaskStatus[] = ['Todo', 'InProgress', 'InReview', 'Done', 'Cancelled'];
export const TASK_PRIORITIES: TaskPriority[] = ['Low', 'Medium', 'High', 'Critical'];

export const STATUS_LABEL: Record<TaskStatus, string> = {
  Todo: 'To do',
  InProgress: 'In progress',
  InReview: 'In review',
  Done: 'Done',
  Cancelled: 'Cancelled',
};

/** Every response is wrapped: { success, data, errors }. */
export interface ApiEnvelope<T> {
  success: boolean;
  data: T | null;
  errors: string[];
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface UserDto {
  id: string;
  email: string;
  displayName: string;
  roles: GlobalRole[];
  isActive: boolean;
}

export interface AuthResponse {
  accessToken: string;
  expiresAtUtc: string;
  refreshToken: string;
  refreshTokenExpiresAtUtc: string;
  user: UserDto;
}

export interface ProjectDto {
  id: string;
  name: string;
  description: string | null;
  myRole: ProjectRole;
  createdByUserId: string;
  createdAt: string;
  updatedAt: string;
}

export interface MemberDto {
  userId: string;
  email: string;
  displayName: string;
  role: ProjectRole;
  joinedAt: string;
}

export interface TaskDto {
  id: string;
  projectId: string;
  projectName: string;
  title: string;
  description: string | null;
  status: TaskStatus;
  priority: TaskPriority;
  assigneeId: string | null;
  assigneeName: string | null;
  dueDate: string | null; // yyyy-MM-dd
  createdById: string;
  createdAt: string;
  updatedAt: string;
  completedAt: string | null;
}

export interface CommentDto {
  id: string;
  taskId: string;
  authorId: string;
  authorName: string;
  body: string;
  createdAt: string;
  updatedAt: string;
}

export interface TimeLogDto {
  id: string;
  taskId: string;
  userId: string;
  userName: string;
  minutes: number;
  workDate: string; // yyyy-MM-dd
  note: string | null;
  createdAt: string;
}

export type SortDir = 'asc' | 'desc';
export type TaskSortBy = 'title' | 'status' | 'priority' | 'dueDate' | 'createdAt' | 'updatedAt';
export type ProjectSortBy = 'name' | 'createdAt' | 'updatedAt';

export interface TaskQuery {
  search?: string;
  status?: TaskStatus[];
  priority?: TaskPriority[];
  /** user id or 'none' (project task list only) */
  assigneeId?: string;
  dueBefore?: string;
  dueAfter?: string;
  sortBy?: TaskSortBy;
  sortDir?: SortDir;
  page?: number;
  pageSize?: number;
}

export interface ProjectQuery {
  search?: string;
  role?: ProjectRole;
  sortBy?: ProjectSortBy;
  sortDir?: SortDir;
  page?: number;
  pageSize?: number;
}

/** Fields for PATCH: omitted = unchanged, null = clear. */
export interface TaskPatch {
  title?: string;
  description?: string | null;
  status?: TaskStatus;
  priority?: TaskPriority;
  assigneeId?: string | null;
  dueDate?: string | null;
}

export interface TaskCreate {
  title: string;
  description?: string | null;
  priority?: TaskPriority;
  assigneeId?: string | null;
  dueDate?: string | null;
}
