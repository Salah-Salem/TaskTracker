import { useState, type FormEvent } from 'react';
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom';
import { errorMessages } from '../api/client';
import { Errors, Field } from '../components/ui';
import { useAuth } from './AuthContext';

/** Route guard: anonymous users go to /login and come back afterwards (US-A3/A4). */
export function RequireAuth({ children }: { children: JSX.Element }) {
  const { user } = useAuth();
  const location = useLocation();
  if (!user) return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />;
  return children;
}

export function RequireAdmin({ children }: { children: JSX.Element }) {
  const { isAdmin } = useAuth();
  return isAdmin ? children : <Navigate to="/projects" replace />;
}

const DEMO_ACCOUNTS = [
  ['alice@example.com', 'Owner'],
  ['bob@example.com', 'Member'],
  ['carol@example.com', 'Viewer'],
  ['dave@example.com', 'not a member'],
  ['admin@example.com', 'Admin'],
] as const;

export function LoginPage() {
  const { user, login } = useAuth();
  const navigate = useNavigate();
  const from = (useLocation().state as { from?: string } | null)?.from ?? '/projects';
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);

  if (user) return <Navigate to={from} replace />;

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setErrors([]);
    try {
      await login(email, password);
      navigate(from, { replace: true });
    } catch (err) {
      setErrors(errorMessages(err));
      setBusy(false);
    }
  }

  return (
    <AuthCard title="Sign in" subtitle="Welcome back to Task Tracker">
      <form onSubmit={submit} className="stack">
        <Errors errors={errors} />
        <Field label="Email">
          <input type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
        </Field>
        <Field label="Password">
          <input type="password" autoComplete="current-password" required value={password} onChange={(e) => setPassword(e.target.value)} />
        </Field>
        <button className="btn btn-primary btn-block" disabled={busy}>{busy ? 'Signing in…' : 'Sign in'}</button>
      </form>
      <p className="auth-switch">No account? <Link to="/register">Create one</Link></p>
      {import.meta.env.DEV && (
        <details className="demo">
          <summary>Demo accounts (password <code>Passw0rd!</code>)</summary>
          <ul>
            {DEMO_ACCOUNTS.map(([addr, role]) => (
              <li key={addr}>
                <button type="button" className="link-btn" onClick={() => { setEmail(addr); setPassword('Passw0rd!'); }}>
                  {addr}
                </button>{' '}
                <span className="muted">— {role}</span>
              </li>
            ))}
          </ul>
        </details>
      )}
    </AuthCard>
  );
}

export function RegisterPage() {
  const { user, register } = useAuth();
  const navigate = useNavigate();
  const [form, setForm] = useState({ displayName: '', email: '', password: '' });
  const [errors, setErrors] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);

  if (user) return <Navigate to="/projects" replace />;

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (form.password.length < 8) return setErrors(['Password must be at least 8 characters.']);
    setBusy(true);
    setErrors([]);
    try {
      await register(form.email, form.password, form.displayName);
      navigate('/projects', { replace: true });
    } catch (err) {
      setErrors(errorMessages(err));
      setBusy(false);
    }
  }

  const set = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, [k]: e.target.value });

  return (
    <AuthCard title="Create account" subtitle="You can create projects and be invited to others">
      <form onSubmit={submit} className="stack">
        <Errors errors={errors} />
        <Field label="Name">
          <input required maxLength={100} autoComplete="name" value={form.displayName} onChange={set('displayName')} />
        </Field>
        <Field label="Email">
          <input type="email" required autoComplete="email" value={form.email} onChange={set('email')} />
        </Field>
        <Field label="Password" hint="At least 8 characters">
          <input type="password" required minLength={8} autoComplete="new-password" value={form.password} onChange={set('password')} />
        </Field>
        <button className="btn btn-primary btn-block" disabled={busy}>{busy ? 'Creating…' : 'Create account'}</button>
      </form>
      <p className="auth-switch">Already registered? <Link to="/login">Sign in</Link></p>
    </AuthCard>
  );
}

function AuthCard({ title, subtitle, children }: { title: string; subtitle: string; children: React.ReactNode }) {
  return (
    <div className="auth-page">
      <div className="auth-card">
        <div className="brand brand-lg"><span className="brand-mark" aria-hidden>✓</span> Task Tracker</div>
        <h1>{title}</h1>
        <p className="muted">{subtitle}</p>
        {children}
      </div>
    </div>
  );
}
