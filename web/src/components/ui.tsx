import { useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { STATUS_LABEL, type ProjectRole, type TaskPriority, type TaskStatus } from '../api/types';
import { errorMessages } from '../api/client';

export function Errors({ errors }: { errors: string[] }) {
  if (!errors.length) return null;
  return (
    <div className="alert" role="alert">
      {errors.length === 1 ? errors[0] : <ul>{errors.map((e) => <li key={e}>{e}</li>)}</ul>}
    </div>
  );
}

export function Spinner({ label = 'Loading…' }: { label?: string }) {
  return <div className="spinner" role="status" aria-live="polite"><span className="spinner-dot" />{label}</div>;
}

export function EmptyState({ title, children }: { title: string; children?: ReactNode }) {
  return (
    <div className="empty">
      <p className="empty-title">{title}</p>
      {children && <div className="empty-body">{children}</div>}
    </div>
  );
}

export function StatusBadge({ status }: { status: TaskStatus }) {
  return <span className={`badge status-${status}`}>{STATUS_LABEL[status]}</span>;
}

export function PriorityBadge({ priority }: { priority: TaskPriority }) {
  return <span className={`priority priority-${priority}`}><span className="priority-dot" aria-hidden />{priority}</span>;
}

export function RoleBadge({ role }: { role: ProjectRole | 'Admin' | 'User' }) {
  return <span className={`badge role-${role}`}>{role}</span>;
}

export function Pagination({ page, pageSize, total, onPage }: {
  page: number; pageSize: number; total: number; onPage: (p: number) => void;
}) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  if (total <= pageSize) return null;
  return (
    <nav className="pagination" aria-label="Pagination">
      <button className="btn btn-ghost" disabled={page <= 1} onClick={() => onPage(page - 1)}>← Prev</button>
      <span>Page {page} of {pages} · {total} total</span>
      <button className="btn btn-ghost" disabled={page >= pages} onClick={() => onPage(page + 1)}>Next →</button>
    </nav>
  );
}

export function Modal({ title, onClose, children, footer, wide }: {
  title: string; onClose: () => void; children: ReactNode; footer?: ReactNode; wide?: boolean;
}) {
  const titleId = useId();
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose();
    document.addEventListener('keydown', onKey);
    ref.current?.querySelector<HTMLElement>('input, textarea, select, button')?.focus();
    return () => document.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <div className="backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div ref={ref} className={`modal${wide ? ' modal-wide' : ''}`} role="dialog" aria-modal="true" aria-labelledby={titleId}>
        <header className="modal-header">
          <h2 id={titleId}>{title}</h2>
          <button className="icon-btn" aria-label="Close" onClick={onClose}>×</button>
        </header>
        <div className="modal-body">{children}</div>
        {footer && <footer className="modal-footer">{footer}</footer>}
      </div>
    </div>
  );
}

/** "Are you sure?" dialog that runs `onConfirm` and shows API errors (e.g. 409) inline. */
export function ConfirmDialog({ title, message, confirmLabel = 'Confirm', danger, onConfirm, onClose }: {
  title: string; message: ReactNode; confirmLabel?: string; danger?: boolean;
  onConfirm: () => Promise<unknown>; onClose: () => void;
}) {
  const [busy, setBusy] = useState(false);
  const [errors, setErrors] = useState<string[]>([]);

  async function confirm() {
    setBusy(true);
    setErrors([]);
    try {
      await onConfirm();
      onClose();
    } catch (e) {
      setErrors(errorMessages(e));
      setBusy(false);
    }
  }

  return (
    <Modal title={title} onClose={onClose} footer={
      <>
        <button className="btn btn-ghost" onClick={onClose} disabled={busy}>Cancel</button>
        <button className={`btn ${danger ? 'btn-danger' : 'btn-primary'}`} onClick={confirm} disabled={busy}>
          {busy ? 'Working…' : confirmLabel}
        </button>
      </>
    }>
      <div className="confirm-message">{message}</div>
      <Errors errors={errors} />
    </Modal>
  );
}

export function Field({ label, children, hint }: { label: string; children: ReactNode; hint?: string }) {
  return (
    <label className="field">
      <span className="field-label">{label}</span>
      {children}
      {hint && <span className="field-hint">{hint}</span>}
    </label>
  );
}
