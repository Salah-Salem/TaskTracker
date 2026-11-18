import { useState } from 'react';
import { errorMessages } from '../api/client';
import { usersApi } from '../api/endpoints';
import type { GlobalRole, UserDto } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ConfirmDialog, Errors, Pagination, RoleBadge, Spinner } from '../components/ui';
import { useAsync } from '../lib/hooks';

const PAGE_SIZE = 20;

/** US-B2: Admin-only user management. The API rejects non-admins with 403 regardless of this screen. */
export function AdminUsersPage() {
  const { user: me } = useAuth();
  const [page, setPage] = useState(1);
  const users = useAsync(() => usersApi.list(page, PAGE_SIZE), [page]);
  const [errors, setErrors] = useState<string[]>([]);
  const [confirm, setConfirm] = useState<UserDto | null>(null);

  async function act(action: () => Promise<unknown>) {
    setErrors([]);
    try {
      await action();
      users.reload();
    } catch (e) {
      setErrors(errorMessages(e)); // e.g. "At least one active Admin is required"
    }
  }

  return (
    <section>
      <div className="page-header">
        <div>
          <h1>Users</h1>
          <p className="muted">
            Global roles only. Admins can manage users and act as Owner on every project.
            Changes take effect on the user's next request.
          </p>
        </div>
      </div>

      <Errors errors={[...users.errors, ...errors]} />
      {!users.data ? <Spinner /> : (
        <>
          <div className={`table-wrap${users.loading ? ' is-loading' : ''}`}>
            <table className="table">
              <thead><tr><th>Name</th><th>Email</th><th>Role</th><th>Status</th><th className="actions-col">Actions</th></tr></thead>
              <tbody>
                {users.data.items.map((u) => (
                  <tr key={u.id} className={u.isActive ? undefined : 'row-muted'}>
                    <td>{u.displayName}{u.id === me?.id && <span className="muted"> (you)</span>}</td>
                    <td className="muted">{u.email}</td>
                    <td>
                      <select aria-label={`Role of ${u.displayName}`} value={u.roles[0] ?? 'User'}
                        onChange={(e) => act(() => usersApi.changeRole(u.id, e.target.value as GlobalRole))}>
                        <option value="User">User</option>
                        <option value="Admin">Admin</option>
                      </select>
                    </td>
                    <td>{u.isActive ? <span className="badge status-Done">Active</span> : <span className="badge status-Cancelled">Deactivated</span>}</td>
                    <td className="actions-col">
                      {u.isActive
                        ? <button className="link-btn small danger" onClick={() => setConfirm(u)}>Deactivate</button>
                        : <button className="link-btn small" onClick={() => act(() => usersApi.activate(u.id))}>Activate</button>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pagination page={page} pageSize={PAGE_SIZE} total={users.data.totalCount} onPage={setPage} />
          <p className="muted small legend">Role legend: <RoleBadge role="Admin" /> <RoleBadge role="User" /></p>
        </>
      )}

      {confirm && (
        <ConfirmDialog title={`Deactivate ${confirm.displayName}?`} danger confirmLabel="Deactivate"
          message="They are signed out everywhere immediately and can't sign in until reactivated."
          onConfirm={() => usersApi.deactivate(confirm.id).then(users.reload)}
          onClose={() => setConfirm(null)} />
      )}
    </section>
  );
}
