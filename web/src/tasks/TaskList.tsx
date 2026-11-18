import { useEffect, useState } from 'react';
import { STATUS_LABEL, TASK_PRIORITIES, TASK_STATUSES, type MemberDto, type TaskDto } from '../api/types';
import { PriorityBadge, StatusBadge } from '../components/ui';
import { formatDate, isOverdue, useDebounced } from '../lib/hooks';
import { activeFilterCount, type useTaskQuery } from './taskQuery';

type QueryApi = ReturnType<typeof useTaskQuery>;

/** Search box + status/priority chips + assignee + due range + sort. Every filter ANDs with the rest. */
export function TaskFilters({ api, members }: { api: QueryApi; members?: MemberDto[] }) {
  const { query, set, toggle, clear } = api;
  const [text, setText] = useState(query.search ?? '');
  const debounced = useDebounced(text);

  // Push the debounced search text into the URL…
  useEffect(() => {
    if ((query.search ?? '') !== debounced) set('search', debounced || undefined);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [debounced]);
  // …and pull it back when the URL changes from elsewhere (e.g. "Clear filters").
  useEffect(() => { if (!query.search) setText(''); }, [query.search]);

  const active = activeFilterCount(query);

  return (
    <div className="filters">
      <div className="toolbar">
        <input className="search" type="search" placeholder="Search title or description…" aria-label="Search tasks"
          value={text} onChange={(e) => setText(e.target.value)} />
        {members && (
          <select aria-label="Assignee" value={query.assigneeId ?? ''} onChange={(e) => set('assignee', e.target.value || undefined)}>
            <option value="">Anyone</option>
            <option value="none">Unassigned</option>
            {members.map((m) => <option key={m.userId} value={m.userId}>{m.displayName}</option>)}
          </select>
        )}
        <select aria-label="Sort" value={`${query.sortBy}:${query.sortDir}`} onChange={(e) => set('sort', e.target.value)}>
          <option value="createdAt:desc">Newest first</option>
          <option value="createdAt:asc">Oldest first</option>
          <option value="updatedAt:desc">Recently updated</option>
          <option value="dueDate:asc">Due date ↑</option>
          <option value="dueDate:desc">Due date ↓</option>
          <option value="priority:desc">Priority: high → low</option>
          <option value="priority:asc">Priority: low → high</option>
          <option value="status:asc">Status</option>
          <option value="title:asc">Title A–Z</option>
        </select>
      </div>

      <div className="chip-row" role="group" aria-label="Filter by status">
        <span className="chip-label">Status</span>
        {TASK_STATUSES.map((s) => (
          <button key={s} type="button" className={`chip${query.status?.includes(s) ? ' chip-on' : ''}`}
            aria-pressed={query.status?.includes(s)} onClick={() => toggle('status', s)}>{STATUS_LABEL[s]}</button>
        ))}
      </div>
      <div className="chip-row" role="group" aria-label="Filter by priority">
        <span className="chip-label">Priority</span>
        {TASK_PRIORITIES.map((p) => (
          <button key={p} type="button" className={`chip${query.priority?.includes(p) ? ' chip-on' : ''}`}
            aria-pressed={query.priority?.includes(p)} onClick={() => toggle('priority', p)}>{p}</button>
        ))}
        <span className="chip-label chip-label-gap">Due</span>
        <input type="date" aria-label="Due from" value={query.dueAfter ?? ''} onChange={(e) => set('dueAfter', e.target.value || undefined)} />
        <span className="muted">to</span>
        <input type="date" aria-label="Due until" value={query.dueBefore ?? ''} onChange={(e) => set('dueBefore', e.target.value || undefined)} />
        {active > 0 && (
          <button type="button" className="btn btn-ghost btn-sm clear-btn" onClick={() => { setText(''); clear(); }}>
            Clear {active} filter{active > 1 ? 's' : ''}
          </button>
        )}
      </div>
    </div>
  );
}

export function TaskTable({ tasks, onOpen, showProject }: {
  tasks: TaskDto[]; onOpen: (t: TaskDto) => void; showProject?: boolean;
}) {
  return (
    <div className="table-wrap">
      <table className="table">
        <thead>
          <tr>
            <th>Title</th>
            {showProject && <th>Project</th>}
            <th>Status</th>
            <th>Priority</th>
            <th>Assignee</th>
            <th>Due</th>
          </tr>
        </thead>
        <tbody>
          {tasks.map((t) => (
            <tr key={t.id} onClick={() => onOpen(t)} className="clickable">
              <td>
                <button className="link-btn row-title" onClick={(e) => { e.stopPropagation(); onOpen(t); }}>{t.title}</button>
              </td>
              {showProject && <td className="muted">{t.projectName}</td>}
              <td><StatusBadge status={t.status} /></td>
              <td><PriorityBadge priority={t.priority} /></td>
              <td>{t.assigneeName ?? <span className="muted">Unassigned</span>}</td>
              <td className={isOverdue(t.dueDate, t.status) ? 'overdue' : undefined}>
                {formatDate(t.dueDate)}{isOverdue(t.dueDate, t.status) && <span className="sr-only"> (overdue)</span>}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
