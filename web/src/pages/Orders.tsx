import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, errorMessage, gbp, today } from '../api';
import { Chips, Field, VAT_OPTIONS, useLoad, vatPct } from '../components';

const QUOTE_STATUS = ['Draft', 'Sent', 'Accepted', 'Declined', 'Invoiced', 'Expired'];
const PO_STATUS = ['Draft', 'Sent', 'Received', 'Invoiced', 'Cancelled'];
type Line = { description: string; quantity: string; unitPrice: string; vatRate: number; accountId: string };
const blank = (): Line => ({ description: '', quantity: '1', unitPrice: '', vatRate: 0, accountId: '' });

/** Quotes and purchase orders — commitments, not transactions. Nothing posts until conversion. */
export default function Orders() {
  const { businessId } = useParams();
  const [kind, setKind] = useState<'quotes' | 'purchase-orders'>('quotes');
  const [items, reload] = useLoad<any[]>(`/businesses/${businessId}/${kind}`, [kind]);
  const [contacts] = useLoad<any[]>(
    `/businesses/${businessId}/${kind === 'quotes' ? 'customers' : 'vendors'}`, [kind]);
  const [accounts] = useLoad<any[]>(`/businesses/${businessId}/accounts`);
  const [showForm, setShowForm] = useState(false);
  const [contactId, setContactId] = useState('');
  const [number, setNumber] = useState('');
  const [date, setDate] = useState(today());
  const [secondDate, setSecondDate] = useState('');
  const [lines, setLines] = useState<Line[]>([blank()]);
  const [err, setErr] = useState('');

  const isQuote = kind === 'quotes';
  const analysis = (accounts ?? []).filter((a: any) =>
    isQuote ? a.type === 3 : a.type === 4 || a.type === 0);
  const totals = lines.reduce((t, l) => {
    const net = (parseFloat(l.quantity) || 0) * (parseFloat(l.unitPrice) || 0);
    return { net: t.net + net, vat: t.vat + Math.round(net * vatPct(l.vatRate) * 100) / 100 };
  }, { net: 0, vat: 0 });
  const set = (i: number, patch: Partial<Line>) =>
    setLines(ls => ls.map((l, idx) => idx === i ? { ...l, ...patch } : l));

  const act = async (fn: () => Promise<unknown>) => {
    try { await fn(); reload(); } catch (e) { alert(errorMessage(e)); }
  };

  const save = async () => {
    setErr('');
    const payload: Record<string, unknown> = {
      number, date, reference: null, notes: null,
      lines: lines.filter(l => l.accountId && l.unitPrice).map(l => ({
        itemId: null, description: l.description, quantity: parseFloat(l.quantity) || 0,
        unitPrice: parseFloat(l.unitPrice) || 0, vatRate: l.vatRate, accountId: l.accountId,
      })),
    };
    if (isQuote) {
      payload.customerId = contactId;
      payload.expiryDate = secondDate || date;
    } else {
      payload.vendorId = contactId;
      payload.expectedDate = secondDate || null;
    }
    try {
      await api.post(`/businesses/${businessId}/${kind}`, payload);
      setShowForm(false); setNumber(''); setLines([blank()]);
      reload();
    } catch (e) { setErr(errorMessage(e)); }
  };

  const convert = (row: any) => {
    const invoiceNumber = prompt(
      isQuote ? 'Invoice number for this quote?' : 'Supplier invoice number?',
      isQuote ? `INV-${row.number.replace(/\D/g, '')}` : '');
    if (!invoiceNumber) return;
    act(async () => {
      const r = await api.post(`/businesses/${businessId}/${kind}/${row.id}/convert`,
        { invoiceNumber, invoiceDate: today() });
      alert(`Draft invoice ${r.data.number} created for ${gbp(r.data.grossTotal)}. `
        + 'Check it against what was actually delivered, then post it.');
    });
  };

  const STATUS = isQuote ? QUOTE_STATUS : PO_STATUS;
  const convertedValue = isQuote ? 4 : 3;

  return (
    <div>
      <div className="row">
        <h1>{isQuote ? 'Quotes' : 'Purchase orders'}</h1><div className="spacer" />
        <Chips options={[['quotes', 'Quotes'], ['purchase-orders', 'Purchase orders']]}
          value={kind} onChange={k => { setKind(k); setShowForm(false); setContactId(''); }} />
        <button className="btn" onClick={() => setShowForm(s => !s)}>{showForm ? 'Close' : 'New'}</button>
      </div>
      <div className="sub">
        {isQuote
          ? 'A quote is an offer, so nothing is owed and nothing posts. Converting produces a draft invoice.'
          : 'An order commits you to buy but creates no liability — the creditor arises when the supplier invoices.'}
      </div>

      {showForm && (
        <div className="card" style={{ marginBottom: 18 }}>
          <div className="row">
            <Field label={isQuote ? 'Customer' : 'Supplier'}>
              <select value={contactId} onChange={e => setContactId(e.target.value)}>
                <option value="">— choose —</option>
                {(contacts ?? []).map((c: any) => <option key={c.id} value={c.id}>{c.name}</option>)}
              </select></Field>
            <Field label="Number"><input value={number} onChange={e => setNumber(e.target.value)}
              placeholder={isQuote ? 'QU-001' : 'PO-001'} /></Field>
            <Field label="Date"><input type="date" value={date}
              onChange={e => setDate(e.target.value)} /></Field>
            <Field label={isQuote ? 'Valid until' : 'Expected'}>
              <input type="date" value={secondDate} onChange={e => setSecondDate(e.target.value)} /></Field>
          </div>
          <table style={{ marginTop: 12 }}>
            <thead><tr><th>Description</th><th className="num">Qty</th><th className="num">Unit</th>
              <th>VAT</th><th>Account</th></tr></thead>
            <tbody>
              {lines.map((l, i) => (
                <tr key={i}>
                  <td><input value={l.description} onChange={e => set(i, { description: e.target.value })} /></td>
                  <td><input style={{ textAlign: 'right' }} value={l.quantity}
                    onChange={e => set(i, { quantity: e.target.value })} /></td>
                  <td><input style={{ textAlign: 'right' }} value={l.unitPrice}
                    onChange={e => set(i, { unitPrice: e.target.value })} /></td>
                  <td><select value={l.vatRate} onChange={e => set(i, { vatRate: parseInt(e.target.value) })}>
                    {VAT_OPTIONS.map(([v, lab]) => <option key={v} value={v}>{lab}</option>)}</select></td>
                  <td><select value={l.accountId} onChange={e => set(i, { accountId: e.target.value })}>
                    <option value="">— account —</option>
                    {analysis.map((a: any) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}
                  </select></td>
                </tr>
              ))}
            </tbody>
          </table>
          <div className="row" style={{ marginTop: 10 }}>
            <button className="btn ghost" onClick={() => setLines(ls => [...ls, blank()])}>+ line</button>
            <div className="spacer" />
            <div>Net {gbp(totals.net)} · VAT {gbp(totals.vat)} · <strong>{gbp(totals.net + totals.vat)}</strong></div>
          </div>
          {err && <div className="err">{err}</div>}
          <button className="btn" style={{ marginTop: 12 }}
            disabled={!contactId || !number} onClick={save}>Save</button>
        </div>
      )}

      <table>
        <thead><tr><th>Number</th><th>{isQuote ? 'Customer' : 'Supplier'}</th><th>Date</th>
          <th>{isQuote ? 'Valid until' : 'Expected'}</th>
          <th className="num">Value</th><th>Status</th><th /></tr></thead>
        <tbody>
          {(items ?? []).map((r: any) => (
            <tr key={r.id}>
              <td>{r.number}</td><td>{r.contact}</td><td>{r.date}</td>
              <td>{isQuote ? r.expiryDate : (r.expectedDate ?? '—')}</td>
              <td className="num">{gbp(r.grossTotal)}</td>
              <td><span className={`badge${r.status === convertedValue ? ' posted' : ''}`}>
                {STATUS[r.status] ?? r.status}</span></td>
              <td>
                <div className="row" style={{ gap: 6 }}>
                  {r.status !== convertedValue && (
                    <>
                      <select defaultValue="" onChange={e => {
                        if (!e.target.value) return;
                        act(() => api.post(`/businesses/${businessId}/${kind}/${r.id}/status`, {},
                          { params: { status: parseInt(e.target.value) } }));
                      }}>
                        <option value="">set status…</option>
                        {STATUS.map((label, v) => v !== convertedValue &&
                          <option key={v} value={v}>{label}</option>)}
                      </select>
                      <button className="btn ghost" onClick={() => convert(r)}>Invoice it</button>
                    </>
                  )}
                </div>
              </td>
            </tr>
          ))}
          {(items ?? []).length === 0 &&
            <tr><td colSpan={7} className="sub">Nothing here yet.</td></tr>}
        </tbody>
      </table>
    </div>
  );
}
