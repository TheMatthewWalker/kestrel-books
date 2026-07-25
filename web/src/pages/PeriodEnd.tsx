import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, errorMessage, gbp, today } from '../api';
import { Chips, Field, useLoad } from '../components';

const KINDS: [number, string][] = [
  [0, 'Accrual'], [1, 'Prepayment'], [2, 'Accrued income'], [3, 'Deferred income'],
];
const EXPLAIN: Record<number, string> = {
  0: 'Cost incurred but not yet invoiced. Posted at the period end and reversed automatically on the first of the next month, so the real invoice lands cleanly.',
  1: 'Cost paid in advance. Held on the balance sheet and released to the P&L in equal monthly instalments.',
  2: 'Income earned but not yet invoiced. Posted now and reversed next period.',
  3: 'Income received in advance. Held as a liability and released to the P&L as it is earned.',
};
const STATUS = ['Active', 'Completed', 'Cancelled'];

export default function PeriodEnd() {
  const { businessId } = useParams();
  const [items, reload] = useLoad<any[]>(`/businesses/${businessId}/period-end`);
  const [accounts] = useLoad<any[]>(`/businesses/${businessId}/accounts`);
  const [showForm, setShowForm] = useState(false);
  const [f, setF] = useState({
    kind: 0, description: '', totalAmount: '', pandLAccountId: '',
    balanceSheetAccountId: '', startDate: today(), periods: '12',
  });
  const [err, setErr] = useState('');

  const spread = f.kind === 1 || f.kind === 3;
  const income = f.kind === 2 || f.kind === 3;
  const pandL = (accounts ?? []).filter((a: any) => a.type === (income ? 3 : 4));
  const balanceSheet = (accounts ?? []).filter((a: any) => a.type === 0 || a.type === 1);

  const create = async () => {
    setErr('');
    try {
      await api.post(`/businesses/${businessId}/period-end`, {
        kind: f.kind, description: f.description, totalAmount: parseFloat(f.totalAmount) || 0,
        pandLAccountId: f.pandLAccountId, balanceSheetAccountId: f.balanceSheetAccountId,
        startDate: f.startDate, periods: spread ? (parseInt(f.periods) || 1) : 1,
      });
      setShowForm(false); setF({ ...f, description: '', totalAmount: '' });
      reload();
    } catch (e) { setErr(errorMessage(e)); }
  };

  const act = async (fn: () => Promise<unknown>) => {
    try { await fn(); reload(); } catch (e) { alert(errorMessage(e)); }
  };

  const monthly = spread && f.totalAmount && parseInt(f.periods)
    ? (parseFloat(f.totalAmount) || 0) / (parseInt(f.periods) || 1) : 0;

  return (
    <div>
      <div className="row"><h1>Accruals &amp; prepayments</h1><div className="spacer" />
        <button className="btn ghost" onClick={() => act(async () => {
          const r = await api.post(`/businesses/${businessId}/period-end/run-all`);
          alert(`${r.data.schedules} schedule(s) run, ${r.data.posted} journal(s) posted.`);
        })}>Run everything due</button>
        <button className="btn" onClick={() => setShowForm(s => !s)}>{showForm ? 'Close' : 'New adjustment'}</button>
      </div>
      <div className="sub">
        Put costs and income in the period they belong to, not the period the paperwork arrived in.
      </div>

      {showForm && (
        <div className="card" style={{ marginBottom: 18, maxWidth: 780 }}>
          <label>Type</label>
          <Chips options={KINDS} value={f.kind} onChange={v => setF({ ...f, kind: v, pandLAccountId: '' })} />
          <div className="sub" style={{ marginTop: 8 }}>{EXPLAIN[f.kind]}</div>
          <Field label="Description">
            <input value={f.description} onChange={e => setF({ ...f, description: e.target.value })}
              placeholder="Electricity for June, invoice not yet received" /></Field>
          <div className="row">
            <Field label="Total amount">
              <input value={f.totalAmount} onChange={e => setF({ ...f, totalAmount: e.target.value })} /></Field>
            <Field label={spread ? 'First release date' : 'Period end date'}>
              <input type="date" value={f.startDate}
                onChange={e => setF({ ...f, startDate: e.target.value })} /></Field>
            {spread && (
              <Field label="Spread over (months)">
                <input value={f.periods} onChange={e => setF({ ...f, periods: e.target.value })} /></Field>
            )}
          </div>
          {spread && monthly > 0 &&
            <div className="sub">{gbp(monthly)} per month for {f.periods} months.</div>}
          <div className="row">
            <Field label={income ? 'Income account' : 'Expense account'}>
              <select value={f.pandLAccountId} onChange={e => setF({ ...f, pandLAccountId: e.target.value })}>
                <option value="">— choose —</option>
                {pandL.map((a: any) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}
              </select></Field>
            <Field label="Balance sheet account">
              <select value={f.balanceSheetAccountId}
                onChange={e => setF({ ...f, balanceSheetAccountId: e.target.value })}>
                <option value="">— accruals / prepayments —</option>
                {balanceSheet.map((a: any) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}
              </select></Field>
          </div>
          {err && <div className="err">{err}</div>}
          <button className="btn" style={{ marginTop: 12 }}
            disabled={!f.description || !f.totalAmount || !f.pandLAccountId || !f.balanceSheetAccountId}
            onClick={create}>Create schedule</button>
        </div>
      )}

      <table>
        <thead><tr><th>Type</th><th>Description</th><th className="num">Amount</th>
          <th className="num">Released</th><th className="num">Remaining</th>
          <th>Progress</th><th>Next</th><th>Status</th><th /></tr></thead>
        <tbody>
          {(items ?? []).map((s: any) => (
            <tr key={s.id}>
              <td>{KINDS.find(([v]) => v === s.kind)?.[1] ?? s.kind}</td>
              <td>{s.description}</td>
              <td className="num">{gbp(s.totalAmount)}</td>
              <td className="num">{gbp(s.released)}</td>
              <td className="num">{gbp(s.remaining)}</td>
              <td>{s.isSpread ? `${s.periodsReleased}/${s.periods}` : (s.periodsReleased ? 'posted' : '—')}</td>
              <td>{s.nextRunDate ?? '—'}</td>
              <td><span className={`badge${s.status === 1 ? ' posted' : ''}`}>{STATUS[s.status] ?? s.status}</span></td>
              <td>
                {s.status === 0 && (
                  <div className="row" style={{ gap: 6 }}>
                    <button className="btn ghost" onClick={() => act(async () => {
                      const r = await api.post(`/businesses/${businessId}/period-end/${s.id}/run`);
                      alert(r.data.posted > 0 ? `${r.data.posted} journal(s) posted.` : 'Nothing due yet.');
                    })}>Run</button>
                    <button className="btn ghost" onClick={() => act(() =>
                      api.post(`/businesses/${businessId}/period-end/${s.id}/cancel`))}>Cancel</button>
                  </div>
                )}
              </td>
            </tr>
          ))}
          {(items ?? []).length === 0 &&
            <tr><td colSpan={9} className="sub">No adjustments yet.</td></tr>}
        </tbody>
      </table>
    </div>
  );
}
