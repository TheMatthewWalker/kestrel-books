import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, errorMessage, gbp, today } from '../api';
import { Field, useLoad } from '../components';

const STATUS = ['Draft', 'In progress', 'Completed', 'Cancelled'];

/** Bills of material and production orders: materials in, finished goods out, at cost. */
export default function Production() {
  const { businessId } = useParams();
  const [orders, reloadOrders] = useLoad<any[]>(`/businesses/${businessId}/production/orders`);
  const [items] = useLoad<any[]>(`/businesses/${businessId}/items`);
  const [bomItemId, setBomItemId] = useState('');
  const [bom, setBom] = useState<any>(null);
  const [labour, setLabour] = useState('0');
  const [overhead, setOverhead] = useState('0');
  const [lines, setLines] = useState<{ componentItemId: string; quantityPer: string }[]>([]);
  const [orderItemId, setOrderItemId] = useState('');
  const [orderQty, setOrderQty] = useState('');
  const [err, setErr] = useState('');

  const tracked = (items ?? []).filter((i: any) => i.trackStock);
  const finished = (items ?? []).filter((i: any) => i.kind === 3 || i.kind === 0);

  const loadBom = async (itemId: string) => {
    setBomItemId(itemId);
    if (!itemId) { setBom(null); return; }
    try {
      const r = await api.get(`/businesses/${businessId}/production/boms/${itemId}`);
      setBom(r.data);
      if (r.data.exists) {
        setLabour(String(r.data.labourCostPerUnit));
        setOverhead(String(r.data.overheadCostPerUnit));
        setLines(r.data.lines.map((l: any) => ({
          componentItemId: l.componentItemId, quantityPer: String(l.quantityPer),
        })));
      } else { setLabour('0'); setOverhead('0'); setLines([]); }
    } catch (e) { alert(errorMessage(e)); }
  };

  const saveBom = async () => {
    setErr('');
    try {
      await api.put(`/businesses/${businessId}/production/boms/${bomItemId}`, {
        labourCostPerUnit: parseFloat(labour) || 0,
        overheadCostPerUnit: parseFloat(overhead) || 0,
        lines: lines.filter(l => l.componentItemId).map(l => ({
          componentItemId: l.componentItemId, quantityPer: parseFloat(l.quantityPer) || 0,
        })),
      });
      loadBom(bomItemId);
      alert('Bill of materials saved.');
    } catch (e) { setErr(errorMessage(e)); }
  };

  const act = async (fn: () => Promise<unknown>) => {
    try { await fn(); reloadOrders(); } catch (e) { alert(errorMessage(e)); }
  };

  const createOrder = () => act(() => api.post(`/businesses/${businessId}/production/orders`, {
    itemId: orderItemId, quantity: parseFloat(orderQty) || 0, notes: null,
  })).then(() => { setOrderQty(''); });

  const complete = (o: any) => {
    const qty = prompt(`Quantity completed for ${o.number}?`, String(o.quantityPlanned));
    if (!qty) return;
    act(() => api.post(`/businesses/${businessId}/production/orders/${o.id}/complete`,
      { date: today(), quantityCompleted: parseFloat(qty) || 0 }));
  };

  return (
    <div>
      <h1>Production</h1>
      <div className="sub">
        A bill of materials is the recipe; an order consumes it. Issuing materials moves stock value
        into work in progress, completing moves it into the finished item at full cost.
      </div>

      <h2>Bill of materials</h2>
      <div className="row" style={{ alignItems: 'flex-end', maxWidth: 620 }}>
        <Field label="Finished item">
          <select value={bomItemId} onChange={e => loadBom(e.target.value)}>
            <option value="">— choose —</option>
            {finished.map((i: any) => <option key={i.id} value={i.id}>{i.code} {i.name}</option>)}
          </select></Field>
      </div>

      {bomItemId && (
        <div className="card" style={{ marginTop: 12, maxWidth: 760 }}>
          {!bom?.exists && <div className="sub">No bill of materials yet — add components below.</div>}
          <table>
            <thead><tr><th>Component</th><th className="num">Quantity per unit</th><th /></tr></thead>
            <tbody>
              {lines.map((l, i) => (
                <tr key={i}>
                  <td><select value={l.componentItemId} onChange={e => setLines(ls =>
                    ls.map((x, idx) => idx === i ? { ...x, componentItemId: e.target.value } : x))}>
                    <option value="">— component —</option>
                    {tracked.map((t: any) => <option key={t.id} value={t.id}>{t.code} {t.name}</option>)}
                  </select></td>
                  <td><input style={{ textAlign: 'right' }} value={l.quantityPer}
                    onChange={e => setLines(ls => ls.map((x, idx) =>
                      idx === i ? { ...x, quantityPer: e.target.value } : x))} /></td>
                  <td><button className="btn ghost"
                    onClick={() => setLines(ls => ls.filter((_, idx) => idx !== i))}>Remove</button></td>
                </tr>
              ))}
            </tbody>
          </table>
          <div className="row" style={{ marginTop: 10 }}>
            <button className="btn ghost" onClick={() =>
              setLines(ls => [...ls, { componentItemId: '', quantityPer: '1' }])}>+ component</button>
            <Field label="Labour per unit"><input value={labour} onChange={e => setLabour(e.target.value)} /></Field>
            <Field label="Overhead per unit"><input value={overhead} onChange={e => setOverhead(e.target.value)} /></Field>
          </div>
          {err && <div className="err">{err}</div>}
          <button className="btn" style={{ marginTop: 12 }} onClick={saveBom}>Save bill of materials</button>
        </div>
      )}

      <h2>Production orders</h2>
      <div className="row" style={{ alignItems: 'flex-end', maxWidth: 620 }}>
        <Field label="Item">
          <select value={orderItemId} onChange={e => setOrderItemId(e.target.value)}>
            <option value="">— choose —</option>
            {finished.map((i: any) => <option key={i.id} value={i.id}>{i.code} {i.name}</option>)}
          </select></Field>
        <Field label="Quantity"><input value={orderQty} onChange={e => setOrderQty(e.target.value)} /></Field>
        <button className="btn" disabled={!orderItemId || !orderQty} onClick={createOrder}>Create order</button>
      </div>

      <table style={{ marginTop: 14 }}>
        <thead><tr><th>Number</th><th>Item</th><th className="num">Planned</th><th className="num">Completed</th>
          <th>Status</th><th className="num">Material</th><th className="num">Total cost</th><th /></tr></thead>
        <tbody>
          {(orders ?? []).map((o: any) => (
            <tr key={o.id}>
              <td>{o.number}</td><td>{o.itemCode} {o.itemName}</td>
              <td className="num">{o.quantityPlanned}</td>
              <td className="num">{o.quantityCompleted}</td>
              <td><span className={`badge${o.status === 2 ? ' posted' : ''}`}>{STATUS[o.status] ?? o.status}</span></td>
              <td className="num">{gbp(o.materialCost)}</td>
              <td className="num"><strong>{gbp(o.totalCost)}</strong></td>
              <td>
                <div className="row" style={{ gap: 6 }}>
                  {o.status === 0 && <button className="btn ghost" onClick={() => act(() =>
                    api.post(`/businesses/${businessId}/production/orders/${o.id}/issue-materials`, {},
                      { params: { date: today() } }))}>Issue materials</button>}
                  {o.status === 1 && <button className="btn ghost" onClick={() => complete(o)}>Complete</button>}
                </div>
              </td>
            </tr>
          ))}
          {(orders ?? []).length === 0 && <tr><td colSpan={8} className="sub">No production orders.</td></tr>}
        </tbody>
      </table>
    </div>
  );
}
