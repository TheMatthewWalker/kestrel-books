import { useEffect, useState, type ReactNode } from 'react';
import { api } from './api';

/** Fetch-on-mount hook with manual reload. */
export function useLoad<T>(url: string, deps: unknown[] = []): [T | null, () => void] {
  const [data, setData] = useState<T | null>(null);
  const [tick, setTick] = useState(0);
  useEffect(() => {
    let live = true;
    api.get(url).then(r => { if (live) setData(r.data); }).catch(() => {});
    return () => { live = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [url, tick, ...deps]);
  return [data, () => setTick(t => t + 1)];
}

export function Field({ label, children }: { label: string; children: ReactNode }) {
  return <div><label>{label}</label>{children}</div>;
}

export function Chips<T extends string | number>({ options, value, onChange }:
  { options: [T, string][]; value: T; onChange: (v: T) => void }) {
  return (
    <div className="row" style={{ flexWrap: 'wrap', gap: 6, marginTop: 4 }}>
      {options.map(([v, label]) => (
        <button key={String(v)} type="button"
          className={`btn${value === v ? '' : ' ghost'}`}
          style={{ padding: '5px 12px', fontSize: 13 }}
          onClick={() => onChange(v)}>{label}</button>
      ))}
    </div>
  );
}

export const VAT_OPTIONS: [number, string][] = [[0, '20%'], [1, '5%'], [2, 'Zero'], [3, 'Exempt']];
export const vatPct = (r: number) => (r === 0 ? 0.2 : r === 1 ? 0.05 : 0);
