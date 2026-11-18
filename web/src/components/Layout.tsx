import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';

export function Layout() {
  const { user, isAdmin, logout } = useAuth();
  const navigate = useNavigate();

  async function signOut() {
    await logout();
    navigate('/login', { replace: true });
  }

  return (
    <div className="app">
      <header className="topbar">
        <div className="topbar-inner">
          <NavLink to="/projects" className="brand">
            <span className="brand-mark" aria-hidden>✓</span> Task Tracker
          </NavLink>
          <nav className="nav">
            <NavLink to="/projects">Projects</NavLink>
            <NavLink to="/my-tasks">My tasks</NavLink>
            {isAdmin && <NavLink to="/admin/users">Users</NavLink>}
          </nav>
          <div className="user-menu">
            <span className="user-name" title={user?.email}>
              {user?.displayName}
              {isAdmin && <span className="badge role-Admin">Admin</span>}
            </span>
            <button className="btn btn-ghost btn-sm" onClick={signOut}>Sign out</button>
          </div>
        </div>
      </header>
      <main className="content">
        <Outlet />
      </main>
    </div>
  );
}
