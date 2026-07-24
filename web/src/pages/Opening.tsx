import { useRef, useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, errorMessage, gbp, today } from '../api';
import { Field, useLoad } from '../components';

/** Conversion: trial balance, then open invoices, then reconcile against the TB controls. */
export default function Opening() {
  const { businessId } = useParams();
  const [status, reload] = useLoad<any>(`/businesses/${businessId}/opening/status`);
  const [accounts] = useLoad<any[]>(`/businesses/${businessId}/accounts`);
  const [customers] = useLoad<any[]>(`/businesses/${businessId}/customers`);
  const [vendors] = useLoad<any[]>(`/businesses/${businessId}/vendors`);
  const [conversionDate, setConversionDate] = useState(today());
  const [parsed, setParsed] = useState<any>(null);
  const [err, setErr] = useState('');
  const fileRef = useRef<HTMLInputElement>(null);

  // open invoice entry
  const [invKind, setInvKind] = useState<'sales' | 'purchase'>('sales');
  const [contactId, setContactId] = useState('');
  const [number, setNumber] = useState('');
  const [invDate, setInvDate] = useState(today());
  const [dueDate, setDueDate] = useState(today());
  const [gross, setGross] = useState('');

  const parse = async () => {
    const file = fileRef.current?.files?.[0];
    if (!file) return;
    const form = new FormData();
    form.append('file', file);
    setErr('');
    try {
      const r = await api.post(`/businesses/${businessId}/opening/trial-balance/parse-csv`, form,
        { headers: { 'Content-Type': 'multipart/form-data' } });
      setParsed(r.data);
    } catch (e) { setErr(errorMessage(e)); }
  };

  const commitTb = async () => {
    if (!parsed) return;
    setErr('');
    try {
      await api.post(`/businesses/${businessId}/opening/trial-balance`, {
        conversionDate,
        lines: (parsed.matched ?? []).map((m: any) => ({
          accountId: m.accountId, debit: m.debit ?? 0, credit: m.credit ?? 0,
        })),
      });
      setParsed(null); reload();
      alert('Opening trial balance posted, dated the day before conversion.');
    } catch (e) { setErr(errorMessage(e)); }
  };

  const addOpenInvoice = async () => {
    setErr('');
    try {
      await api.post(`/businesses/${businessId}/opening/${invKind}-invoices`, {
        contactId, number, date: invDate, dueDate, gross: parseFloat(gross) || 0,
      });
      setNumber(''); setGross(''); reload();
    } catch (e) { setErr(errorMessage(e)); }
  };

  const contacts = invKind === 'sales' ? customers : vendors;
  const unmatched = parsed?.unmatched ?? [];

  return (
    <div>
      <h1>Opening balances</h1>
      <div className="sub">
        Bring the old system's closing position across: trial balance first, then the individual
        open invoices that make up the debtor and creditor controls.
      </div>

      {status && (
        <div className="cards">
          <div className="card"><div className="label">Conversion date</div>
            <div className="value" style={{ fontSize: 18 }}>{status.conversionDate ?? 'Not set'}</div></div>
          <div className="card"><div className="label">Debtors control (TB)</div>
            <div className="value" style={{ fontSize: 18 }}>{gbp(status.tbDebtors ?? 0)}</div></div>
          <div className="card"><div className="label">Open sales invoices entered</div>
            <div className={`value${Math.abs((status.enteredSales ?? 0) - (status.tbDebtors ?? 0)) > 0.004 ? ' bad' : ''}`}
              style={{ fontSize: 18 }}>{gbp(status.enteredSales ?? 0)}</div></div>
          <div className="card"><div className="label">Open purchase invoices entered</div>
            <div className={`value${Math.abs((status.enteredPurchases ?? 0) - (status.tbCreditors ?? 0)) > 0.004 ? ' bad' : ''}`}
              style={{ fontSize: 18 }}>{gbp(status.enteredPurchases ?? 0)}</div></div>
        </div>
      )}

      <h2>1 · Trial balance</h2>
      <div className="row" style={{ alignItems: 'flex-end', maxWidth: 760 }}>
        <Field label="Conversion date (first day on KestrelBooks)">
          <input type="date" value={conversionDate} onChange={e => setConversionDate(e.target.value)} /></Field>
        <Field label="TB export (CSV: code, debit, credit)"><input type="file" ref={fileRef} /></Field>
        <button className="btn ghost" onClick={parse}>Parse</button>
      </div>

      {parsed && (
        <div style={{ marginTop: 14 }}>
          <div className="sub">
            {(parsed.matched ?? []).length} matched to accounts · {unmatched.length} unmatched
            {unmatched.length > 0 && ' (unmatched rows are skipped — add the accounts first if they matter)'}
          </div>
          <table>
            <thead><tr><th>Code</th><th>Account</th><th className="num">Debit</th><th className="num">Credit</th></tr></thead>
            <tbody>
              {(parsed.matched ?? []).map((m: any, i: number) => (
                <tr key={i}><td>{m.code}</td><td>{m.name}</td>
                  <td className="num dr">{m.debit ? gbp(m.debit) : ''}</td>
                  <td className="num cr">{m.credit ? gbp(m.credit) : ''}</td></tr>
              ))}
              {unmatched.map((u: any, i: number) => (
                <tr key={`u${i}`}><td>{u.code}</td><td className="sub">not found — skipped</td>
                  <td className="num">{u.debit ? gbp(u.debit) : ''}</td>
                  <td className="num">{u.credit ? gbp(u.credit) : ''}</td></tr>
              ))}
            </tbody>
          </table>
          <button className="btn" style={{ marginTop: 12 }} onClick={commitTb}>Post opening trial balance</button>
        </div>
      )}

      <h2>2 · Open invoices</h2>
      <div className="sub">
        These carry no journal — their value is already in the TB control accounts. They exist so
        ageing, statements and settlement work from day one.
      </div>
      <div className="card" style={{ maxWidth: 760 }}>
        <div className="row">
          <Field label="Type">
            <select value={invKind} onChange={e => { setInvKind(e.target.value as 'sales' | 'purchase'); setContactId(''); }}>
              <option value="sales">Sales (debtor)</option>
              <option value="purchase">Purchase (creditor)</option>
            </select></Field>
          <Field label={invKind === 'sales' ? 'Customer' : 'Supplier'}>
            <select value={contactId} onChange={e => setContactId(e.target.value)}>
              <option value="">— choose —</option>
              {(contacts ?? []).map((c: any) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select></Field>
          <Field label="Number"><input value={number} onChange={e => setNumber(e.target.value)} /></Field>
        </div>
        <div className="row">
          <Field label="Date"><input type="date" value={invDate} onChange={e => setInvDate(e.target.value)} /></Field>
          <Field label="Due"><input type="date" value={dueDate} onChange={e => setDueDate(e.target.value)} /></Field>
          <Field label="Gross outstanding"><input value={gross} onChange={e => setGross(e.target.value)} /></Field>
          <button className="btn" style={{ alignSelf: 'end' }}
            disabled={!contactId || !number || !gross} onClick={addOpenInvoice}>Add</button>
        </div>
      </div>
      {err && <div className="err">{err}</div>}
      <div className="sub" style={{ marginTop: 16 }}>
        {(accounts ?? []).length} accounts in the chart. Reconcile the cards above to zero difference
        before you start posting live transactions.
      </div>
    </div>
  );
}
