import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, errorMessage, gbp, today } from '../api';
import { Chips, Field, VAT_OPTIONS, useLoad } from '../components';

export default function CreditNotes() {
  const { businessId } = useParams();
  const [kind, setKind] = useState<'sales' | 'purchase'>('sales');
  const [items, reload] = useLoad<any[]>(`/businesses/${businessId}/${kind}-credit-notes?page=1&pageSize=100`, [kind]);
  const [contacts] = useLoad<any[]>(`/businesses/${businessId}/${kind === 'sales' ? 'customers' : 'vendors'}`, [kind]);
  const [accounts] = useLoad<any[]>(`/businesses/${businessId}/accounts`);
  const [invoices] = useLoad<any[]>(`/businesses/${businessId}/${kind}-invoices?page=1&pageSize=200`, [kind]);
  const [showForm, setShowForm] = useState(false);
  const [contactId, setContactId] = useState('');
  const [number, setNumber] = useState('');
  const [date, setDate] = useState(today());
  const [desc, setDesc] = useState('');
  const [amount, setAmount] = useState('');
  const [vatRate, setVatRate] = useState(0);
  const [accountId, setAccountId] = useState('');
  const [err, setErr] = useState('');
  const [allocating, setAllocating] = useState<any>(null);
  const [allocInvoice, setAllocInvoice] = useState('');
  const [allocAmount, setAllocAmount] = useState('');

  const analysisAccounts = (accounts ?? []).filter((a: any) =>
    kind === 'sales' ? a.type === 3 : a.type === 4 || a.type === 0);

  const save = async () => {
    setErr('');
    try {
      const r = await api.post(`/businesses/${businessId}/${kind}-credit-notes`, {
        contactId, number, date, dueDate: date, reference: null, notes: null,
        lines: [{ itemId: null, description: desc || 'Credit', quantity: 1,
          unitPrice: parseFloat(amount) || 0, vatRate, accountId }],
      });
      await api.post(`/businesses/${businessId}/${kind}-credit-notes/${r.data.id}/post`);
      setShowForm(false); setNumber(''); setAmount('');
      reload();
    } catch (e) { setErr(errorMessage(e)); }
  };

  const openInvoices = (cn: any) => (invoices ?? []).filter((i: any) =>
    (i.status === 1 || i.status === 'Posted') && i.contact === cn.contact
    && i.grossTotal - i.amountPaid > 0.004);

  const allocate = async () => {
    try {
      const r = await api.post(`/businesses/${businessId}/${kind}-credit-notes/${allocating.id}/allocate`,
        { invoiceId: allocInvoice, amount: parseFloat(allocAmount) || 0 });
      alert(`Allocated. Credit remaining ${gbp(r.data.creditNoteRemaining ?? 0)}; invoice outstanding ${gbp(r.data.invoiceOutstanding ?? 0)}.`);
      setAllocating(null); reload();
    } catch (e) { alert(errorMessage(e)); }
  };

  return (
    <div>
      <div className="row">
        <h1>Credit notes</h1><div className="spacer" />
        <Chips options={[['sales', 'Sales'], ['purchase', 'Purchase']]} value={kind}
          onChange={k => { setKind(k); setShowForm(false); setAllocating(null); }} />
        <button className="btn" onClick={() => setShowForm(s => !s)}>{showForm ? 'Close' : 'New credit note'}</button>
      </div>
      <div className="sub">The mirror of an invoice. Allocate against invoices (a journal-less contra) or refund from Money.</div>

      {showForm && (
        <div className="card" style={{ marginBottom: 18, maxWidth: 680 }}>
          <div className="row">
            <Field label={kind === 'sales' ? 'Customer' : 'Supplier'}>
              <select value={contactId} onChange={e => setContactId(e.target.value)}>
                <option value="">— choose —</option>
                {(contacts ?? []).map((c: any) => <option key={c.id} value={c.id}>{c.name}</option>)}
              </select></Field>
            <Field label="Number"><input value={number} onChange={e => setNumber(e.target.value)} placeholder="CN-001" /></Field>
            <Field label="Date"><input type="date" value={date} onChange={e => setDate(e.target.value)} /></Field>
          </div>
          <Field label="What's being credited"><input value={desc} onChange={e => setDesc(e.target.value)} /></Field>
          <div className="row">
            <Field label="Amount (net)"><input value={amount} onChange={e => setAmount(e.target.value)} /></Field>
            <Field label="VAT"><select value={vatRate} onChange={e => setVatRate(parseInt(e.target.value))}>
              {VAT_OPTIONS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select></Field>
            <Field label="Account"><select value={accountId} onChange={e => setAccountId(e.target.value)}>
              <option value="">— account —</option>
              {analysisAccounts.map((a: any) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}
            </select></Field>
          </div>
          {err && <div className="err">{err}</div>}
          <button className="btn" style={{ marginTop: 12 }}
            disabled={!contactId || !number || !accountId} onClick={save}>Create &amp; post</button>
        </div>
      )}

      {allocating && (
        <div className="card" style={{ marginBottom: 18, maxWidth: 560 }}>
          <h2 style={{ marginTop: 0 }}>Allocate {allocating.number}</h2>
          <div className="sub">Unapplied {gbp(allocating.grossTotal - allocating.amountPaid)} · same-contact posted invoices only</div>
          <Field label="Invoice">
            <select value={allocInvoice} onChange={e => {
              setAllocInvoice(e.target.value);
              const inv = openInvoices(allocating).find((i: any) => i.id === e.target.value);
              if (inv) setAllocAmount(Math.min(allocating.grossTotal - allocating.amountPaid,
                inv.grossTotal - inv.amountPaid).toFixed(2));
            }}>
              <option value="">— choose —</option>
              {openInvoices(allocating).map((i: any) =>
                <option key={i.id} value={i.id}>{i.number} (outstanding {gbp(i.grossTotal - i.amountPaid)})</option>)}
            </select></Field>
          <Field label="Amount"><input value={allocAmount} onChange={e => setAllocAmount(e.target.value)} /></Field>
          <div className="row" style={{ marginTop: 12 }}>
            <button className="btn" disabled={!allocInvoice} onClick={allocate}>Allocate</button>
            <button className="btn ghost" onClick={() => setAllocating(null)}>Cancel</button>
          </div>
        </div>
      )}

      <table>
        <thead><tr><th>Number</th><th>Contact</th><th>Date</th>
          <th className="num">Gross</th><th className="num">Unapplied</th><th>Status</th><th /></tr></thead>
        <tbody>
          {(items ?? []).map((c: any) => {
            const posted = c.status === 1 || c.status === 'Posted';
            const unapplied = c.grossTotal - c.amountPaid;
            return (
              <tr key={c.id}>
                <td>{c.number}</td><td>{c.contact}</td><td>{c.date}</td>
                <td className="num">{gbp(c.grossTotal)}</td>
                <td className="num">{gbp(unapplied)}</td>
                <td><span className={`badge${posted ? ' posted' : ''}`}>
                  {typeof c.status === 'number' ? ['Draft', 'Posted'][c.status] : c.status}</span></td>
                <td>{posted && unapplied > 0.004 &&
                  <button className="btn ghost" onClick={() => { setAllocating(c); setAllocInvoice(''); }}>Allocate</button>}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
