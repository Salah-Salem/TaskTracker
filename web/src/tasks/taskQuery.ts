import { useSearchParams } from 'react-router-dom';
import {
  TASK_PRIORITIES, TASK_STATUSES,
  type SortDir, type TaskPriority, type TaskQuery, type TaskSortBy, type TaskStatus,
} from '../api/types';

export const TASK_PAGE_SIZE = 20;

/** URL query string ⇄ TaskQuery. Filters live in the URL so they survive reloads and can be shared. */
export function parseTaskQuery(p: URLSearchParams): TaskQuery {
  const list = <T extends string>(key: string, allowed: readonly T[]) =>
    p.getAll(key).filter((v): v is T => (allowed as readonly string[]).includes(v));
  const [sortBy, sortDir] = (p.get('sort') ?? 'createdAt:desc').split(':') as [TaskSortBy, SortDir];
  return {
    search: p.get('search') ?? undefined,
    status: list<TaskStatus>('status', TASK_STATUSES),
    priority: list<TaskPriority>('priority', TASK_PRIORITIES),
    assigneeId: p.get('assignee') ?? undefined,
    dueAfter: p.get('dueAfter') ?? undefined,
    dueBefore: p.get('dueBefore') ?? undefined,
    sortBy,
    sortDir,
    page: Number(p.get('page') ?? 1) || 1,
    pageSize: TASK_PAGE_SIZE,
  };
}

/** Number of active filters (search included) — drives the "Clear filters" button (US-C6). */
export function activeFilterCount(q: TaskQuery): number {
  return [q.search, q.assigneeId, q.dueAfter, q.dueBefore].filter(Boolean).length
    + (q.status?.length ?? 0) + (q.priority?.length ?? 0);
}

const FILTER_KEYS = ['search', 'status', 'priority', 'assignee', 'dueAfter', 'dueBefore', 'page'];

export function useTaskQuery() {
  const [params, setParams] = useSearchParams();
  const query = parseTaskQuery(params);

  function set(key: string, value: string | string[] | undefined) {
    const p = new URLSearchParams(params);
    p.delete(key);
    if (Array.isArray(value)) value.forEach((v) => p.append(key, v));
    else if (value) p.set(key, value);
    if (key !== 'page') p.delete('page');
    setParams(p, { replace: true });
  }

  function toggle(key: 'status' | 'priority', value: string) {
    const current = params.getAll(key);
    set(key, current.includes(value) ? current.filter((v) => v !== value) : [...current, value]);
  }

  function clear() {
    const p = new URLSearchParams(params);
    FILTER_KEYS.forEach((k) => p.delete(k));
    setParams(p, { replace: true });
  }

  return { query, params, set, toggle, clear };
}
