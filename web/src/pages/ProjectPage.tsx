import { useState, type FormEvent } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { ApiError, errorMessages } from '../api/client';
import { projectsApi, tasksApi } from '../api/endpoints';
import type { MemberDto, ProjectDto, ProjectRole } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ConfirmDialog, EmptyState, Errors, Field, Modal, Pagination, RoleBadge, Spinner } from '../components/ui';
import { formatDate, useAsync } from '../lib/hooks';
import { can } from '../lib/permissions';
import { TaskDetailModal } from '../tasks/TaskDetailModal';
import { TaskFormModal } from '../tasks/TaskFormModal';
import { TaskFilters, TaskTable } from '../tasks/TaskList';
import { activeFilterCount, TASK_PAGE_SIZE, useTaskQuery } from '../tasks/taskQuery';

type Tab = 'tasks' | 'members' | 'settings';

export function ProjectPage() {
  const { projectId = '' } = useParams();
  const [params, setParams] = useSearchParams();
  const tab = (params.get('tab') as Tab | null) ?? 'tasks';
  const project = useAsync(() => projectsApi.get(projectId), [projectId]);
  const members = useAsync(() => projectsApi.members(projectId), [projectId]);

  if (project.errors.length) {
    // Non-members get 404 from the API — same screen as a project that doesn't exist.
    return (
      <EmptyState title="Project not found">
        <p>It may have been deleted, or you're not a member. <Link to="/projects">Back to projects</Link></p>
      </EmptyState>
    );
  }
  if (!project.data) return <Spinner />;

  const p = project.data;
  const setTab = (t: Tab) => setParams(t === 'tasks' ? {} : { tab: t }, { replace: true });

  return (
    <section>
      <nav className="breadcrumb"><Link to="/projects">Projects</Link> / <span>{p.name}</span></nav>
      <div className="page-header">
        <div>
          <h1>{p.name} <RoleBadge role={p.myRole} /></h1>
          {p.description && <p className="muted">{p.description}</p>}
        </div>
      </div>

      <div className="tabs" role="tablist">
        <button role="tab" aria-selected={tab === 'tasks'} className={tab === 'tasks' ? 'tab tab-on' : 'tab'} onClick={() => setTab('tasks')}>Tasks</button>
        <button role="tab" aria-selected={tab === 'members'} className={tab === 'members' ? 'tab tab-on' : 'tab'} onClick={() => setTab('members')}>
          Members {members.data && <span className="count">{members.data.length}</span>}
        </button>
        {can.editProject(p.myRole) && (
          <button role="tab" aria-selected={tab === 'settings'} className={tab === 'settings' ? 'tab tab-on' : 'tab'} onClick={() => setTab('settings')}>Settings</button>
        )}
      </div>

      {tab === 'tasks' && <TasksTab project={p} members={members.data ?? []} />}
      {tab === 'members' && (
        <MembersTab project={p} members={members.data} errors={members.errors}
          onChanged={() => { members.reload(); project.reload(); }} />
      )}
      {tab === 'settings' && can.editProject(p.myRole) && <SettingsTab project={p} onSaved={project.setData} />}
    </section>
  );
}

function TasksTab({ project, members }: { project: ProjectDto; members: MemberDto[] }) {
  const api = useTaskQuery();
  const { query, params, set } = api;
  const openTaskId = params.get('task');
  const [creating, setCreating] = useState(false);
  const tasks = useAsync(() => tasksApi.list(project.id, query), [project.id, params.toString()]);

  return (
    <>
      <div className="section-head">
        <TaskFilters api={api} members={members} />
        {can.createTask(project.myRole) && (
          <button className="btn btn-primary new-task" onClick={() => setCreating(true)}>+ New task</button>
        )}
      </div>

      <Errors errors={tasks.errors} />
      {tasks.loading && !tasks.data ? <Spinner /> : tasks.data && (tasks.data.items.length === 0 ? (
        <EmptyState title={activeFilterCount(query) ? 'No tasks found' : 'No tasks yet'}>
          {activeFilterCount(query)
            ? <button className="btn btn-ghost" onClick={api.clear}>Clear filters</button>
            : can.createTask(project.myRole) ? 'Create the first task for this project.' : 'Nothing to see here yet.'}
        </EmptyState>
      ) : (
        <div className={tasks.loading ? 'is-loading' : undefined}>
          <TaskTable tasks={tasks.data.items} onOpen={(t) => set('task', t.id)} />
          <Pagination page={query.page ?? 1} pageSize={TASK_PAGE_SIZE} total={tasks.data.totalCount}
            onPage={(n) => set('page', String(n))} />
        </div>
      ))}

      {creating && (
        <TaskFormModal projectId={project.id} members={members} onClose={() => setCreating(false)}
          onSaved={() => { setCreating(false); tasks.reload(); }} />
      )}
      {openTaskId && (
        <TaskDetailModal projectId={project.id} taskId={openTaskId} role={project.myRole} members={members}
          onChanged={tasks.reload} onClose={() => set('task', undefined)} />
      )}
    </>
  );
}

function MembersTab({ project, members, errors, onChanged }: {
  project: ProjectDto; members?: MemberDto[]; errors: string[]; onChanged: () => void;
}) {
  const { user } = useAuth();
  const manage = can.manageMembers(project.myRole);
  const [adding, setAdding] = useState(false);
  const [rowErrors, setRowErrors] = useState<string[]>([]);
  const [confirm, setConfirm] = useState<{ kind: 'remove' | 'transfer'; member: MemberDto } | null>(null);

  async function changeRole(m: MemberDto, role: Exclude<ProjectRole, 'Owner'>) {
    setRowErrors([]);
    try {
      await projectsApi.changeMemberRole(project.id, m.userId, role);
      onChanged();
    } catch (e) {
      setRowErrors(errorMessages(e));
    }
  }

  return (
    <>
      <div className="section-head">
        <p className="muted">
          <strong>Owner</strong> manages the project and deletes tasks · <strong>Member</strong> creates and edits tasks ·{' '}
          <strong>Viewer</strong> is read-only and can't be assigned.
        </p>
        {manage && <button className="btn btn-primary" onClick={() => setAdding(true)}>+ Add member</button>}
      </div>
      <Errors errors={[...errors, ...rowErrors]} />
      {!members ? <Spinner /> : (
        <div className="table-wrap">
          <table className="table">
            <thead><tr><th>Name</th><th>Email</th><th>Role</th><th>Joined</th>{manage && <th className="actions-col">Actions</th>}</tr></thead>
            <tbody>
              {members.map((m) => (
                <tr key={m.userId}>
                  <td>{m.displayName}{m.userId === user?.id && <span className="muted"> (you)</span>}</td>
                  <td className="muted">{m.email}</td>
                  <td>
                    {manage && m.role !== 'Owner' ? (
                      <select aria-label={`Role of ${m.displayName}`} value={m.role}
                        onChange={(e) => changeRole(m, e.target.value as 'Member' | 'Viewer')}>
                        <option value="Member">Member</option>
                        <option value="Viewer">Viewer</option>
                      </select>
                    ) : <RoleBadge role={m.role} />}
                  </td>
                  <td className="muted">{formatDate(m.joinedAt)}</td>
                  {manage && (
                    <td className="actions-col">
                      {m.role !== 'Owner' && (
                        <>
                          <button className="link-btn small" onClick={() => setConfirm({ kind: 'transfer', member: m })}>Make owner</button>
                          <button className="link-btn small danger" onClick={() => setConfirm({ kind: 'remove', member: m })}>Remove</button>
                        </>
                      )}
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {adding && <AddMemberModal projectId={project.id} onClose={() => setAdding(false)} onAdded={() => { setAdding(false); onChanged(); }} />}
      {confirm?.kind === 'remove' && (
        <ConfirmDialog title="Remove member?" danger confirmLabel="Remove"
          message={<>{confirm.member.displayName} loses access to this project and is unassigned from its tasks.</>}
          onConfirm={() => projectsApi.removeMember(project.id, confirm.member.userId).then(onChanged)}
          onClose={() => setConfirm(null)} />
      )}
      {confirm?.kind === 'transfer' && (
        <ConfirmDialog title="Transfer ownership?" confirmLabel="Transfer"
          message={<>{confirm.member.displayName} becomes the Owner. The current Owner becomes a Member and can no longer delete tasks or manage members.</>}
          onConfirm={() => projectsApi.transferOwnership(project.id, confirm.member.userId).then(onChanged)}
          onClose={() => setConfirm(null)} />
      )}
    </>
  );
}

function AddMemberModal({ projectId, onClose, onAdded }: { projectId: string; onClose: () => void; onAdded: () => void }) {
  const [email, setEmail] = useState('');
  const [role, setRole] = useState<'Member' | 'Viewer'>('Member');
  const [errors, setErrors] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setErrors([]);
    try {
      await projectsApi.addMember(projectId, { email: email.trim(), role });
      onAdded();
    } catch (err) {
      // 404 = no such user, 409 = already a member
      setErrors(err instanceof ApiError && err.status === 404 ? ['No user with that email has registered yet.'] : errorMessages(err));
      setBusy(false);
    }
  }

  return (
    <Modal title="Add member" onClose={onClose} footer={
      <>
        <button className="btn btn-ghost" onClick={onClose}>Cancel</button>
        <button className="btn btn-primary" form="add-member" disabled={busy || !email}>Add</button>
      </>
    }>
      <form id="add-member" onSubmit={submit} className="stack">
        <Errors errors={errors} />
        <Field label="Email of a registered user">
          <input type="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
        </Field>
        <Field label="Role" hint="Ownership can only be handed over with “Make owner”.">
          <select value={role} onChange={(e) => setRole(e.target.value as 'Member' | 'Viewer')}>
            <option value="Member">Member — can create and edit tasks</option>
            <option value="Viewer">Viewer — read-only</option>
          </select>
        </Field>
      </form>
    </Modal>
  );
}

function SettingsTab({ project, onSaved }: { project: ProjectDto; onSaved: (p: ProjectDto) => void }) {
  const navigate = useNavigate();
  const [name, setName] = useState(project.name);
  const [description, setDescription] = useState(project.description ?? '');
  const [errors, setErrors] = useState<string[]>([]);
  const [saved, setSaved] = useState(false);
  const [deleting, setDeleting] = useState(false);

  async function save(e: FormEvent) {
    e.preventDefault();
    setErrors([]);
    setSaved(false);
    try {
      onSaved(await projectsApi.update(project.id, { name: name.trim(), description: description.trim() || null }));
      setSaved(true);
    } catch (err) {
      setErrors(errorMessages(err));
    }
  }

  return (
    <div className="settings">
      <form onSubmit={save} className="card stack">
        <h3>Details</h3>
        <Errors errors={errors} />
        <Field label="Name"><input required maxLength={100} value={name} onChange={(e) => setName(e.target.value)} /></Field>
        <Field label="Description">
          <textarea rows={3} maxLength={1000} value={description} onChange={(e) => setDescription(e.target.value)} />
        </Field>
        <div className="row-actions">
          <button className="btn btn-primary" disabled={!name.trim()}>Save</button>
          {saved && <span className="success">Saved</span>}
        </div>
      </form>

      <div className="card danger-zone">
        <h3>Delete project</h3>
        <p className="muted">Removes the project and all of its tasks for every member.</p>
        <button className="btn btn-danger" onClick={() => setDeleting(true)}>Delete project</button>
      </div>

      {deleting && (
        <ConfirmDialog title="Delete project?" danger confirmLabel="Delete project"
          message={<>“{project.name}” and everything in it will disappear for all members.</>}
          onConfirm={() => projectsApi.remove(project.id).then(() => navigate('/projects', { replace: true }))}
          onClose={() => setDeleting(false)} />
      )}
    </div>
  );
}
