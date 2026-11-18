import { useState, type FormEvent } from 'react';
import { errorMessages } from '../api/client';
import { commentsApi, tasksApi, timeLogsApi } from '../api/endpoints';
import { STATUS_LABEL, TASK_STATUSES, type CommentDto, type MemberDto, type ProjectRole, type TaskStatus, type TimeLogDto } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ConfirmDialog, Errors, Modal, PriorityBadge, Spinner, StatusBadge } from '../components/ui';
import { formatDate, formatDateTime, formatMinutes, isOverdue, useAsync } from '../lib/hooks';
import { can } from '../lib/permissions';
import { TaskFormModal } from './TaskFormModal';

/**
 * Task details + comments + time logs. Controls follow the caller's project role:
 * Viewers get a read-only view; Members edit/comment/log; only Owners delete the task
 * or moderate other people's comments and time logs.
 */
export function TaskDetailModal({ projectId, taskId, role, members, onChanged, onClose }: {
  projectId: string; taskId: string; role?: ProjectRole; members?: MemberDto[];
  onChanged: () => void; onClose: () => void;
}) {
  const task = useAsync(() => tasksApi.get(projectId, taskId), [projectId, taskId]);
  const [editing, setEditing] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [statusErrors, setStatusErrors] = useState<string[]>([]);
  const t = task.data;

  async function quickStatus(status: TaskStatus) {
    setStatusErrors([]);
    try {
      task.setData(await tasksApi.update(projectId, taskId, { status }));
      onChanged();
    } catch (e) {
      setStatusErrors(errorMessages(e));
    }
  }

  if (editing && t && members) {
    return <TaskFormModal projectId={projectId} task={t} members={members} onClose={() => setEditing(false)}
      onSaved={(saved) => { task.setData(saved); setEditing(false); onChanged(); }} />;
  }

  if (deleting && t) {
    return <ConfirmDialog title="Delete task?" danger confirmLabel="Delete task"
      message={<>“{t.title}” and its comments and time logs will disappear for everyone.</>}
      onConfirm={async () => { await tasksApi.remove(projectId, taskId); onChanged(); onClose(); }}
      onClose={() => setDeleting(false)} />;
  }

  return (
    <Modal wide title={t?.title ?? 'Task'} onClose={onClose} footer={t && (
      <>
        {can.deleteTask(role) && <button className="btn btn-danger-ghost" onClick={() => setDeleting(true)}>Delete</button>}
        <span className="spacer" />
        {can.editTask(role) && members && <button className="btn btn-primary" onClick={() => setEditing(true)}>Edit</button>}
        <button className="btn btn-ghost" onClick={onClose}>Close</button>
      </>
    )}>
      <Errors errors={task.errors} />
      {!t ? (task.loading && <Spinner />) : (
        <div className="detail">
          <dl className="detail-grid">
            <dt>Status</dt>
            <dd>
              {can.editTask(role) ? (
                <select aria-label="Status" value={t.status} onChange={(e) => quickStatus(e.target.value as TaskStatus)}>
                  {TASK_STATUSES.map((s) => <option key={s} value={s}>{STATUS_LABEL[s]}</option>)}
                </select>
              ) : <StatusBadge status={t.status} />}
              {t.completedAt && <span className="muted small"> · completed {formatDateTime(t.completedAt)}</span>}
            </dd>
            <dt>Priority</dt><dd><PriorityBadge priority={t.priority} /></dd>
            <dt>Assignee</dt><dd>{t.assigneeName ?? <span className="muted">Unassigned</span>}</dd>
            <dt>Due</dt><dd className={isOverdue(t.dueDate, t.status) ? 'overdue' : undefined}>{formatDate(t.dueDate)}</dd>
            <dt>Project</dt><dd>{t.projectName}</dd>
            <dt>Updated</dt><dd>{formatDateTime(t.updatedAt)}</dd>
          </dl>
          <Errors errors={statusErrors} />
          <p className="description">{t.description || <span className="muted">No description.</span>}</p>

          <div className="detail-columns">
            <Comments projectId={projectId} taskId={taskId} role={role} />
            <TimeLogs projectId={projectId} taskId={taskId} role={role} />
          </div>
        </div>
      )}
    </Modal>
  );
}

function Comments({ projectId, taskId, role }: { projectId: string; taskId: string; role?: ProjectRole }) {
  const { user } = useAuth();
  const list = useAsync(() => commentsApi.list(projectId, taskId), [projectId, taskId]);
  const [body, setBody] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [editing, setEditing] = useState<{ id: string; body: string } | null>(null);

  async function run(action: () => Promise<unknown>) {
    setErrors([]);
    try {
      await action();
      list.reload();
      return true;
    } catch (e) {
      setErrors(errorMessages(e));
      return false;
    }
  }

  async function add(e: FormEvent) {
    e.preventDefault();
    if (body.trim() && (await run(() => commentsApi.create(projectId, taskId, body.trim())))) setBody('');
  }

  return (
    <section className="panel">
      <h3>Comments {list.data && <span className="count">{list.data.totalCount}</span>}</h3>
      <Errors errors={[...list.errors, ...errors]} />
      {can.comment(role) && (
        <form onSubmit={add} className="inline-form">
          <textarea rows={2} maxLength={2000} placeholder="Write a comment…" aria-label="New comment" value={body} onChange={(e) => setBody(e.target.value)} />
          <button className="btn btn-primary btn-sm" disabled={!body.trim()}>Post</button>
        </form>
      )}
      <ul className="feed">
        {list.data?.items.map((c: CommentDto) => (
          <li key={c.id} className="feed-item">
            <div className="feed-meta">
              <strong>{c.authorName}</strong>
              <span className="muted small">{formatDateTime(c.createdAt)}{c.updatedAt !== c.createdAt && ' · edited'}</span>
            </div>
            {editing?.id === c.id ? (
              <div className="inline-form">
                <textarea rows={2} aria-label="Edit comment" value={editing.body} onChange={(e) => setEditing({ id: c.id, body: e.target.value })} />
                <div className="row-actions">
                  <button className="btn btn-primary btn-sm" disabled={!editing.body.trim()}
                    onClick={async () => (await run(() => commentsApi.update(projectId, taskId, c.id, editing.body.trim()))) && setEditing(null)}>Save</button>
                  <button className="btn btn-ghost btn-sm" onClick={() => setEditing(null)}>Cancel</button>
                </div>
              </div>
            ) : <p className="feed-body">{c.body}</p>}
            {can.editAuthored(role, c.authorId, user?.id) && editing?.id !== c.id && (
              <div className="row-actions">
                <button className="link-btn small" onClick={() => setEditing({ id: c.id, body: c.body })}>Edit</button>
                <button className="link-btn small danger" onClick={() => run(() => commentsApi.remove(projectId, taskId, c.id))}>Delete</button>
              </div>
            )}
          </li>
        ))}
        {list.data?.items.length === 0 && <li className="muted small">No comments yet.</li>}
      </ul>
    </section>
  );
}

function TimeLogs({ projectId, taskId, role }: { projectId: string; taskId: string; role?: ProjectRole }) {
  const { user } = useAuth();
  const list = useAsync(() => timeLogsApi.list(projectId, taskId), [projectId, taskId]);
  const [form, setForm] = useState({ minutes: '', workDate: '', note: '' });
  const [errors, setErrors] = useState<string[]>([]);
  const [editing, setEditing] = useState<{ id: string; minutes: string } | null>(null);
  const total = list.data?.items.reduce((s, l) => s + l.minutes, 0) ?? 0;
  const todayUtc = new Date().toISOString().slice(0, 10); // the API rejects future dates in UTC

  async function run(action: () => Promise<unknown>) {
    setErrors([]);
    try {
      await action();
      list.reload();
      return true;
    } catch (e) {
      setErrors(errorMessages(e));
      return false;
    }
  }

  async function add(e: FormEvent) {
    e.preventDefault();
    const minutes = Number(form.minutes);
    const ok = await run(() => timeLogsApi.create(projectId, taskId, {
      minutes, workDate: form.workDate || null, note: form.note.trim() || null,
    }));
    if (ok) setForm({ minutes: '', workDate: '', note: '' });
  }

  return (
    <section className="panel">
      <h3>Time logged {list.data && <span className="count">{formatMinutes(total)}</span>}</h3>
      <Errors errors={[...list.errors, ...errors]} />
      {can.logTime(role) && (
        <form onSubmit={add} className="timelog-form">
          <input type="number" min={1} max={1440} required placeholder="Min" aria-label="Minutes"
            value={form.minutes} onChange={(e) => setForm({ ...form, minutes: e.target.value })} />
          <input type="date" max={todayUtc} aria-label="Work date (defaults to today)"
            value={form.workDate} onChange={(e) => setForm({ ...form, workDate: e.target.value })} />
          <input placeholder="Note (optional)" maxLength={500} aria-label="Note"
            value={form.note} onChange={(e) => setForm({ ...form, note: e.target.value })} />
          <button className="btn btn-primary btn-sm">Log</button>
        </form>
      )}
      <ul className="feed">
        {list.data?.items.map((l: TimeLogDto) => (
          <li key={l.id} className="feed-item">
            <div className="feed-meta">
              <strong>{l.userName}</strong>
              <span className="muted small">{formatDate(l.workDate)}</span>
            </div>
            {editing?.id === l.id ? (
              <div className="row-actions">
                <input type="number" min={1} max={1440} aria-label="Minutes" className="input-sm"
                  value={editing.minutes} onChange={(e) => setEditing({ id: l.id, minutes: e.target.value })} />
                <button className="btn btn-primary btn-sm"
                  onClick={async () => (await run(() => timeLogsApi.update(projectId, taskId, l.id, { minutes: Number(editing.minutes) }))) && setEditing(null)}>Save</button>
                <button className="btn btn-ghost btn-sm" onClick={() => setEditing(null)}>Cancel</button>
              </div>
            ) : (
              <p className="feed-body"><span className="mono">{formatMinutes(l.minutes)}</span>{l.note && <> — {l.note}</>}</p>
            )}
            {can.editAuthored(role, l.userId, user?.id) && editing?.id !== l.id && (
              <div className="row-actions">
                <button className="link-btn small" onClick={() => setEditing({ id: l.id, minutes: String(l.minutes) })}>Edit</button>
                <button className="link-btn small danger" onClick={() => run(() => timeLogsApi.remove(projectId, taskId, l.id))}>Delete</button>
              </div>
            )}
          </li>
        ))}
        {list.data?.items.length === 0 && <li className="muted small">No time logged yet.</li>}
      </ul>
    </section>
  );
}
