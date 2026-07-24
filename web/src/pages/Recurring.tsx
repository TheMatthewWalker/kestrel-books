import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, errorMessage, today } from '../api';
import { Chips, Field, VAT_OPTIONS, useLoad } from '../components';

const FREQ: [number, string][] = [[0, 'Weekly'], [1, 'Monthly'], [2, 'Quarterly'], [3, 'Yearly']];

export default function Recurring() {
  const { businessId } = useParams();
  const [items, reload] = useLoad<any[]>(`/businesses/${businessId}/recurring-invoices`);
  const [customers] = useLoad<any[]>(`/businesses/${businessId}/customers`);
  const [accounts] = useLoad<any[]>(`/businesses/${businessId}/accounts`);
  const [showForm, setShowForm] = useState(false);
  const [f, setF] = useState({
    customerId: '', name: '', numberPrefix: 'REC', frequency: 1, paymentTermsDays: 30,
    nextRunDate: today(), endDate: '', autoPost: false,
    description: '', unitPrice: '', vatRate: 0, accountId: '',
  });
  const [err, setErr] = useState('');

  const income = (accounts ?? []).filter((a: any) => a.type === 3);

  const create = async () => {
    setErr('');
    try {
      await api.post(`/businesses/${businessId}/recurring-invoices`, {
        customerId: f.customerId, name: f.name, numberPrefix: f.numberPrefix,
        frequency: f.frequency, paymentTermsDays: f.paymentTermsDays,
        nextRunDate: f.nextRunDate, endDate: f.endDate || null, autoPost: f.autoPost,
        lines: [{ itemId: null, description: f.description || f.name, quantity: 1,
          unitPrice: parseFloat(f.unitPrice) || 0, vatRate: f.vatRate, accountId: f.accountId }],
      });
      setShowForm(false); reload();
    } catch (e) { setErr(errorMessage(e)); }
  };

  const act = async (fn: () => Promise<unknown>) => {
    try { await fn(); reload(); } catch (e) { alert(errorMessage(e)); }
  };

  return (
    <div>
      <div className="row"><h1>Recurring invoices</h1><div className="spacer" />
        <button className="btn" onClick={() => setShowForm(s => !s)}>{showForm ? 'Close' : 'New template'}</button></div>
      <div className="sub">
        Retainers, rent, subscriptions. Generated as drafts for review by default; auto-post when the amount never varies.
        A background sweep runs twice daily and catches up any missed periods.
      </div>

      {showForm && (
        <div className="card" style={{ marginBottom: 18, maxWidth: 760 }}>
          <div className="row">
            <Field label="Customer">
              <select value={f.customerId} onChange={e => setF({ ...f, customerId: e.target.value })}>
                <option value="">— choose —</option>
                {(customers ?? []).map((c: any) => <option key={c.id} value={c.id}>{c.name}</option>)}
              </select></Field>
            <Field label="Template name">
              <input value={f.name} onChange={e => setF({ ...f, name: e.target.value })}
                placeholder="Acme monthly retainer" /></Field>
            <Field label="Number prefix">
              <input value={f.numberPrefix} onChange={e => setF({ ...f, numberPrefix: e.target.value })} /></Field>
          </div>
          <label>Frequency</label>
          <Chips options={FREQ} value={f.frequency} onChange={v => setF({ ...f, frequency: v })} />
          <div className="row" style={{ marginTop: 8 }}>
            <Field label="First invoice date">
              <input type="date" value={f.nextRunDate} onChange={e => setF({ ...f, nextRunDate: e.target.value })} /></Field>
            <Field label="End date (optional)">
              <input type="date" value={f.endDate} onChange={e => setF({ ...f, endDate: e.target.value })} /></Field>
            <Field label="Payment terms (days)">
              <input type="number" value={f.paymentTermsDays}
                onChange={e => setF({ ...f, paymentTermsDays: parseInt(e.target.value) || 30 })} /></Field>
          </div>
          <div className="row">
            <Field label="Line description">
              <input value={f.description} onChange={e => setF({ ...f, description: e.target.value })} /></Field>
            <Field label="Amount (net)">
              <input value={f.unitPrice} onChange={e => setF({ ...f, unitPrice: e.target.value })} /></Field>
            <Field label="VAT">
              <select value={f.vatRate} onChange={e => setF({ ...f, vatRate: parseInt(e.target.value) })}>
                {VAT_OPTIONS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select></Field>
            <Field label="Income account">
              <select value={f.accountId} onChange={e => setF({ ...f, accountId: e.target.value })}>
                <option value="">— account —</option>
                {income.map((a: any) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}
              </select></Field>
          </div>
          <label>When due</label>
          <Chips options={[['draft', 'Create as draft'], ['auto', 'Auto-post']]}
            value={f.autoPost ? 'auto' : 'draft'} onChange={v => setF({ ...f, autoPost: v === 'auto' })} />
          {err && <div className="err">{err}</div>}
          <button className="btn" style={{ marginTop: 12 }}
            disabled={!f.customerId || !f.name || !f.accountId} onClick={create}>Create template</button>
        </div>
      )}

      <table>
        <thead><tr><th>Name</th><th>Customer</th><th>Frequency</th><th>Next run</th>
          <th className="num">Sent</th><th>Status</th><th /></tr></thead>
        <tbody>
          {(items ?? []).map((t: any) => (
            <tr key={t.id}>
              <td>{t.name}</td><td>{t.customer}</td>
              <td>{FREQ.find(([v]) => v === t.frequency)?.[1] ?? t.frequency}{t.autoPost ? ' · auto-post' : ''}</td>
              <td>{t.nextRunDate}</td>
              <td className="num">{t.generatedCount}</td>
              <td><span className={`badge${t.paused ? '' : ' posted'}`}>{t.paused ? 'Paused' : 'Active'}</span></td>
              <td>
                <div className="row" style={{ gap: 6 }}>
                  <button className="btn ghost" onClick={() => act(async () => {
                    const r = await api.post(`/businesses/${businessId}/recurring-invoices/${t.id}/run-now`);
                    alert(r.data.generated > 0 ? `Generated ${r.data.generated} invoice(s).` : 'Nothing due yet.');
                  })}>Run now</button>
                  <button className="btn ghost" onClick={() => act(() =>
                    api.post(`/businesses/${businessId}/recurring-invoices/${t.id}/pause`, {},
                      { params: { paused: !t.paused } }))}>{t.paused ? 'Resume' : 'Pause'}</button>
                </div>
              </td>
            </tr>
          ))}
          {(items ?? []).length === 0 && <tr><td colSpan={7} className="sub">No templates yet.</td></tr>}
        </tbody>
      </table>
    </div>
  );
}
