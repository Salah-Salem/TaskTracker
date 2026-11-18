import { useState, type FormEvent } from 'react';
import { errorMessages } from '../api/client';
import { tasksApi } from '../api/endpoints';
import {
  STATUS_LABEL, TASK_PRIORITIES, TASK_STATUSES,
  type MemberDto, type TaskDto, type TaskPatch, type TaskPriority, type TaskStatus,
} from '../api/types';
import { Errors, Field, Modal } from '../components/ui';
import { can } from '../lib/permissions';

interface FormState {
  title: string;
  description: string;
  status: TaskStatus;
  priority: TaskPriority;
  assigneeId: string;
  dueDate: string;
}

const fromTask = (t?: TaskDto): FormState => ({
  title: t?.title ?? '',
  description: t?.description ?? '',
  status: t?.status ?? 'Todo',
  priority: t?.priority ?? 'Medium',
  assigneeId: t?.assigneeId ?? '',
  dueDate: t?.dueDate ?? '',
});

/** Only the fields that changed; cleared optional fields become null (PATCH semantics). */
export function diffPatch(before: FormState, after: FormState): TaskPatch {
  const patch: TaskPatch = {};
  if (after.title.trim() !== before.title) patch.title = after.title.trim();
  if (after.description.trim() !== before.description) patch.description = after.description.trim() || null;
  if (after.status !== before.status) patch.status = after.status;
  if (after.priority !== before.priority) patch.priority = after.priority;
  if (after.assigneeId !== before.assigneeId) patch.assigneeId = after.assigneeId || null;
  if (after.dueDate !== before.dueDate) patch.dueDate = after.dueDate || null;
  return patch;
}

/** Create (no `task`) or edit (with `task`). Assignee list = project Owners/Members only. */
export function TaskFormModal({ projectId, task, members, onSaved, onClose }: {
  projectId: string; task?: TaskDto; members: MemberDto[];
  onSaved: (t: TaskDto) => void; onClose: () => void;
}) {
  const initial = fromTask(task);
  const [form, setForm] = useState<FormState>(initial);
  const [errors, setErrors] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const assignable = members.filter((m) => can.beAssigned(m.role));

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!form.title.trim()) return setErrors(['Title is required.']);
    setBusy(true);
    setErrors([]);
    try {
      const saved = task
        ? await tasksApi.update(projectId, task.id, diffPatch(initial, form))
        : await tasksApi.create(projectId, {
            title: form.title.trim(),
            description: form.description.trim() || null,
            priority: form.priority,
            assigneeId: form.assigneeId || null,
            dueDate: form.dueDate || null,
          });
      onSaved(saved);
    } catch (err) {
      setErrors(errorMessages(err)); // e.g. 422 "Assignee must be a project Owner or Member"
      setBusy(false);
    }
  }

  const set = <K extends keyof FormState>(k: K) =>
    (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>) =>
      setForm({ ...form, [k]: e.target.value as FormState[K] });

  return (
    <Modal title={task ? 'Edit task' : 'New task'} onClose={onClose} footer={
      <>
        <button className="btn btn-ghost" onClick={onClose}>Cancel</button>
        <button className="btn btn-primary" form="task-form" disabled={busy}>{busy ? 'Saving…' : task ? 'Save changes' : 'Create task'}</button>
      </>
    }>
      <form id="task-form" onSubmit={submit} className="stack">
        <Errors errors={errors} />
        <Field label="Title">
          <input required maxLength={200} value={form.title} onChange={set('title')} />
        </Field>
        <Field label="Description">
          <textarea rows={4} maxLength={4000} value={form.description} onChange={set('description')} />
        </Field>
        <div className="grid-2">
          {task && (
            <Field label="Status">
              <select value={form.status} onChange={set('status')}>
                {TASK_STATUSES.map((s) => <option key={s} value={s}>{STATUS_LABEL[s]}</option>)}
              </select>
            </Field>
          )}
          <Field label="Priority">
            <select value={form.priority} onChange={set('priority')}>
              {TASK_PRIORITIES.map((p) => <option key={p}>{p}</option>)}
            </select>
          </Field>
          <Field label="Assignee" hint="Viewers can't be assigned">
            <select value={form.assigneeId} onChange={set('assigneeId')}>
              <option value="">Unassigned</option>
              {assignable.map((m) => <option key={m.userId} value={m.userId}>{m.displayName} ({m.role})</option>)}
            </select>
          </Field>
          <Field label="Due date">
            <input type="date" value={form.dueDate} onChange={set('dueDate')} />
          </Field>
        </div>
      </form>
    </Modal>
  );
}
