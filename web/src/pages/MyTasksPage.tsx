import { useNavigate } from 'react-router-dom';
import { tasksApi } from '../api/endpoints';
import { EmptyState, Errors, Pagination, Spinner } from '../components/ui';
import { useAsync } from '../lib/hooks';
import { TaskFilters, TaskTable } from '../tasks/TaskList';
import { activeFilterCount, TASK_PAGE_SIZE, useTaskQuery } from '../tasks/taskQuery';

/** Tasks assigned to me across every project I belong to (GET /api/me/tasks). */
export function MyTasksPage() {
  const navigate = useNavigate();
  const api = useTaskQuery();
  const { query, params, set } = api;
  const tasks = useAsync(() => tasksApi.mine(query), [params.toString()]);

  return (
    <section>
      <div className="page-header">
        <div>
          <h1>My tasks</h1>
          <p className="muted">Everything assigned to you, across all your projects.</p>
        </div>
      </div>

      <TaskFilters api={api} />
      <Errors errors={tasks.errors} />
      {tasks.loading && !tasks.data ? <Spinner /> : tasks.data && (tasks.data.items.length === 0 ? (
        <EmptyState title={activeFilterCount(query) ? 'No tasks found' : 'Nothing assigned to you'}>
          {activeFilterCount(query) ? <button className="btn btn-ghost" onClick={api.clear}>Clear filters</button> : 'Enjoy the quiet.'}
        </EmptyState>
      ) : (
        <div className={tasks.loading ? 'is-loading' : undefined}>
          {/* Open the task inside its project, where your role and the member list are known. */}
          <TaskTable showProject tasks={tasks.data.items} onOpen={(t) => navigate(`/projects/${t.projectId}?task=${t.id}`)} />
          <Pagination page={query.page ?? 1} pageSize={TASK_PAGE_SIZE} total={tasks.data.totalCount}
            onPage={(n) => set('page', String(n))} />
        </div>
      ))}
    </section>
  );
}
