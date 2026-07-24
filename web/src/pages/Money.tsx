import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, errorMessage, gbp, today } from '../api';
import { Chips, Field, useLoad } from '../components';

/** Money in/out: settle invoices, refund credit notes, or post direct to an account. */
export default function Money() {
  const { businessId } = useParams();
  const [items, reload] = useLoad<any[]>(`/businesses/${businessId}/money?page=1&pageSize=100`);
  const [accounts] = useLoad<any[]>(`/businesses/${businessId}/accounts`);
  const [salesInv] = useLoad<any[]>(`/businesses/${businessId}/sales-invoices?page=1&pageSize=200`);
  const [purchInv] = useLoad<any[]>(`/businesses/${businessId}/purchase-invoices?page=1&pageSize=200`);
  const [salesCn] = useLoad<any[]>(`/businesses/${businessId}/sales-credit-notes?page=1&pageSize=100`);
  const [purchCn] = useLoad<any[]>(`/businesses/${businessId}/purchase-credit-notes?page=1&pageSize=100`);

  const [showForm, setShowForm] = useState(false);
  const [direction, setDirection] = useState<number>(0);
  const [mode, setMode] = useState<string>('invoice');
  const [date, setDate] = useState(today());
  const [reference, setReference] = useState('');
  const [amount, setAmount] = useState('');
  const [bankId, setBankId] = useState('');
  const [targetId, setTargetId] = useState('');
  const [err, setErr] = useState('');

  const banks = (accounts ?? []).filter((a: any) => a.isBank);
  const open = (list: any[] | null) => (list ?? []).filter((d: any) =>
    (d.status === 1 || d.status === 'Posted') && d.grossTotal - d.amountPaid > 0.004);

  // Target list depends on direction + mode:
  //   In  + invoice → open sales invoices;    In  + refund → purchase CNs (supplier refunds us)
  //   Out + invoice → open purchase invoices; Out + refund → sales CNs (we refund a customer)
  const targets =
    mode === 'direct' ? (accounts ?? []).filter((a: any) => !a.isBank)
    : mode === 'invoice' ? open(direction === 0 ? salesInv : purchInv)
    : open(direction === 0 ? purchCn : salesCn);

  const save = async () => {
    setErr('');
    const body: Record<string, unknown> = {
      direction, date, reference, amount: parseFloat(amount) || 0, bankAccountId: bankId,
      customerId: null, vendorId: null, salesInvoiceId: null, purchaseInvoiceId: null,
      directAccountId: null, notes: null, salesCreditNoteId: null, purchaseCreditNoteId: null,
    };
    if (mode === 'direct') body.directAccountId = targetId;
    else if (mode === 'invoice') body[direction === 0 ? 'salesInvoiceId' : 'purchaseInvoiceId'] = targetId;
    else body[direction === 0 ? 'purchaseCreditNoteId' : 'salesCreditNoteId'] = targetId;
    try {
      const r = await api.post(`/businesses/${businessId}/money`, body);
      await api.post(`/businesses/${businessId}/money/${r.data.id}/post`);
      setShowForm(false); setAmount(''); setReference(''); setTargetId('');
      reload();
    } catch (e) { setErr(errorMessage(e)); }
  };

  return (
    <div>
      <div className="row"><h1>Money</h1><div className="spacer" />
        <button className="btn" onClick={() => setShowForm(s => !s)}>{showForm ? 'Close' : 'Record money'}</button></div>
      <div className="sub">Receipts and payments, posted straight to the bank and the right control account.</div>

      {showForm && (
        <div className="card" style={{ marginBottom: 18, maxWidth: 680 }}>
          <Chips options={[[0, 'Money in'], [1, 'Money out']]} value={direction}
            onChange={d => { setDirection(d); setTargetId(''); }} />
          <div style={{ marginTop: 10 }}>
            <Chips options={[['invoice', 'Settle an invoice'], ['refund', 'Credit note refund'], ['direct', 'Direct to account']]}
              value={mode} onChange={m => { setMode(m); setTargetId(''); }} />
          </div>
          <div className="row" style={{ marginTop: 8 }}>
            <Field label="Date"><input type="date" value={date} onChange={e => setDate(e.target.value)} /></Field>
            <Field label="Reference"><input value={reference} onChange={e => setReference(e.target.value)} placeholder="BACS / chq" /></Field>
            <Field label="Amount"><input value={amount} onChange={e => setAmount(e.target.value)} /></Field>
          </div>
          <div className="row">
            <Field label="Bank account"><select value={bankId} onChange={e => setBankId(e.target.value)}>
              <option value="">— bank —</option>
              {banks.map((a: any) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}
            </select></Field>
            <Field label={mode === 'direct' ? 'Post to account' : mode === 'invoice' ? 'Invoice' : 'Credit note'}>
              <select value={targetId} onChange={e => setTargetId(e.target.value)}>
                <option value="">— choose —</option>
                {targets.map((t: any) => (
                  <option key={t.id} value={t.id}>
                    {t.code ? `${t.code} ${t.name}` : `${t.number} · ${t.contact} · ${gbp(t.grossTotal - t.amountPaid)} open`}
                  </option>
                ))}
              </select></Field>
          </div>
          {err && <div className="err">{err}</div>}
          <button className="btn" style={{ marginTop: 12 }}
            disabled={!bankId || !targetId || !amount} onClick={save}>Record &amp; post</button>
        </div>
      )}

      <table>
        <thead><tr><th>Date</th><th>Reference</th><th>Direction</th><th className="num">Amount</th><th>Status</th></tr></thead>
        <tbody>
          {(items ?? []).map((t: any) => {
            const isIn = t.direction === 0 || t.direction === 'In';
            return (
              <tr key={t.id}>
                <td>{t.date}</td><td>{t.reference}</td><td>{isIn ? 'In' : 'Out'}</td>
                <td className={`num ${isIn ? 'cr' : 'dr'}`}>{gbp(t.amount)}</td>
                <td><span className={`badge${t.status === 1 || t.status === 'Posted' ? ' posted' : ''}`}>
                  {typeof t.status === 'number' ? ['Draft', 'Posted'][t.status] : t.status}</span></td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
