import { useRef, useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, errorMessage, gbp, today } from '../api';
import { Field } from '../components';

/**
 * Imports the journal a payroll package produces. Running payroll itself is
 * deliberately out of scope — BrightPay and Moneysoft do it well, and what
 * practices need here is for the resulting journal to land without retyping.
 */
export default function Payroll() {
  const { businessId } = useParams();
  const [date, setDate] = useState(today());
  const [reference, setReference] = useState('');
  const [preview, setPreview] = useState<any>(null);
  const fileRef = useRef<HTMLInputElement>(null);

  const send = async (mode: 'preview' | 'import') => {
    const file = fileRef.current?.files?.[0];
    if (!file) return;
    const form = new FormData();
    form.append('file', file);
    form.append('date', date);
    if (mode === 'import') form.append('reference', reference || `PAY-${date.slice(0, 7)}`);
    try {
      const r = await api.post(`/businesses/${businessId}/payroll/${mode}`, form,
        { headers: { 'Content-Type': 'multipart/form-data' } });
      if (mode === 'preview') setPreview(r.data);
      else {
        alert(`Posted as journal ${r.data.journalNumber}.`);
        setPreview(null);
        if (fileRef.current) fileRef.current.value = '';
      }
    } catch (e) { alert(errorMessage(e)); }
  };

  return (
    <div>
      <h1>Payroll journal</h1>
      <div className="sub">
        Export the journal from your payroll package as CSV — account code, debit, credit,
        description — then check it here before it posts.
      </div>

      <div className="card" style={{ maxWidth: 700 }}>
        <div className="row" style={{ alignItems: 'flex-end' }}>
          <Field label="Journal date (usually the pay date or month end)">
            <input type="date" value={date} onChange={e => setDate(e.target.value)} /></Field>
          <Field label="Reference">
            <input value={reference} onChange={e => setReference(e.target.value)}
              placeholder={`PAY-${date.slice(0, 7)}`} /></Field>
        </div>
        <Field label="CSV file"><input type="file" ref={fileRef} accept=".csv,text/csv" /></Field>
        <div className="row" style={{ marginTop: 12 }}>
          <button className="btn" onClick={() => send('preview')}>Check the file</button>
          {preview && preview.problems.length === 0 &&
            <button className="btn ghost" onClick={() => send('import')}>Post it</button>}
        </div>
      </div>

      {preview && (
        <div style={{ marginTop: 18 }}>
          {preview.problems.length > 0 ? (
            <div className="card" style={{ maxWidth: 700 }}>
              <strong>This file cannot be posted yet</strong>
              <ul style={{ margin: '8px 0 0', paddingLeft: 18 }}>
                {preview.problems.map((p: string, i: number) =>
                  <li key={i} style={{ color: 'var(--debit)', fontSize: 13 }}>{p}</li>)}
              </ul>
            </div>
          ) : (
            <div className="sub">
              Balanced at {gbp(preview.totalDebits)} each side. Check the accounts below, then post.
            </div>
          )}

          <table style={{ marginTop: 12 }}>
            <thead><tr><th>Code</th><th>Account</th><th>Description</th>
              <th className="num">Debit</th><th className="num">Credit</th></tr></thead>
            <tbody>
              {preview.lines.map((l: any, i: number) => (
                <tr key={i}>
                  <td>{l.code}</td>
                  <td>{l.matched ? l.accountName
                    : <span className="badge overdue">{l.problem}</span>}</td>
                  <td>{l.description ?? ''}</td>
                  <td className="num dr">{l.debit ? gbp(l.debit) : ''}</td>
                  <td className="num cr">{l.credit ? gbp(l.credit) : ''}</td>
                </tr>
              ))}
              <tr>
                <td colSpan={3}><strong>Totals</strong></td>
                <td className="num"><strong>{gbp(preview.totalDebits)}</strong></td>
                <td className="num"><strong>{gbp(preview.totalCredits)}</strong></td>
              </tr>
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
