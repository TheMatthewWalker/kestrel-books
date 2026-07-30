import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, errorMessage, gbp, today } from '../api';
import { Chips, Field } from '../components';

const SEVERITY = ['Note', 'Worth a look', 'Important'];

/**
 * The checklist every accountant runs before signing off, done by the machine.
 * Everything here is a question, not an accusation — real books contain
 * legitimate duplicates and legitimate round numbers.
 */
export default function Review() {
  const { businessId } = useParams();
  const [view, setView] = useState('review');
  const [from, setFrom] = useState(`${new Date().getFullYear()}-01-01`);
  const [to, setTo] = useState(today());
  const [result, setResult] = useState<any>(null);
  const [preflight, setPreflight] = useState<any>(null);
  const [busy, setBusy] = useState(false);

  const run = async () => {
    setBusy(true);
    try {
      if (view === 'review') {
        const r = await api.get(`/businesses/${businessId}/review`, { params: { from, to } });
        setResult(r.data);
      } else {
        const r = await api.get(`/businesses/${businessId}/review/vat-preflight`, { params: { from, to } });
        setPreflight(r.data);
      }
    } catch (e) { alert(errorMessage(e)); }
    finally { setBusy(false); }
  };

  const badge = (s: number) => s === 2 ? 'badge overdue' : s === 1 ? 'badge' : 'badge';

  return (
    <div>
      <h1>Review</h1>
      <div className="sub">
        The questions worth asking before you sign anything off. Every item is a prompt to look,
        not a claim that something is wrong.
      </div>

      <Chips options={[['review', 'Period review'], ['vat', 'VAT pre-flight']]}
        value={view} onChange={v => { setView(v); }} />

      <div className="row" style={{ alignItems: 'flex-end', marginTop: 12, maxWidth: 620 }}>
        <Field label="From"><input type="date" value={from} onChange={e => setFrom(e.target.value)} /></Field>
        <Field label="To"><input type="date" value={to} onChange={e => setTo(e.target.value)} /></Field>
        <button className="btn" disabled={busy} onClick={run}>
          {view === 'review' ? 'Review the period' : 'Check the return'}
        </button>
      </div>

      {view === 'review' && result && (
        <div style={{ marginTop: 18 }}>
          <div className="cards">
            <div className="card"><div className="label">Checks run</div>
              <div className="value">{result.checked}</div></div>
            <div className="card"><div className="label">Things to look at</div>
              <div className={`value${result.findings.length > 0 ? ' bad' : ''}`}>
                {result.findings.length}</div></div>
            <div className="card"><div className="label">Important</div>
              <div className={`value${result.findings.filter((f: any) => f.severity === 2).length > 0 ? ' bad' : ''}`}>
                {result.findings.filter((f: any) => f.severity === 2).length}</div></div>
          </div>

          {result.findings.length === 0 ? (
            <div className="card" style={{ marginTop: 16, maxWidth: 620 }}>
              <strong>Nothing flagged.</strong>
              <div className="sub" style={{ marginTop: 6, marginBottom: 0 }}>
                All {result.checked} checks passed for this period. That is not a guarantee the books
                are right — it means none of the usual warning signs are present.
              </div>
            </div>
          ) : (
            <table style={{ marginTop: 14 }}>
              <thead><tr><th>Priority</th><th>What</th><th>Why it is worth a look</th>
                <th className="num">Amount</th><th>Date</th></tr></thead>
              <tbody>
                {result.findings.map((f: any, i: number) => (
                  <tr key={i}>
                    <td><span className={badge(f.severity)}>{SEVERITY[f.severity]}</span></td>
                    <td>{f.title}</td>
                    <td style={{ fontSize: 13 }}>{f.detail}</td>
                    <td className="num">{f.amount != null ? gbp(f.amount) : '—'}</td>
                    <td>{f.date ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}

      {view === 'vat' && preflight && (
        <div style={{ marginTop: 18 }}>
          <div className="card" style={{ maxWidth: 640 }}>
            <div className="label">Net VAT due for the period</div>
            <div className="value">{gbp(preflight.boxes.netVatDue)}</div>
            <div className="sub" style={{ marginTop: 8, marginBottom: 0 }}>
              {preflight.safeToSubmit
                ? preflight.failures === 0
                  ? 'Every check passed. Nothing here says do not file.'
                  : `${preflight.failures} check(s) worth reading below, but nothing blocking.`
                : 'Something important failed. Do not submit until it is resolved.'}
            </div>
          </div>

          <table style={{ marginTop: 14 }}>
            <thead><tr><th>Check</th><th>Result</th><th>Detail</th></tr></thead>
            <tbody>
              {preflight.checks.map((c: any, i: number) => (
                <tr key={i}>
                  <td>{c.title}</td>
                  <td>{c.passed
                    ? <span className="badge posted">pass</span>
                    : <span className={c.severity === 2 ? 'badge overdue' : 'badge'}>
                        {c.severity === 2 ? 'blocking' : 'check'}</span>}</td>
                  <td style={{ fontSize: 13 }}>{c.detail}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
