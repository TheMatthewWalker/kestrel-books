import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, errorMessage, gbp, today } from '../api';
import { Chips, Field, useLoad } from '../components';

const STATUS: [number, string][] = [[0, 'Under construction'], [1, 'In use'], [2, 'Disposed']];
const METHOD: [number, string][] = [[0, 'Straight line'], [1, 'Reducing balance']];

export default function Assets() {
  const { businessId } = useParams();
  const [assets, reload] = useLoad<any[]>(`/businesses/${businessId}/assets`);
  const [accounts] = useLoad<any[]>(`/businesses/${businessId}/accounts`);
  const [showForm, setShowForm] = useState(false);
  const [runMonth, setRunMonth] = useState(today().slice(0, 7));
  const [err, setErr] = useState('');
  const [f, setF] = useState({
    code: '', description: '', category: '', status: 1,
    acquisitionDate: today(), cost: '', residualValue: '0',
    method: 0, usefulLifeMonths: '60', annualRatePercent: '25',
    depreciationStart: today(), costAccountId: '', accumDepAccountId: '',
    depExpenseAccountId: '', notes: '',
  });

  const assetAccounts = (accounts ?? []).filter((a: any) => a.type === 0);
  const expenseAccounts = (accounts ?? []).filter((a: any) => a.type === 4);

  const save = async () => {
    setErr('');
    try {
      await api.post(`/businesses/${businessId}/assets`, {
        code: f.code, description: f.description, category: f.category || null, status: f.status,
        acquisitionDate: f.acquisitionDate, cost: parseFloat(f.cost) || 0,
        residualValue: parseFloat(f.residualValue) || 0, method: f.method,
        usefulLifeMonths: parseInt(f.usefulLifeMonths) || 0,
        annualRatePercent: parseFloat(f.annualRatePercent) || 0,
        depreciationStart: f.depreciationStart, costAccountId: f.costAccountId,
        accumDepAccountId: f.accumDepAccountId, depExpenseAccountId: f.depExpenseAccountId,
        notes: f.notes || null,
      });
      setShowForm(false); setF({ ...f, code: '', description: '', cost: '' });
      reload();
    } catch (e) { setErr(errorMessage(e)); }
  };

  const runDepreciation = async () => {
    const [year, month] = runMonth.split('-');
    if (!confirm(`Post depreciation for ${runMonth}? Running twice for the same month is blocked.`)) return;
    try {
      const r = await api.post(`/businesses/${businessId}/assets/depreciation-run`, {},
        { params: { year: parseInt(year), month: parseInt(month) } });
      alert(r.data.message ?? `Posted journal ${r.data.journalNumber}.`);
      reload();
    } catch (e) { alert(errorMessage(e)); }
  };

  const capitalise = async (asset: any) => {
    const date = prompt('Capitalisation date (YYYY-MM-DD)', today());
    if (!date) return;
    try {
      const r = await api.post(`/businesses/${businessId}/assets/${asset.id}/capitalise`, {}, { params: { date } });
      alert(`Capitalised — journal ${r.data.journalNumber}. Depreciation starts from this date.`);
      reload();
    } catch (e) { alert(errorMessage(e)); }
  };

  const totals = (assets ?? []).reduce((t: any, a: any) => ({
    cost: t.cost + a.cost, dep: t.dep + a.accumulatedDepreciation, nbv: t.nbv + a.netBookValue,
  }), { cost: 0, dep: 0, nbv: 0 });

  return (
    <div>
      <div className="row"><h1>Fixed assets</h1><div className="spacer" />
        <button className="btn" onClick={() => setShowForm(s => !s)}>{showForm ? 'Close' : 'New asset'}</button></div>
      <div className="sub">
        The register drives the depreciation charge. Assets under construction accumulate cost without
        depreciating until you capitalise them.
      </div>

      <div className="cards">
        <div className="card"><div className="label">Cost</div><div className="value">{gbp(totals.cost)}</div></div>
        <div className="card"><div className="label">Accumulated depreciation</div>
          <div className="value">{gbp(totals.dep)}</div></div>
        <div className="card"><div className="label">Net book value</div>
          <div className="value">{gbp(totals.nbv)}</div></div>
      </div>

      <h2>Monthly depreciation</h2>
      <div className="row" style={{ alignItems: 'flex-end', maxWidth: 520 }}>
        <Field label="Month"><input type="month" value={runMonth} onChange={e => setRunMonth(e.target.value)} /></Field>
        <button className="btn" onClick={runDepreciation}>Post depreciation run</button>
      </div>
      <div className="sub" style={{ marginTop: 6 }}>
        One journal for every in-use asset: Dr depreciation expense, Cr accumulated depreciation.
        Idempotent — a second run for the same month does nothing.
      </div>

      {showForm && (
        <div className="card" style={{ marginTop: 16, maxWidth: 860 }}>
          <div className="row">
            <Field label="Code"><input value={f.code} onChange={e => setF({ ...f, code: e.target.value })} /></Field>
            <div style={{ flex: 2 }}><Field label="Description">
              <input value={f.description} onChange={e => setF({ ...f, description: e.target.value })} /></Field></div>
            <Field label="Category">
              <input value={f.category} onChange={e => setF({ ...f, category: e.target.value })}
                placeholder="Plant & machinery" /></Field>
          </div>
          <label>Status</label>
          <Chips options={STATUS} value={f.status} onChange={v => setF({ ...f, status: v })} />
          <div className="row" style={{ marginTop: 8 }}>
            <Field label="Acquired"><input type="date" value={f.acquisitionDate}
              onChange={e => setF({ ...f, acquisitionDate: e.target.value })} /></Field>
            <Field label="Cost"><input value={f.cost} onChange={e => setF({ ...f, cost: e.target.value })} /></Field>
            <Field label="Residual value"><input value={f.residualValue}
              onChange={e => setF({ ...f, residualValue: e.target.value })} /></Field>
          </div>
          <label>Depreciation method</label>
          <Chips options={METHOD} value={f.method} onChange={v => setF({ ...f, method: v })} />
          <div className="row" style={{ marginTop: 8 }}>
            {f.method === 0
              ? <Field label="Useful life (months)"><input value={f.usefulLifeMonths}
                  onChange={e => setF({ ...f, usefulLifeMonths: e.target.value })} /></Field>
              : <Field label="Annual rate %"><input value={f.annualRatePercent}
                  onChange={e => setF({ ...f, annualRatePercent: e.target.value })} /></Field>}
            <Field label="Depreciation starts"><input type="date" value={f.depreciationStart}
              onChange={e => setF({ ...f, depreciationStart: e.target.value })} /></Field>
          </div>
          <div className="row">
            <Field label="Cost account">
              <select value={f.costAccountId} onChange={e => setF({ ...f, costAccountId: e.target.value })}>
                <option value="">— choose —</option>
                {assetAccounts.map((a: any) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}
              </select></Field>
            <Field label="Accumulated depreciation">
              <select value={f.accumDepAccountId} onChange={e => setF({ ...f, accumDepAccountId: e.target.value })}>
                <option value="">— choose —</option>
                {assetAccounts.map((a: any) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}
              </select></Field>
            <Field label="Depreciation expense">
              <select value={f.depExpenseAccountId} onChange={e => setF({ ...f, depExpenseAccountId: e.target.value })}>
                <option value="">— choose —</option>
                {expenseAccounts.map((a: any) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}
              </select></Field>
          </div>
          {err && <div className="err">{err}</div>}
          <button className="btn" style={{ marginTop: 12 }}
            disabled={!f.code || !f.cost || !f.costAccountId || !f.accumDepAccountId || !f.depExpenseAccountId}
            onClick={save}>Save asset</button>
        </div>
      )}

      <h2>Register</h2>
      <table>
        <thead><tr><th>Code</th><th>Description</th><th>Status</th><th>Method</th>
          <th className="num">Cost</th><th className="num">Accum. dep.</th>
          <th className="num">NBV</th><th className="num">Next charge</th><th /></tr></thead>
        <tbody>
          {(assets ?? []).map((a: any) => (
            <tr key={a.id}>
              <td>{a.code}</td><td>{a.description}</td>
              <td><span className={`badge${a.status === 1 ? ' posted' : ''}`}>
                {STATUS.find(([v]) => v === a.status)?.[1] ?? a.status}</span></td>
              <td>{METHOD.find(([v]) => v === a.method)?.[1] ?? a.method}</td>
              <td className="num">{gbp(a.cost)}</td>
              <td className="num">{gbp(a.accumulatedDepreciation)}</td>
              <td className="num"><strong>{gbp(a.netBookValue)}</strong></td>
              <td className="num">{gbp(a.nextMonthlyCharge)}</td>
              <td>{a.status === 0 &&
                <button className="btn ghost" onClick={() => capitalise(a)}>Capitalise</button>}</td>
            </tr>
          ))}
          {(assets ?? []).length === 0 && <tr><td colSpan={9} className="sub">No assets yet.</td></tr>}
        </tbody>
      </table>
    </div>
  );
}
