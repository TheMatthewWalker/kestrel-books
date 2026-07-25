import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, errorMessage, gbp, today } from '../api';
import { Field, useLoad } from '../components';

const MOVEMENT = ['Receipt', 'Issue', 'Adjustment', 'Write-off', 'Production', 'Opening'];

/** Stock levels, adjustments and the movement history behind each valuation. */
export default function Inventory() {
  const { businessId } = useParams();
  const [levels, reload] = useLoad<any[]>(`/businesses/${businessId}/inventory/levels`);
  const [drill, setDrill] = useState<any>(null);
  const [movements, setMovements] = useState<any[]>([]);
  const [adjusting, setAdjusting] = useState<any>(null);
  const [date, setDate] = useState(today());
  const [quantity, setQuantity] = useState('');
  const [unitCost, setUnitCost] = useState('');
  const [reason, setReason] = useState('');
  const [err, setErr] = useState('');

  const openMovements = async (item: any) => {
    try {
      const r = await api.get(`/businesses/${businessId}/inventory/movements/${item.id}`);
      setMovements(r.data); setDrill(item);
    } catch (e) { alert(errorMessage(e)); }
  };

  const adjust = async () => {
    setErr('');
    try {
      await api.post(`/businesses/${businessId}/inventory/adjust`, {
        itemId: adjusting.id, date, quantity: parseFloat(quantity) || 0,
        unitCost: unitCost ? parseFloat(unitCost) : null, reason,
      });
      setAdjusting(null); setQuantity(''); setUnitCost(''); setReason('');
      reload();
    } catch (e) { setErr(errorMessage(e)); }
  };

  const totalValue = (levels ?? []).reduce((s: number, l: any) => s + l.value, 0);

  if (drill) {
    return (
      <div>
        <div className="row"><h1>{drill.code} — {drill.name}</h1><div className="spacer" />
          <button className="btn" onClick={() => setDrill(null)}>Back</button></div>
        <div className="sub">
          {drill.quantityOnHand} on hand at {gbp(drill.avgUnitCost)} average = {gbp(drill.value)}
        </div>
        <table>
          <thead><tr><th>Date</th><th>Type</th><th className="num">Quantity</th>
            <th className="num">Unit cost</th><th className="num">Value</th><th>Note</th></tr></thead>
          <tbody>
            {movements.map((m: any) => (
              <tr key={m.id}>
                <td>{m.date}</td>
                <td>{MOVEMENT[m.type] ?? m.type}</td>
                <td className={`num ${m.quantity >= 0 ? 'cr' : 'dr'}`}>{m.quantity}</td>
                <td className="num">{gbp(m.unitCost)}</td>
                <td className="num">{gbp(m.quantity * m.unitCost)}</td>
                <td>{m.reason ?? m.note ?? ''}</td>
              </tr>
            ))}
            {movements.length === 0 && <tr><td colSpan={6} className="sub">No movements.</td></tr>}
          </tbody>
        </table>
      </div>
    );
  }

  return (
    <div>
      <h1>Inventory</h1>
      <div className="sub">
        Weighted average cost. Receipts re-average, issues leave at the average — so the
        balance sheet value and cost of sales always agree with the ledger.
      </div>

      <div className="cards">
        <div className="card"><div className="label">Tracked items</div>
          <div className="value">{(levels ?? []).length}</div></div>
        <div className="card"><div className="label">Stock value</div>
          <div className="value">{gbp(totalValue)}</div></div>
      </div>

      {adjusting && (
        <div className="card" style={{ marginTop: 16, maxWidth: 620 }}>
          <h2 style={{ marginTop: 0 }}>Adjust {adjusting.code}</h2>
          <div className="sub">
            Positive quantity brings stock in (give a unit cost), negative writes it off at the current average.
          </div>
          <div className="row">
            <Field label="Date"><input type="date" value={date} onChange={e => setDate(e.target.value)} /></Field>
            <Field label="Quantity (+/−)"><input value={quantity} onChange={e => setQuantity(e.target.value)} /></Field>
            <Field label="Unit cost (in only)">
              <input value={unitCost} onChange={e => setUnitCost(e.target.value)} /></Field>
          </div>
          <Field label="Reason (goes on the journal)">
            <input value={reason} onChange={e => setReason(e.target.value)} placeholder="Stocktake variance" /></Field>
          {err && <div className="err">{err}</div>}
          <div className="row" style={{ marginTop: 12 }}>
            <button className="btn" disabled={!quantity || !reason} onClick={adjust}>Post adjustment</button>
            <button className="btn ghost" onClick={() => setAdjusting(null)}>Cancel</button>
          </div>
        </div>
      )}

      <h2>Stock on hand</h2>
      <table>
        <thead><tr><th>Code</th><th>Name</th><th className="num">On hand</th>
          <th className="num">Average cost</th><th className="num">Value</th><th /></tr></thead>
        <tbody>
          {(levels ?? []).map((l: any) => (
            <tr key={l.id}>
              <td>{l.code}</td><td>{l.name}</td>
              <td className={`num${l.quantityOnHand < 0 ? ' dr' : ''}`}>{l.quantityOnHand}</td>
              <td className="num">{gbp(l.avgUnitCost)}</td>
              <td className="num"><strong>{gbp(l.value)}</strong></td>
              <td>
                <div className="row" style={{ gap: 6 }}>
                  <button className="btn ghost" onClick={() => openMovements(l)}>Movements</button>
                  <button className="btn ghost" onClick={() => setAdjusting(l)}>Adjust</button>
                </div>
              </td>
            </tr>
          ))}
          {(levels ?? []).length === 0 &&
            <tr><td colSpan={6} className="sub">No stock-tracked items — enable tracking on an item first.</td></tr>}
        </tbody>
      </table>
    </div>
  );
}
