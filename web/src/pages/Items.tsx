import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, errorMessage, gbp } from '../api';
import { Chips, Field, VAT_OPTIONS, useLoad } from '../components';

const KINDS: [number, string][] = [[0, 'Product'], [1, 'Service'], [2, 'Raw material'], [3, 'Finished good']];

/** Item master: what you sell and buy, and which items carry stock. */
export default function Items() {
  const { businessId } = useParams();
  const [items, reload] = useLoad<any[]>(`/businesses/${businessId}/items`);
  const [accounts] = useLoad<any[]>(`/businesses/${businessId}/accounts`);
  const [showForm, setShowForm] = useState(false);
  const [f, setF] = useState({
    kind: 0, code: '', name: '', salesPrice: '', purchasePrice: '', defaultVatRate: 0,
    salesAccountId: '', purchaseAccountId: '', trackStock: false,
    inventoryAccountId: '', cogsAccountId: '',
  });
  const [err, setErr] = useState('');

  const income = (accounts ?? []).filter((a: any) => a.type === 3);
  const expense = (accounts ?? []).filter((a: any) => a.type === 4);
  const assets = (accounts ?? []).filter((a: any) => a.type === 0);

  const save = async () => {
    setErr('');
    try {
      await api.post(`/businesses/${businessId}/items`, {
        kind: f.kind, code: f.code, name: f.name,
        salesPrice: parseFloat(f.salesPrice) || 0, purchasePrice: parseFloat(f.purchasePrice) || 0,
        defaultVatRate: f.defaultVatRate,
        salesAccountId: f.salesAccountId || null, purchaseAccountId: f.purchaseAccountId || null,
        trackStock: f.trackStock,
        inventoryAccountId: f.trackStock ? (f.inventoryAccountId || null) : null,
        cogsAccountId: f.trackStock ? (f.cogsAccountId || null) : null,
      });
      setShowForm(false);
      setF({ ...f, code: '', name: '', salesPrice: '', purchasePrice: '' });
      reload();
    } catch (e) { setErr(errorMessage(e)); }
  };

  return (
    <div>
      <div className="row"><h1>Items</h1><div className="spacer" />
        <button className="btn" onClick={() => setShowForm(s => !s)}>{showForm ? 'Close' : 'New item'}</button></div>
      <div className="sub">
        Products and services with their default prices, VAT treatment and analysis accounts.
        Stock-tracked items also carry a quantity and an average cost.
      </div>

      {showForm && (
        <div className="card" style={{ marginBottom: 18, maxWidth: 820 }}>
          <label>Kind</label>
          <Chips options={KINDS} value={f.kind} onChange={v => setF({ ...f, kind: v })} />
          <div className="row" style={{ marginTop: 8 }}>
            <Field label="Code"><input value={f.code} onChange={e => setF({ ...f, code: e.target.value })} /></Field>
            <div style={{ flex: 2 }}><Field label="Name">
              <input value={f.name} onChange={e => setF({ ...f, name: e.target.value })} /></Field></div>
            <Field label="Default VAT">
              <select value={f.defaultVatRate} onChange={e => setF({ ...f, defaultVatRate: parseInt(e.target.value) })}>
                {VAT_OPTIONS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select></Field>
          </div>
          <div className="row">
            <Field label="Sales price (net)">
              <input value={f.salesPrice} onChange={e => setF({ ...f, salesPrice: e.target.value })} /></Field>
            <Field label="Purchase price (net)">
              <input value={f.purchasePrice} onChange={e => setF({ ...f, purchasePrice: e.target.value })} /></Field>
            <Field label="Sales account">
              <select value={f.salesAccountId} onChange={e => setF({ ...f, salesAccountId: e.target.value })}>
                <option value="">— none —</option>
                {income.map((a: any) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}
              </select></Field>
            <Field label="Purchase account">
              <select value={f.purchaseAccountId} onChange={e => setF({ ...f, purchaseAccountId: e.target.value })}>
                <option value="">— none —</option>
                {expense.map((a: any) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}
              </select></Field>
          </div>
          <label>Stock</label>
          <Chips options={[['no', 'Not tracked'], ['yes', 'Track stock (AVCO)']]}
            value={f.trackStock ? 'yes' : 'no'} onChange={v => setF({ ...f, trackStock: v === 'yes' })} />
          {f.trackStock && (
            <div className="row" style={{ marginTop: 8 }}>
              <Field label="Inventory account (asset)">
                <select value={f.inventoryAccountId} onChange={e => setF({ ...f, inventoryAccountId: e.target.value })}>
                  <option value="">— choose —</option>
                  {assets.map((a: any) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}
                </select></Field>
              <Field label="Cost of sales account">
                <select value={f.cogsAccountId} onChange={e => setF({ ...f, cogsAccountId: e.target.value })}>
                  <option value="">— choose —</option>
                  {expense.map((a: any) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}
                </select></Field>
            </div>
          )}
          {err && <div className="err">{err}</div>}
          <button className="btn" style={{ marginTop: 12 }}
            disabled={!f.code || !f.name} onClick={save}>Save item</button>
        </div>
      )}

      <table>
        <thead><tr><th>Code</th><th>Name</th><th>Kind</th><th className="num">Sales</th>
          <th className="num">Purchase</th><th>Stock</th><th className="num">On hand</th></tr></thead>
        <tbody>
          {(items ?? []).map((i: any) => (
            <tr key={i.id}>
              <td>{i.code}</td><td>{i.name}</td>
              <td>{KINDS.find(([v]) => v === i.kind)?.[1] ?? i.kind}</td>
              <td className="num">{gbp(i.salesPrice)}</td>
              <td className="num">{gbp(i.purchasePrice)}</td>
              <td>{i.trackStock ? <span className="badge posted">Tracked</span> : <span className="badge">—</span>}</td>
              <td className="num">{i.trackStock ? (i.quantityOnHand ?? 0) : '—'}</td>
            </tr>
          ))}
          {(items ?? []).length === 0 && <tr><td colSpan={7} className="sub">No items yet.</td></tr>}
        </tbody>
      </table>
    </div>
  );
}
