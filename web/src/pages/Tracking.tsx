import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, errorMessage, gbp, today } from '../api';
import { Field, useLoad } from '../components';

/** Departments, projects, sites — the second dimension alongside the account code. */
export default function Tracking() {
  const { businessId } = useParams();
  const [categories, reload] = useLoad<any[]>(`/businesses/${businessId}/tracking`);
  const [newCategory, setNewCategory] = useState('');
  const [newOption, setNewOption] = useState<Record<string, string>>({});
  const [reportFor, setReportFor] = useState('');
  const [from, setFrom] = useState(`${new Date().getFullYear()}-01-01`);
  const [to, setTo] = useState(today());
  const [report, setReport] = useState<any>(null);
  const [err, setErr] = useState('');

  const act = async (fn: () => Promise<unknown>) => {
    setErr('');
    try { await fn(); reload(); } catch (e) { setErr(errorMessage(e)); }
  };

  const runReport = async (categoryId: string) => {
    setReportFor(categoryId);
    try {
      const r = await api.get(`/businesses/${businessId}/tracking/${categoryId}/profit-and-loss`,
        { params: { from, to } });
      setReport(r.data);
    } catch (e) { alert(errorMessage(e)); }
  };

  return (
    <div>
      <h1>Tracking categories</h1>
      <div className="sub">
        The chart of accounts says what kind of cost something is; tracking says whose it is.
        Having both means you never have to duplicate the chart per department — which is how
        charts of accounts become unusable. Two categories maximum, deliberately.
      </div>

      {(categories ?? []).map((c: any) => (
        <div key={c.id} className="card" style={{ marginBottom: 12, maxWidth: 700 }}>
          <div className="row">
            <strong>{c.name}</strong>
            <span className="badge">{c.options.length} options</span>
            <div className="spacer" />
            <button className="btn ghost" onClick={() => runReport(c.id)}>P&amp;L by {c.name.toLowerCase()}</button>
          </div>
          <div className="row" style={{ flexWrap: 'wrap', gap: 6, marginTop: 10 }}>
            {c.options.map((o: any) => (
              <span key={o.id} className="badge" style={{ cursor: 'pointer' }}
                title="Archive this option"
                onClick={() => {
                  if (confirm(`Archive "${o.name}"? Past journals keep it; it just stops being offered.`))
                    act(() => api.post(`/businesses/${businessId}/tracking/options/${o.id}/archive`));
                }}>{o.name} ×</span>
            ))}
            {c.options.length === 0 && <span className="sub">No options yet.</span>}
          </div>
          <div className="row" style={{ marginTop: 10 }}>
            <input placeholder={`New ${c.name.toLowerCase()}…`} value={newOption[c.id] ?? ''}
              onChange={e => setNewOption({ ...newOption, [c.id]: e.target.value })} />
            <button className="btn ghost" disabled={!newOption[c.id]}
              onClick={() => act(async () => {
                await api.post(`/businesses/${businessId}/tracking/${c.id}/options`,
                  { name: newOption[c.id] });
                setNewOption({ ...newOption, [c.id]: '' });
              })}>Add</button>
          </div>
        </div>
      ))}

      {(categories ?? []).length < 2 && (
        <div className="card" style={{ maxWidth: 520 }}>
          <Field label="New category name">
            <input value={newCategory} onChange={e => setNewCategory(e.target.value)}
              placeholder="Department, Project, Site…" /></Field>
          {err && <div className="err">{err}</div>}
          <button className="btn" style={{ marginTop: 10 }} disabled={!newCategory}
            onClick={() => act(async () => {
              await api.post(`/businesses/${businessId}/tracking`, { name: newCategory });
              setNewCategory('');
            })}>Add category</button>
        </div>
      )}

      {reportFor && (
        <div style={{ marginTop: 20 }}>
          <div className="row" style={{ alignItems: 'flex-end', maxWidth: 560 }}>
            <Field label="From"><input type="date" value={from}
              onChange={e => setFrom(e.target.value)} /></Field>
            <Field label="To"><input type="date" value={to}
              onChange={e => setTo(e.target.value)} /></Field>
            <button className="btn ghost" onClick={() => runReport(reportFor)}>Refresh</button>
          </div>
          {report && (
            <table style={{ marginTop: 12 }}>
              <thead><tr><th>Segment</th><th className="num">Income</th>
                <th className="num">Expenses</th><th className="num">Profit</th></tr></thead>
              <tbody>
                {report.segments.map((s: any, i: number) => (
                  <tr key={i}>
                    <td>{s.name}</td>
                    <td className="num">{gbp(s.income)}</td>
                    <td className="num">{gbp(s.expenses)}</td>
                    <td className={`num${s.profit < 0 ? ' dr' : ''}`}><strong>{gbp(s.profit)}</strong></td>
                  </tr>
                ))}
                <tr><td><strong>Total</strong></td><td /><td />
                  <td className="num"><strong>{gbp(report.totalProfit)}</strong></td></tr>
              </tbody>
            </table>
          )}
        </div>
      )}
    </div>
  );
}
