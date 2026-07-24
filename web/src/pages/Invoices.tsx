import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, downloadFile, errorMessage, gbp, today } from '../api';
import { Chips, Field, VAT_OPTIONS, useLoad, vatPct } from '../components';

type Line = { description: string; quantity: string; unitPrice: string; vatRate: number; accountId: string };
const blank = (): Line => ({ description: '', quantity: '1', unitPrice: '', vatRate: 0, accountId: '' });

export default function Invoices() {
  const { businessId } = useParams();
  const [kind, setKind] = useState<'sales' | 'purchase'>('sales');
  const [items, reload] = useLoad<any[]>(`/businesses/${businessId}/${kind}-invoices?page=1&pageSize=100`, [kind]);
  const [contacts] = useLoad<any[]>(`/businesses/${businessId}/${kind === 'sales' ? 'customers' : 'vendors'}`, [kind]);
  const [accounts] = useLoad<any[]>(`/businesses/${businessId}/accounts`);
  const [showForm, setShowForm] = useState(false);
  const [contactId, setContactId] = useState('');
  const [number, setNumber] = useState('');
  const [date, setDate] = useState(today());
  const [due, setDue] = useState('');
  const [lines, setLines] = useState<Line[]>([blank()]);
  const [err, setErr] = useState('');

  const analysisAccounts = (accounts ?? []).filter((a: any) =>
    kind === 'sales' ? a.type === 3 : a.type === 4 || a.type === 0);
  const totals = lines.reduce((t, l) => {
    const net = (parseFloat(l.quantity) || 0) * (parseFloat(l.unitPrice) || 0);
    return { net: t.net + net, vat: t.vat + Math.round(net * vatPct(l.vatRate) * 100) / 100 };
  }, { net: 0, vat: 0 });
  const set = (i: number, patch: Partial<Line>) =>
    setLines(ls => ls.map((l, idx) => idx === i ? { ...l, ...patch } : l));

  const save = async (post: boolean) => {
    setErr('');
    try {
      const r = await api.post(`/businesses/${businessId}/${kind}-invoices`, {
        contactId, number, date, dueDate: due || date, reference: null, notes: null,
        lines: lines.filter(l => l.accountId && l.unitPrice).map(l => ({
          itemId: null, description: l.description, quantity: parseFloat(l.quantity) || 0,
          unitPrice: parseFloat(l.unitPrice) || 0, vatRate: l.vatRate, accountId: l.accountId,
        })),
      });
      if (post) await api.post(`/businesses/${businessId}/${kind}-invoices/${r.data.id}/post`);
      setShowForm(false); setNumber(''); setLines([blank()]);
      reload();
    } catch (e) { setErr(errorMessage(e)); }
  };

  const act = async (fn: () => Promise<unknown>) => {
    try { await fn(); reload(); } catch (e) { alert(errorMessage(e)); }
  };

  return (
    <div>
      <div className="row">
        <h1>Invoices</h1><div className="spacer" />
        <Chips options={[['sales', 'Sales'], ['purchase', 'Purchase']]} value={kind}
          onChange={k => { setKind(k); setShowForm(false); }} />
        <button className="btn" onClick={() => setShowForm(s => !s)}>{showForm ? 'Close' : 'New invoice'}</button>
      </div>
      <div className="sub">Posting writes the double entry and locks the document; correct by credit note or reversal.</div>

      {showForm && (
        <div className="card" style={{ marginBottom: 18 }}>
          <div className="row">
            <Field label={kind === 'sales' ? 'Customer' : 'Supplier'}>
              <select value={contactId} onChange={e => setContactId(e.target.value)}>
                <option value="">— choose —</option>
                {(contacts ?? []).map((c: any) => <option key={c.id} value={c.id}>{c.name}</option>)}
              </select></Field>
            <Field label="Number"><input value={number} onChange={e => setNumber(e.target.value)} placeholder="INV-0001" /></Field>
            <Field label="Date"><input type="date" value={date} onChange={e => setDate(e.target.value)} /></Field>
            <Field label="Due"><input type="date" value={due} onChange={e => setDue(e.target.value)} /></Field>
          </div>
          <table style={{ marginTop: 12 }}>
            <thead><tr><th>Description</th><th className="num">Qty</th><th className="num">Unit (net)</th>
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
                    {VAT_OPTIONS.map(([v, lab]) => <option key={v} value={v}>{lab}</option>)}
                  </select></td>
                  <td><select value={l.accountId} onChange={e => set(i, { accountId: e.target.value })}>
                    <option value="">— account —</option>
                    {analysisAccounts.map((a: any) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}
                  </select></td>
                </tr>
              ))}
            </tbody>
          </table>
          <div className="row" style={{ marginTop: 10 }}>
            <button className="btn ghost" onClick={() => setLines(ls => [...ls, blank()])}>+ line</button>
            <div className="spacer" />
            <div>Net {gbp(totals.net)} · VAT {gbp(totals.vat)} · <strong>Gross {gbp(totals.net + totals.vat)}</strong></div>
          </div>
          {err && <div className="err">{err}</div>}
          <div className="row" style={{ marginTop: 12 }}>
            <button className="btn" disabled={!contactId || !number} onClick={() => save(true)}>Save &amp; post</button>
            <button className="btn ghost" disabled={!contactId || !number} onClick={() => save(false)}>Save draft</button>
          </div>
        </div>
      )}

      <table>
        <thead><tr><th>Number</th><th>Contact</th><th>Date</th><th>Due</th>
          <th className="num">Gross</th><th className="num">Outstanding</th><th>Status</th><th /></tr></thead>
        <tbody>
          {(items ?? []).map((i: any) => {
            const posted = i.status === 1 || i.status === 'Posted';
            return (
              <tr key={i.id}>
                <td>{i.number}</td><td>{i.contact}</td><td>{i.date}</td><td>{i.dueDate}</td>
                <td className="num">{gbp(i.grossTotal)}</td>
                <td className="num">{gbp(i.grossTotal - i.amountPaid)}</td>
                <td><span className={`badge${posted ? ' posted' : ''}`}>
                  {typeof i.status === 'number' ? ['Draft', 'Posted', 'Void'][i.status] : i.status}</span></td>
                <td>
                  <div className="row" style={{ gap: 6 }}>
                    {!posted && <button className="btn ghost"
                      onClick={() => act(() => api.post(`/businesses/${businessId}/${kind}-invoices/${i.id}/post`))}>Post</button>}
                    {posted && kind === 'sales' && <>
                      <button className="btn ghost"
                        onClick={() => downloadFile(`/businesses/${businessId}/sales-invoices/${i.id}/pdf`, `${i.number}.pdf`)}>PDF</button>
                      <button className="btn ghost"
                        onClick={() => act(async () => {
                          const r = await api.post(`/businesses/${businessId}/sales-invoices/${i.id}/email`, {});
                          alert(`Emailed to ${r.data.sentTo}`);
                        })}>Email</button>
                    </>}
                  </div>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
