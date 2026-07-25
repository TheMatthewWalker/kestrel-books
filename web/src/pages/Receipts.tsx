import { useRef, useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, errorMessage, gbp, today } from '../api';
import { Chips, Field, useLoad } from '../components';

const SCAN_STATUS = ['Uploaded', 'Extracted', 'Confirmed', 'Failed'];

/**
 * Receipt capture: upload the image, check what was extracted, then post it
 * either as a purchase invoice (pay later) or as money out (paid on the spot).
 */
export default function Receipts() {
  const { businessId } = useParams();
  const [receipts, reload] = useLoad<any[]>(`/businesses/${businessId}/receipts?page=1&pageSize=100`);
  const [accounts] = useLoad<any[]>(`/businesses/${businessId}/accounts`);
  const [selected, setSelected] = useState<any>(null);
  const [imageUrl, setImageUrl] = useState<string | null>(null);
  const [f, setF] = useState({ vendorName: '', date: today(), net: '', vat: '', expenseAccountId: '', mode: 'invoice', bankAccountId: '' });
  const [err, setErr] = useState('');
  const [busy, setBusy] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);

  const expense = (accounts ?? []).filter((a: any) => a.type === 4);
  const banks = (accounts ?? []).filter((a: any) => a.isBank);

  const upload = async () => {
    const file = fileRef.current?.files?.[0];
    if (!file) return;
    setBusy(true); setErr('');
    const form = new FormData();
    form.append('file', file);
    try {
      await api.post(`/businesses/${businessId}/receipts/upload`, form,
        { headers: { 'Content-Type': 'multipart/form-data' } });
      if (fileRef.current) fileRef.current.value = '';
      reload();
    } catch (e) { setErr(errorMessage(e)); }
    finally { setBusy(false); }
  };

  const openReceipt = async (r: any) => {
    setSelected(r);
    setF({
      vendorName: r.vendorName ?? '', date: r.receiptDate ?? today(),
      net: r.netAmount != null ? String(r.netAmount) : '',
      vat: r.vatAmount != null ? String(r.vatAmount) : '',
      expenseAccountId: '', mode: 'invoice', bankAccountId: '',
    });
    try {
      const img = await api.get(`/businesses/${businessId}/receipts/${r.id}/image`, { responseType: 'blob' });
      setImageUrl(URL.createObjectURL(img.data));
    } catch { setImageUrl(null); }
  };

  const confirm = async () => {
    setErr(''); setBusy(true);
    try {
      const r = await api.post(`/businesses/${businessId}/receipts/${selected.id}/confirm`, {
        vendorName: f.vendorName, date: f.date,
        net: parseFloat(f.net) || 0, vat: parseFloat(f.vat) || 0,
        expenseAccountId: f.expenseAccountId, mode: f.mode,
        bankAccountId: f.mode === 'money' ? f.bankAccountId : null,
      });
      alert(f.mode === 'invoice'
        ? `Draft purchase invoice created${r.data.number ? ` (${r.data.number})` : ''}.`
        : 'Payment posted from the bank.');
      setSelected(null); setImageUrl(null);
      reload();
    } catch (e) { setErr(errorMessage(e)); }
    finally { setBusy(false); }
  };

  if (selected) {
    const gross = (parseFloat(f.net) || 0) + (parseFloat(f.vat) || 0);
    return (
      <div>
        <div className="row"><h1>Receipt</h1><div className="spacer" />
          <button className="btn" onClick={() => { setSelected(null); setImageUrl(null); }}>Back</button></div>
        <div className="sub">Check what was read off the image, then post it.</div>
        <div className="row" style={{ alignItems: 'flex-start', gap: 20 }}>
          <div style={{ flex: 1 }}>
            {imageUrl
              ? <img src={imageUrl} alt="receipt"
                  style={{ maxWidth: '100%', border: '1px solid var(--rule)', borderRadius: 10 }} />
              : <div className="sub">Image unavailable.</div>}
          </div>
          <div className="card" style={{ flex: 1 }}>
            <Field label="Supplier">
              <input value={f.vendorName} onChange={e => setF({ ...f, vendorName: e.target.value })} /></Field>
            <Field label="Date">
              <input type="date" value={f.date} onChange={e => setF({ ...f, date: e.target.value })} /></Field>
            <div className="row">
              <Field label="Net"><input value={f.net} onChange={e => setF({ ...f, net: e.target.value })} /></Field>
              <Field label="VAT"><input value={f.vat} onChange={e => setF({ ...f, vat: e.target.value })} /></Field>
            </div>
            <div className="sub" style={{ marginTop: 6 }}>Gross {gbp(gross)}</div>
            <Field label="Expense account">
              <select value={f.expenseAccountId} onChange={e => setF({ ...f, expenseAccountId: e.target.value })}>
                <option value="">— choose —</option>
                {expense.map((a: any) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}
              </select></Field>
            <label>How was it settled?</label>
            <Chips options={[['invoice', 'Pay later (purchase invoice)'], ['money', 'Paid on the spot']]}
              value={f.mode} onChange={m => setF({ ...f, mode: m })} />
            {f.mode === 'money' && (
              <Field label="Paid from">
                <select value={f.bankAccountId} onChange={e => setF({ ...f, bankAccountId: e.target.value })}>
                  <option value="">— bank —</option>
                  {banks.map((a: any) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}
                </select></Field>
            )}
            {selected.extractionNotes &&
              <div className="sub" style={{ marginTop: 10 }}>Extractor: {selected.extractionNotes}</div>}
            {err && <div className="err">{err}</div>}
            <button className="btn" style={{ marginTop: 12 }}
              disabled={busy || !f.expenseAccountId || (f.mode === 'money' && !f.bankAccountId)}
              onClick={confirm}>Post receipt</button>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div>
      <h1>Receipts</h1>
      <div className="sub">
        Photograph or scan the paper, check the figures, post it. The image stays attached as the
        VAT evidence HMRC expects you to keep.
      </div>
      <div className="row" style={{ alignItems: 'flex-end', maxWidth: 620 }}>
        <Field label="Receipt image or PDF"><input type="file" ref={fileRef} accept="image/*,application/pdf" /></Field>
        <button className="btn" disabled={busy} onClick={upload}>Upload</button>
      </div>
      {err && <div className="err">{err}</div>}

      <table style={{ marginTop: 16 }}>
        <thead><tr><th>Uploaded</th><th>Supplier</th><th>Date</th>
          <th className="num">Net</th><th className="num">VAT</th><th className="num">Gross</th><th>Status</th></tr></thead>
        <tbody>
          {(receipts ?? []).map((r: any) => (
            <tr key={r.id} className="click" onClick={() => openReceipt(r)}>
              <td>{new Date(r.uploadedAtUtc).toLocaleDateString('en-GB')}</td>
              <td>{r.vendorName ?? '—'}</td>
              <td>{r.receiptDate ?? '—'}</td>
              <td className="num">{r.netAmount != null ? gbp(r.netAmount) : '—'}</td>
              <td className="num">{r.vatAmount != null ? gbp(r.vatAmount) : '—'}</td>
              <td className="num">{r.grossAmount != null ? gbp(r.grossAmount) : '—'}</td>
              <td><span className={`badge${r.status === 2 ? ' posted' : ''}`}>
                {SCAN_STATUS[r.status] ?? r.status}</span></td>
            </tr>
          ))}
          {(receipts ?? []).length === 0 &&
            <tr><td colSpan={7} className="sub">Nothing uploaded yet.</td></tr>}
        </tbody>
      </table>
    </div>
  );
}
