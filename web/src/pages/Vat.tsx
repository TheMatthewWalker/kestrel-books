import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, errorMessage, gbp, today } from '../api';
import { Chips, Field, useLoad } from '../components';

const BOXES: [string, string][] = [
  ['vatDueSales', 'Box 1 — VAT due on sales'],
  ['vatDueAcquisitions', 'Box 2 — VAT due on acquisitions'],
  ['totalVatDue', 'Box 3 — Total VAT due'],
  ['vatReclaimedCurrPeriod', 'Box 4 — VAT reclaimed on purchases'],
  ['netVatDue', 'Box 5 — Net VAT due'],
  ['totalValueSalesExVAT', 'Box 6 — Total sales ex VAT'],
  ['totalValuePurchasesExVAT', 'Box 7 — Total purchases ex VAT'],
  ['totalValueGoodsSuppliedExVAT', 'Box 8 — Goods supplied ex VAT'],
  ['totalAcquisitionsExVAT', 'Box 9 — Acquisitions ex VAT'],
];

export default function Vat() {
  const { businessId } = useParams();
  const [status, reloadStatus] = useLoad<any>(`/mtd/businesses/${businessId}/status`);
  const [scheme, reloadScheme] = useLoad<any>(`/mtd/businesses/${businessId}/vat-scheme`);
  const [submissions, reloadSubs] = useLoad<any[]>(`/mtd/businesses/${businessId}/vat/submissions`);
  const [from, setFrom] = useState(`${new Date().getFullYear()}-01-01`);
  const [to, setTo] = useState(today());
  const [periodKey, setPeriodKey] = useState('');
  const [boxes, setBoxes] = useState<Record<string, number> | null>(null);
  const [obligations, setObligations] = useState<any[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState('');

  const preview = async () => {
    setErr(''); setBusy(true);
    try {
      const r = await api.get(`/mtd/businesses/${businessId}/vat/preview`, { params: { from, to } });
      setBoxes(r.data);
    } catch (e) { setErr(errorMessage(e)); }
    finally { setBusy(false); }
  };

  const loadObligations = async () => {
    setErr('');
    try {
      const r = await api.get(`/mtd/businesses/${businessId}/vat/obligations`,
        { params: { from, to } });
      setObligations(r.data);
    } catch (e) { setErr(errorMessage(e)); }
  };

  const submit = async () => {
    if (!boxes) return;
    if (!confirm('Submit this return to HMRC? This is a legal declaration and cannot be undone.')) return;
    setBusy(true); setErr('');
    try {
      await api.post(`/mtd/businesses/${businessId}/vat/submit`, {
        periodKey, from, to, boxes, finalised: true,
      });
      setBoxes(null); reloadSubs();
      alert('Submitted. The receipt is stored against the submission.');
    } catch (e) { setErr(errorMessage(e)); }
    finally { setBusy(false); }
  };

  const saveScheme = async (s: number, flatRate: string) => {
    try {
      await api.put(`/mtd/businesses/${businessId}/vat-scheme`,
        { scheme: s, flatRatePercent: parseFloat(flatRate) || 0 });
      reloadScheme();
    } catch (e) { alert(errorMessage(e)); }
  };

  const connect = async () => {
    try {
      const r = await api.get(`/mtd/businesses/${businessId}/authorise-url`);
      window.open(r.data.url ?? r.data, '_blank');
    } catch (e) { alert(errorMessage(e)); }
  };

  return (
    <div>
      <h1>VAT</h1>
      <div className="sub">
        Nine boxes computed from the ledger, editable before you file. Submission is the legal declaration.
      </div>

      <div className="cards">
        <div className="card">
          <div className="label">HMRC connection</div>
          <div className="value" style={{ fontSize: 16 }}>
            {status?.connected ? `Connected${status.vrn ? ` · ${status.vrn}` : ''}` : 'Not connected'}
          </div>
          <div className="row" style={{ marginTop: 8, gap: 6 }}>
            <button className="btn ghost" onClick={connect}>Authorise</button>
            <button className="btn ghost" onClick={reloadStatus}>Refresh</button>
          </div>
        </div>
        <div className="card">
          <div className="label">VAT scheme</div>
          <div className="value" style={{ fontSize: 16 }}>
            {['Standard (accrual)', 'Cash accounting', 'Flat rate'][scheme?.scheme ?? 0]}
            {scheme?.scheme === 2 ? ` · ${scheme.flatRatePercent}%` : ''}
          </div>
          <SchemePicker current={scheme} onSave={saveScheme} />
        </div>
      </div>

      <h2>Period</h2>
      <div className="row" style={{ alignItems: 'flex-end', maxWidth: 760 }}>
        <Field label="From"><input type="date" value={from} onChange={e => setFrom(e.target.value)} /></Field>
        <Field label="To"><input type="date" value={to} onChange={e => setTo(e.target.value)} /></Field>
        <button className="btn" onClick={preview} disabled={busy}>Compute boxes</button>
        <button className="btn ghost" onClick={loadObligations}>Fetch obligations</button>
      </div>

      {obligations && (
        <table style={{ marginTop: 14 }}>
          <thead><tr><th>Period</th><th>Due</th><th>Status</th><th>Period key</th></tr></thead>
          <tbody>
            {obligations.map((o: any, i: number) => (
              <tr key={i} className="click" onClick={() => {
                setFrom(o.start ?? o.periodFrom); setTo(o.end ?? o.periodTo);
                setPeriodKey(o.periodKey);
              }}>
                <td>{o.start ?? o.periodFrom} → {o.end ?? o.periodTo}</td>
                <td>{o.due ?? o.dueDate}</td>
                <td><span className={`badge${o.status === 'F' ? ' posted' : ''}`}>
                  {o.status === 'F' ? 'Fulfilled' : 'Open'}</span></td>
                <td>{o.periodKey}</td>
              </tr>
            ))}
            {obligations.length === 0 && <tr><td colSpan={4} className="sub">No obligations returned.</td></tr>}
          </tbody>
        </table>
      )}

      {boxes && (
        <div className="card" style={{ marginTop: 16, maxWidth: 620 }}>
          <h2 style={{ marginTop: 0 }}>Return for {from} → {to}</h2>
          <table>
            <tbody>
              {BOXES.map(([key, label]) => (
                <tr key={key}>
                  <td>{label}</td>
                  <td className="num" style={{ width: 140 }}>
                    <input style={{ textAlign: 'right' }} value={boxes[key] ?? 0}
                      onChange={e => setBoxes({ ...boxes, [key]: parseFloat(e.target.value) || 0 })} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <Field label="Period key (from the obligation)">
            <input value={periodKey} onChange={e => setPeriodKey(e.target.value)} placeholder="e.g. 26A1" /></Field>
          <div className="sub" style={{ marginTop: 12 }}>
            By submitting you declare the information is true and complete. False declarations carry penalties.
          </div>
          {err && <div className="err">{err}</div>}
          <button className="btn" style={{ marginTop: 10 }}
            disabled={busy || !periodKey || !status?.connected} onClick={submit}>
            Submit to HMRC
          </button>
        </div>
      )}
      {err && !boxes && <div className="err">{err}</div>}

      <h2>Submission history</h2>
      <table>
        <thead><tr><th>Period</th><th>Submitted</th><th className="num">Net VAT due</th><th>Receipt</th></tr></thead>
        <tbody>
          {(submissions ?? []).map((s: any) => (
            <tr key={s.id}>
              <td>{s.periodFrom} → {s.periodTo}</td>
              <td>{new Date(s.submittedAtUtc).toLocaleDateString('en-GB')}</td>
              <td className="num">{s.netVatDue !== undefined ? gbp(s.netVatDue) : '—'}</td>
              <td>{s.formBundleNumber ?? '—'}</td>
            </tr>
          ))}
          {(submissions ?? []).length === 0 && <tr><td colSpan={4} className="sub">Nothing filed yet.</td></tr>}
        </tbody>
      </table>
    </div>
  );
}

function SchemePicker({ current, onSave }: { current: any; onSave: (s: number, f: string) => void }) {
  const [scheme, setScheme] = useState<number>(current?.scheme ?? 0);
  const [flat, setFlat] = useState(String(current?.flatRatePercent ?? 0));
  return (
    <div style={{ marginTop: 8 }}>
      <Chips options={[[0, 'Standard'], [1, 'Cash'], [2, 'Flat rate']]} value={scheme} onChange={setScheme} />
      {scheme === 2 && (
        <Field label="Flat rate %"><input value={flat} onChange={e => setFlat(e.target.value)} /></Field>
      )}
      <button className="btn ghost" style={{ marginTop: 8 }} onClick={() => onSave(scheme, flat)}>Save scheme</button>
    </div>
  );
}
