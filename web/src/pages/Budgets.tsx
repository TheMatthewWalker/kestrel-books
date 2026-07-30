import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, errorMessage, gbp, today } from '../api';
import { Chips, Field, useLoad } from '../components';

export default function Budgets() {
  const { businessId } = useParams();
  const [budgets, reload] = useLoad<any[]>(`/businesses/${businessId}/budgets`);
  const [accounts] = useLoad<any[]>(`/businesses/${businessId}/accounts`);
  const [selected, setSelected] = useState<any>(null);
  const [view, setView] = useState('variance');
  const [from, setFrom] = useState(`${new Date().getFullYear()}-01-01`);
  const [to, setTo] = useState(today());
  const [report, setReport] = useState<any>(null);
  const [lines, setLines] = useState<Record<string, string>>({});
  const [name, setName] = useState('');
  const [startMonth, setStartMonth] = useState(`${new Date().getFullYear()}-01`);
  const [err, setErr] = useState('');

  const pl = (accounts ?? []).filter((a: any) => a.type === 3 || a.type === 4);

  const create = async () => {
    setErr('');
    try {
      await api.post(`/businesses/${businessId}/budgets`, {
        name, startMonth: `${startMonth}-01`, months: 12,
      });
      setName(''); reload();
    } catch (e) { setErr(errorMessage(e)); }
  };

  const runVariance = async (budget: any) => {
    try {
      const r = await api.get(`/businesses/${businessId}/budgets/${budget.id}/variance`,
        { params: { from, to } });
      setReport(r.data); setSelected(budget); setView('variance');
    } catch (e) { alert(errorMessage(e)); }
  };

  const seed = async (budget: any) => {
    const year = new Date().getFullYear() - 1;
    const uplift = prompt(`Build from ${year}'s actuals. Uplift %?`, '0');
    if (uplift === null) return;
    try {
      const r = await api.post(`/businesses/${businessId}/budgets/${budget.id}/seed-from-actuals`, {
        sourceFrom: `${year}-01-01`, sourceTo: `${year}-12-31`,
        upliftPercent: parseFloat(uplift) || 0,
      });
      alert(`${r.data.linesCreated} monthly figures created from last year plus ${uplift}%.`);
      reload();
    } catch (e) { alert(errorMessage(e)); }
  };

  const saveLines = async (budget: any) => {
    const payload = Object.entries(lines)
      .filter(([, v]) => v !== '')
      .flatMap(([accountId, value]) => {
        const monthly = parseFloat(value) || 0;
        return Array.from({ length: budget.months }, (_, i) => {
          const d = new Date(budget.startMonth);
          d.setMonth(d.getMonth() + i);
          return {
            accountId, trackingOptionId: null,
            month: d.toISOString().slice(0, 10), amount: monthly,
          };
        });
      });
    try {
      await api.put(`/businesses/${businessId}/budgets/${budget.id}/lines`, payload);
      alert('Budget saved — the same figure applied to each month.');
      setLines({}); reload();
    } catch (e) { alert(errorMessage(e)); }
  };

  const Row = ({ r }: { r: any }) => (
    <tr>
      <td>{r.code}</td><td>{r.name}</td>
      <td className="num">{gbp(r.actual)}</td>
      <td className="num">{gbp(r.budget)}</td>
      <td className={`num ${r.favourable ? 'cr' : 'dr'}`}>
        {r.variance >= 0 ? '+' : ''}{gbp(r.variance)}
      </td>
      <td className="num">{r.variancePercent === null ? '—' : `${r.variancePercent}%`}</td>
      <td>{r.favourable
        ? <span className="badge posted">favourable</span>
        : <span className="badge overdue">adverse</span>}</td>
    </tr>
  );

  return (
    <div>
      <h1>Budgets</h1>
      <div className="sub">
        Monthly figures per account, because real budgets are lumpy — insurance renews once,
        the audit fee lands in one hit. Variance says not just how much, but whether it's good news.
      </div>

      <div className="card" style={{ maxWidth: 620, marginBottom: 18 }}>
        <div className="row" style={{ alignItems: 'flex-end' }}>
          <Field label="New budget name">
            <input value={name} onChange={e => setName(e.target.value)} placeholder="2026 plan" /></Field>
          <Field label="Starting">
            <input type="month" value={startMonth} onChange={e => setStartMonth(e.target.value)} /></Field>
          <button className="btn" disabled={!name} onClick={create}>Create</button>
        </div>
        {err && <div className="err">{err}</div>}
      </div>

      <table>
        <thead><tr><th>Name</th><th>Starts</th><th className="num">Months</th>
          <th className="num">Figures</th><th /></tr></thead>
        <tbody>
          {(budgets ?? []).map((b: any) => (
            <tr key={b.id}>
              <td>{b.name}</td><td>{b.startMonth}</td>
              <td className="num">{b.months}</td><td className="num">{b.lineCount}</td>
              <td>
                <div className="row" style={{ gap: 6 }}>
                  <button className="btn ghost" onClick={() => runVariance(b)}>Variance</button>
                  <button className="btn ghost" onClick={() => { setSelected(b); setView('edit'); }}>Figures</button>
                  <button className="btn ghost" onClick={() => seed(b)}>Build from last year</button>
                </div>
              </td>
            </tr>
          ))}
          {(budgets ?? []).length === 0 &&
            <tr><td colSpan={5} className="sub">No budgets yet.</td></tr>}
        </tbody>
      </table>

      {selected && (
        <div style={{ marginTop: 20 }}>
          <div className="row">
            <h2 style={{ margin: 0 }}>{selected.name}</h2><div className="spacer" />
            <Chips options={[['variance', 'Variance'], ['edit', 'Figures']]} value={view} onChange={setView} />
            <button className="btn ghost" onClick={() => { setSelected(null); setReport(null); }}>Close</button>
          </div>

          {view === 'variance' && (
            <>
              <div className="row" style={{ alignItems: 'flex-end', marginTop: 10, maxWidth: 560 }}>
                <Field label="From"><input type="date" value={from}
                  onChange={e => setFrom(e.target.value)} /></Field>
                <Field label="To"><input type="date" value={to}
                  onChange={e => setTo(e.target.value)} /></Field>
                <button className="btn" onClick={() => runVariance(selected)}>Refresh</button>
              </div>

              {report && (
                <>
                  <div className="cards" style={{ marginTop: 14 }}>
                    <div className="card"><div className="label">Actual profit</div>
                      <div className="value">{gbp(report.actualProfit)}</div></div>
                    <div className="card"><div className="label">Budgeted profit</div>
                      <div className="value">{gbp(report.budgetProfit)}</div></div>
                    <div className="card"><div className="label">Variance</div>
                      <div className={`value${report.profitVariance < 0 ? ' bad' : ''}`}>
                        {report.profitVariance >= 0 ? '+' : ''}{gbp(report.profitVariance)}</div></div>
                  </div>

                  <h2>Income</h2>
                  <table>
                    <thead><tr><th>Code</th><th>Account</th><th className="num">Actual</th>
                      <th className="num">Budget</th><th className="num">Variance</th>
                      <th className="num">%</th><th /></tr></thead>
                    <tbody>
                      {report.income.map((r: any) => <Row key={r.code} r={r} />)}
                      {report.income.length === 0 && <tr><td colSpan={7} className="sub">Nothing.</td></tr>}
                    </tbody>
                  </table>

                  <h2>Expenses</h2>
                  <table>
                    <thead><tr><th>Code</th><th>Account</th><th className="num">Actual</th>
                      <th className="num">Budget</th><th className="num">Variance</th>
                      <th className="num">%</th><th /></tr></thead>
                    <tbody>
                      {report.expenses.map((r: any) => <Row key={r.code} r={r} />)}
                      {report.expenses.length === 0 && <tr><td colSpan={7} className="sub">Nothing.</td></tr>}
                    </tbody>
                  </table>
                </>
              )}
            </>
          )}

          {view === 'edit' && (
            <div style={{ marginTop: 12 }}>
              <div className="sub">
                Enter a monthly figure per account — it is applied to every month of the budget.
                Leave blank to leave an account alone.
              </div>
              <table>
                <thead><tr><th>Code</th><th>Account</th><th className="num">Per month</th></tr></thead>
                <tbody>
                  {pl.map((a: any) => (
                    <tr key={a.id}>
                      <td>{a.code}</td><td>{a.name}</td>
                      <td className="num" style={{ width: 140 }}>
                        <input style={{ textAlign: 'right' }} value={lines[a.id] ?? ''}
                          onChange={e => setLines({ ...lines, [a.id]: e.target.value })} />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
              <button className="btn" style={{ marginTop: 12 }}
                onClick={() => saveLines(selected)}>Save figures</button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
