import { useCallback, useEffect, useRef, useState } from 'react';
import { errorMessages } from '../api/client';

export interface AsyncState<T> {
  data: T | undefined;
  loading: boolean;
  errors: string[];
  reload: () => void;
  setData: (d: T) => void;
}

/**
 * Loads data whenever `deps` change; ignores responses from superseded requests
 * (so fast typing in a search box can't show stale results).
 */
export function useAsync<T>(load: () => Promise<T>, deps: unknown[]): AsyncState<T> {
  const [data, setData] = useState<T>();
  const [loading, setLoading] = useState(true);
  const [errors, setErrors] = useState<string[]>([]);
  const [tick, setTick] = useState(0);
  const seq = useRef(0);

  useEffect(() => {
    const id = ++seq.current;
    setLoading(true);
    load()
      .then((d) => { if (id === seq.current) { setData(d); setErrors([]); } })
      .catch((e) => { if (id === seq.current) setErrors(errorMessages(e)); })
      .finally(() => { if (id === seq.current) setLoading(false); });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, tick]);

  const reload = useCallback(() => setTick((t) => t + 1), []);
  return { data, loading, errors, reload, setData };
}

/** Value that only updates after `ms` of no changes — for search-as-you-type. */
export function useDebounced<T>(value: T, ms = 300): T {
  const [v, setV] = useState(value);
  useEffect(() => {
    const h = setTimeout(() => setV(value), ms);
    return () => clearTimeout(h);
  }, [value, ms]);
  return v;
}

export function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—';
  // Date-only values (yyyy-MM-dd) must not be shifted by the local time zone.
  const d = /^\d{4}-\d{2}-\d{2}$/.test(iso) ? new Date(`${iso}T00:00:00`) : new Date(iso);
  return d.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}

export function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
}

export function formatMinutes(min: number): string {
  const h = Math.floor(min / 60);
  const m = min % 60;
  return h ? (m ? `${h}h ${m}m` : `${h}h`) : `${m}m`;
}

export function todayIso(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

export function isOverdue(due: string | null, status: string): boolean {
  return !!due && due < todayIso() && status !== 'Done' && status !== 'Cancelled';
}
