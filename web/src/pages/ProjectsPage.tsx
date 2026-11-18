import { useState, type FormEvent } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { errorMessages } from '../api/client';
import { projectsApi } from '../api/endpoints';
import type { ProjectRole, ProjectSortBy, SortDir } from '../api/types';
import { EmptyState, Errors, Field, Modal, Pagination, RoleBadge, Spinner } from '../components/ui';
import { formatDate, useAsync, useDebounced } from '../lib/hooks';

const PAGE_SIZE = 12;

export function ProjectsPage() {
  const [params, setParams] = useSearchParams();
  const search = params.get('search') ?? '';
  const role = (params.get('role') as ProjectRole | null) ?? undefined;
  const sort = params.get('sort') ?? 'createdAt:desc';
  const page = Number(params.get('page') ?? 1);
  const [sortBy, sortDir] = sort.split(':') as [ProjectSortBy, SortDir];
  const debounced = useDebounced(search);
  const [creating, setCreating] = useState(false);

  const { data, loading, errors } = useAsync(
    () => projectsApi.list({ search: debounced, role, sortBy, sortDir, page, pageSize: PAGE_SIZE }),
    [debounced, role, sortBy, sortDir, page],
  );

  function update(next: Record<string, string | undefined>) {
    const p = new URLSearchParams(params);
    for (const [k, v] of Object.entries(next)) v ? p.set(k, v) : p.delete(k);
    if (!('page' in next)) p.delete('page'); // any filter change goes back to page 1
    setParams(p, { replace: true });
  }

  return (
    <section>
      <div className="page-header">
        <div>
          <h1>Projects</h1>
          <p className="muted">Projects you belong to. Anyone can create one and becomes its Owner.</p>
        </div>
        <button className="btn btn-primary" onClick={() => setCreating(true)}>+ New project</button>
      </div>

      <div className="toolbar">
        <input className="search" type="search" placeholder="Search name or description…" aria-label="Search projects"
          value={search} onChange={(e) => update({ search: e.target.value })} />
        <select aria-label="My role" value={role ?? ''} onChange={(e) => update({ role: e.target.value || undefined })}>
          <option value="">Any role</option>
          <option value="Owner">Owner</option>
          <option value="Member">Member</option>
          <option value="Viewer">Viewer</option>
        </select>
        <select aria-label="Sort" value={sort} onChange={(e) => update({ sort: e.target.value })}>
          <option value="createdAt:desc">Newest first</option>
          <option value="createdAt:asc">Oldest first</option>
          <option value="updatedAt:desc">Recently updated</option>
          <option value="name:asc">Name A–Z</option>
          <option value="name:desc">Name Z–A</option>
        </select>
      </div>

      <Errors errors={errors} />
      {loading && !data ? <Spinner /> : data && data.items.length === 0 ? (
        <EmptyState title={search || role ? 'No projects match your filters' : 'No projects yet'}>
          {search || role
            ? <button className="btn btn-ghost" onClick={() => setParams({})}>Clear filters</button>
            : 'Create a project, or ask an Owner to add you to theirs.'}
        </EmptyState>
      ) : data && (
        <>
          <div className={`card-grid${loading ? ' is-loading' : ''}`}>
            {data.items.map((p) => (
              <Link key={p.id} to={`/projects/${p.id}`} className="card project-card">
                <div className="project-card-head">
                  <h3>{p.name}</h3>
                  <RoleBadge role={p.myRole} />
                </div>
                <p className="project-desc">{p.description || <span className="muted">No description</span>}</p>
                <p className="muted small">Updated {formatDate(p.updatedAt)}</p>
              </Link>
            ))}
          </div>
          <Pagination page={page} pageSize={PAGE_SIZE} total={data.totalCount} onPage={(n) => update({ page: String(n) })} />
        </>
      )}

      {creating && <CreateProjectModal onClose={() => setCreating(false)} />}
    </section>
  );
}

function CreateProjectModal({ onClose }: { onClose: () => void }) {
  const navigate = useNavigate();
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    try {
      const p = await projectsApi.create({ name: name.trim(), description: description.trim() || null });
      navigate(`/projects/${p.id}`);
    } catch (err) {
      setErrors(errorMessages(err));
      setBusy(false);
    }
  }

  return (
    <Modal title="New project" onClose={onClose} footer={
      <>
        <button className="btn btn-ghost" onClick={onClose}>Cancel</button>
        <button className="btn btn-primary" form="create-project" disabled={busy || !name.trim()}>Create</button>
      </>
    }>
      <form id="create-project" onSubmit={submit} className="stack">
        <Errors errors={errors} />
        <Field label="Name"><input required maxLength={100} value={name} onChange={(e) => setName(e.target.value)} /></Field>
        <Field label="Description (optional)">
          <textarea rows={3} maxLength={1000} value={description} onChange={(e) => setDescription(e.target.value)} />
        </Field>
      </form>
    </Modal>
  );
}
