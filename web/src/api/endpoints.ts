import { call, http, toQuery } from './client';
import type {
  ApiEnvelope, AuthResponse, CommentDto, GlobalRole, MemberDto, PagedResult, ProjectDto, ProjectQuery,
  ProjectRole, TaskCreate, TaskDto, TaskPatch, TaskQuery, TimeLogDto, UserDto,
} from './types';

type E<T> = ApiEnvelope<T>;

export const authApi = {
  register: (body: { email: string; password: string; displayName: string }) =>
    call(http.post<E<UserDto>>('/api/auth/register', body)),
  login: (body: { email: string; password: string }) =>
    call(http.post<E<AuthResponse>>('/api/auth/login', body)),
  logout: () => http.post('/api/auth/logout'),
  me: () => call(http.get<E<UserDto>>('/api/auth/me')),
};

export const projectsApi = {
  list: (q: ProjectQuery = {}) => call(http.get<E<PagedResult<ProjectDto>>>(`/api/projects${toQuery(q)}`)),
  get: (id: string) => call(http.get<E<ProjectDto>>(`/api/projects/${id}`)),
  create: (body: { name: string; description?: string | null }) =>
    call(http.post<E<ProjectDto>>('/api/projects', body)),
  update: (id: string, body: { name?: string; description?: string | null }) =>
    call(http.patch<E<ProjectDto>>(`/api/projects/${id}`, body)),
  remove: (id: string) => http.delete(`/api/projects/${id}`),

  members: (id: string) => call(http.get<E<MemberDto[]>>(`/api/projects/${id}/members`)),
  addMember: (id: string, body: { email: string; role: Exclude<ProjectRole, 'Owner'> }) =>
    call(http.post<E<MemberDto>>(`/api/projects/${id}/members`, body)),
  changeMemberRole: (id: string, userId: string, role: Exclude<ProjectRole, 'Owner'>) =>
    call(http.patch<E<MemberDto>>(`/api/projects/${id}/members/${userId}`, { role })),
  removeMember: (id: string, userId: string) => http.delete(`/api/projects/${id}/members/${userId}`),
  transferOwnership: (id: string, newOwnerUserId: string) =>
    call(http.post<E<MemberDto[]>>(`/api/projects/${id}/transfer-ownership`, { newOwnerUserId })),
};

export const tasksApi = {
  list: (projectId: string, q: TaskQuery = {}) =>
    call(http.get<E<PagedResult<TaskDto>>>(`/api/projects/${projectId}/tasks${toQuery(q)}`)),
  mine: (q: TaskQuery = {}) => call(http.get<E<PagedResult<TaskDto>>>(`/api/me/tasks${toQuery(q)}`)),
  get: (projectId: string, taskId: string) =>
    call(http.get<E<TaskDto>>(`/api/projects/${projectId}/tasks/${taskId}`)),
  create: (projectId: string, body: TaskCreate) =>
    call(http.post<E<TaskDto>>(`/api/projects/${projectId}/tasks`, body)),
  update: (projectId: string, taskId: string, patch: TaskPatch) =>
    call(http.patch<E<TaskDto>>(`/api/projects/${projectId}/tasks/${taskId}`, patch)),
  remove: (projectId: string, taskId: string) => http.delete(`/api/projects/${projectId}/tasks/${taskId}`),
};

const taskUrl = (projectId: string, taskId: string) => `/api/projects/${projectId}/tasks/${taskId}`;

export const commentsApi = {
  list: (p: string, t: string) => call(http.get<E<PagedResult<CommentDto>>>(`${taskUrl(p, t)}/comments?pageSize=100`)),
  create: (p: string, t: string, body: string) => call(http.post<E<CommentDto>>(`${taskUrl(p, t)}/comments`, { body })),
  update: (p: string, t: string, id: string, body: string) =>
    call(http.patch<E<CommentDto>>(`${taskUrl(p, t)}/comments/${id}`, { body })),
  remove: (p: string, t: string, id: string) => http.delete(`${taskUrl(p, t)}/comments/${id}`),
};

export const timeLogsApi = {
  list: (p: string, t: string) => call(http.get<E<PagedResult<TimeLogDto>>>(`${taskUrl(p, t)}/timelogs?pageSize=100`)),
  create: (p: string, t: string, body: { minutes: number; workDate?: string | null; note?: string | null }) =>
    call(http.post<E<TimeLogDto>>(`${taskUrl(p, t)}/timelogs`, body)),
  update: (p: string, t: string, id: string, body: { minutes?: number; workDate?: string; note?: string | null }) =>
    call(http.patch<E<TimeLogDto>>(`${taskUrl(p, t)}/timelogs/${id}`, body)),
  remove: (p: string, t: string, id: string) => http.delete(`${taskUrl(p, t)}/timelogs/${id}`),
};

export const usersApi = {
  list: (page = 1, pageSize = 20) => call(http.get<E<PagedResult<UserDto>>>(`/api/users${toQuery({ page, pageSize })}`)),
  changeRole: (id: string, role: GlobalRole) => call(http.put<E<UserDto>>(`/api/users/${id}/role`, { role })),
  deactivate: (id: string) => call(http.put<E<UserDto>>(`/api/users/${id}/deactivate`)),
  activate: (id: string) => call(http.put<E<UserDto>>(`/api/users/${id}/activate`)),
};
