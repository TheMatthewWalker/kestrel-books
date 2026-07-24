import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, errorMessage, today } from '../api';
import { Field, useLoad } from '../components';

export default function Periods() {
  const { businessId } = useParams();
  const [status, reload] = useLoad<any>(`/businesses/${businessId}/periods/status`);
  const [lockThrough, setLockThrough] = useState('');
  const [yearEnd, setYearEnd] = useState('');
  const [err, setErr] = useState('');

  const lock = async (through: string | null) => {
    setErr('');
    try {
      await api.put(`/businesses/${businessId}/periods/lock`, { through });
      reload();
    } catch (e) { setErr(errorMessage(e)); }
  };

  const closeYear = async () => {
    if (!confirm(`Close the year ending ${yearEnd}? This posts the year-end journal and locks the period.`)) return;
    setErr('');
    try {
      const r = await api.post(`/businesses/${businessId}/periods/close-year`, { yearEnd });
      alert(`Year closed. Journal ${r.data.journalNumber ?? ''} posted; retained earnings updated.`);
      reload();
    } catch (e) { setErr(errorMessage(e)); }
  };

  return (
    <div>
      <h1>Periods</h1>
      <div className="sub">
        Locking fences off finalised months so nothing shifts under a filed return.
        Closing the year sweeps income and expenses into retained earnings.
      </div>

      <div className="cards">
        <div className="card">
          <div className="label">Locked through</div>
          <div className="value" style={{ fontSize: 18 }}>{status?.lockedThrough ?? 'Not locked'}</div>
        </div>
        <div className="card">
          <div className="label">Year starts</div>
          <div className="value" style={{ fontSize: 18 }}>
            {status?.yearStartMonth ? new Date(2000, status.yearStartMonth - 1).toLocaleString('en-GB', { month: 'long' }) : '—'}
          </div>
        </div>
      </div>

      <h2>Lock periods</h2>
      <div className="row" style={{ alignItems: 'flex-end', maxWidth: 620 }}>
        <Field label="Lock everything up to and including">
          <input type="date" value={lockThrough} onChange={e => setLockThrough(e.target.value)} /></Field>
        <button className="btn" disabled={!lockThrough} onClick={() => lock(lockThrough)}>Lock</button>
        <button className="btn ghost" onClick={() => lock(null)}>Unlock all</button>
      </div>

      <h2>Close a financial year</h2>
      <div className="row" style={{ alignItems: 'flex-end', maxWidth: 620 }}>
        <Field label="Year end date">
          <input type="date" value={yearEnd} onChange={e => setYearEnd(e.target.value)} /></Field>
        <button className="btn" disabled={!yearEnd} onClick={closeYear}>Close year</button>
      </div>
      <div className="sub" style={{ marginTop: 8 }}>
        Closing posts one journal dated {yearEnd || 'the year end'} zeroing every income and expense account
        into retained earnings, then locks the year. It can only be done once per year end.
      </div>
      {err && <div className="err">{err}</div>}
      <div className="sub" style={{ marginTop: 20 }}>Today is {today()}.</div>
    </div>
  );
}
