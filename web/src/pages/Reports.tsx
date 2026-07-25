import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, errorMessage, gbp, today } from '../api';
import { Chips, Field } from '../components';

type Line = { code: string; name: string; group: string; amount: number };
type Section = { name: string; lines: Line[]; subtotal: number };
type Report = { title: string; period: string; sections: Section[]; total: number };

const REPORTS: [string, string][] = [
  ['trial-balance', 'Trial balance'],
  ['profit-and-loss', 'Profit & loss'],
  ['balance-sheet', 'Balance sheet'],
  ['cash-flow', 'Cash flow'],
];

/** Every report is the same shape from the API: titled sections of coded lines. */
export default function Reports() {
  const { businessId } = useParams();
  const [which, setWhich] = useState('trial-balance');
  const [from, setFrom] = useState(`${new Date().getFullYear()}-01-01`);
  const [to, setTo] = useState(today());
  const [report, setReport] = useState<Report | null>(null);
  const [err, setErr] = useState('');

  const periodic = which === 'profit-and-loss' || which === 'cash-flow';

  useEffect(() => {
    setErr('');
    const params = periodic ? { from, to } : { asOf: to };
    api.get(`/businesses/${businessId}/reports/${which}`, { params })
      .then(r => setReport(r.data))
      .catch(e => { setReport(null); setErr(errorMessage(e)); });
  }, [businessId, which, from, to, periodic]);

  const csv = () => {
    if (!report) return;
    const rows = [['Section', 'Code', 'Account', 'Group', 'Amount']];
    report.sections.forEach(s => s.lines.forEach(l =>
      rows.push([s.name, l.code, l.name, l.group, String(l.amount)])));
    const blob = new Blob([rows.map(r => r.map(c => `"${c}"`).join(',')).join('\n')],
      { type: 'text/csv' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = `${which}-${to}.csv`;
    a.click();
  };

  return (
    <div>
      <h1>Reports</h1>
      <div className="sub">Live from the ledger — every figure is the sum of posted journal lines.</div>

      <Chips options={REPORTS} value={which} onChange={setWhich} />

      <div className="row" style={{ alignItems: 'flex-end', marginTop: 12, maxWidth: 620 }}>
        {periodic && (
          <Field label="From"><input type="date" value={from} onChange={e => setFrom(e.target.value)} /></Field>
        )}
        <Field label={periodic ? 'To' : 'As at'}>
          <input type="date" value={to} onChange={e => setTo(e.target.value)} /></Field>
        <button className="btn ghost" onClick={csv} disabled={!report}>Export CSV</button>
      </div>

      {err && <div className="err">{err}</div>}

      {report && (
        <div style={{ marginTop: 18 }}>
          <h2 style={{ marginTop: 0 }}>{report.title}</h2>
          <div className="sub">{report.period}</div>

          {report.sections.map(section => (
            <div key={section.name} style={{ marginBottom: 18 }}>
              <h2>{section.name}</h2>
              <table>
                <thead><tr><th>Code</th><th>Account</th><th>Group</th><th className="num">Amount</th></tr></thead>
                <tbody>
                  {section.lines.map((l, i) => (
                    <tr key={`${l.code}-${i}`}>
                      <td>{l.code}</td><td>{l.name}</td><td className="sub" style={{ marginBottom: 0 }}>{l.group}</td>
                      <td className={`num${l.amount < 0 ? ' dr' : ''}`}>{gbp(l.amount)}</td>
                    </tr>
                  ))}
                  {section.lines.length === 0 &&
                    <tr><td colSpan={4} className="sub">Nothing in this section.</td></tr>}
                  <tr>
                    <td /><td><strong>{section.name} total</strong></td><td />
                    <td className="num"><strong>{gbp(section.subtotal)}</strong></td>
                  </tr>
                </tbody>
              </table>
            </div>
          ))}

          <div className="card" style={{ maxWidth: 420 }}>
            <div className="label">
              {which === 'trial-balance' ? 'Debits less credits'
                : which === 'balance-sheet' ? 'Assets less liabilities and equity'
                : which === 'profit-and-loss' ? 'Profit for the period'
                : 'Net movement in cash'}
            </div>
            <div className={`value${which === 'trial-balance' || which === 'balance-sheet'
              ? (Math.abs(report.total) > 0.004 ? ' bad' : '') : (report.total < 0 ? ' bad' : '')}`}>
              {gbp(report.total)}
            </div>
            {(which === 'trial-balance' || which === 'balance-sheet') && (
              <div className="sub" style={{ marginTop: 4, marginBottom: 0 }}>
                {Math.abs(report.total) > 0.004
                  ? 'This should be zero — the ledger is out of balance, which needs investigating.'
                  : 'Balances, as it must.'}
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
