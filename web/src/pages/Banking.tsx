import { useRef, useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, errorMessage, gbp } from '../api';
import { Field, useLoad } from '../components';

/** Statement import + reconciliation: match suggestions, exclude, or quick-create. */
export default function Banking() {
  const { businessId } = useParams();
  const [accounts] = useLoad<any[]>(`/businesses/${businessId}/accounts`);
  const [bankId, setBankId] = useState('');
  const [data, setData] = useState<any>(null);
  const [chosen, setChosen] = useState<Record<string, string>>({});
  const fileRef = useRef<HTMLInputElement>(null);

  const banks = (accounts ?? []).filter((a: any) => a.isBank);
  const plAccounts = (accounts ?? []).filter((a: any) => a.type === 3 || a.type === 4);

  const loadLines = async (id: string) => {
    setBankId(id);
    if (!id) { setData(null); return; }
    try {
      const r = await api.get(`/businesses/${businessId}/banking/lines`, { params: { bankAccountId: id } });
      setData(r.data);
    } catch (e) { alert(errorMessage(e)); }
  };

  const upload = async () => {
    const file = fileRef.current?.files?.[0];
    if (!file || !bankId) return;
    const form = new FormData();
    form.append('file', file);
    form.append('bankAccountId', bankId);
    try {
      await api.post(`/businesses/${businessId}/banking/import`, form,
        { headers: { 'Content-Type': 'multipart/form-data' } });
      loadLines(bankId);
    } catch (e) { alert(errorMessage(e)); }
  };

  const act = async (fn: () => Promise<unknown>) => {
    try { await fn(); loadLines(bankId); } catch (e) { alert(errorMessage(e)); }
  };

  const lines: any[] = data?.lines ?? (Array.isArray(data) ? data : []);
  const suggestions: Record<string, any> = {};
  (data?.suggestions ?? []).forEach((s: any) => { suggestions[s.lineId] = s; });

  return (
    <div>
      <h1>Banking</h1>
      <div className="sub">Import a statement, then work each line to zero: match, create, or exclude.</div>
      <div className="row" style={{ maxWidth: 760, alignItems: 'flex-end' }}>
        <Field label="Bank account">
          <select value={bankId} onChange={e => loadLines(e.target.value)}>
            <option value="">— choose —</option>
            {banks.map((a: any) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}
          </select></Field>
        <Field label="Statement file (CSV or OFX)"><input type="file" ref={fileRef} /></Field>
        <button className="btn" disabled={!bankId} onClick={upload}>Import</button>
      </div>

      {bankId && (
        <table style={{ marginTop: 16 }}>
          <thead><tr><th>Date</th><th>Description</th><th className="num">Amount</th><th>Status</th><th /></tr></thead>
          <tbody>
            {lines.map((l: any) => {
              const matched = l.status === 1 || l.status === 'Matched';
              const excluded = l.status === 2 || l.status === 'Excluded';
              const sugg = suggestions[l.id];
              return (
                <tr key={l.id}>
                  <td>{l.date}</td><td>{l.description}</td>
                  <td className={`num ${l.amount >= 0 ? 'cr' : 'dr'}`}>{gbp(l.amount)}</td>
                  <td><span className={`badge${matched ? ' posted' : ''}`}>
                    {matched ? 'Matched' : excluded ? 'Excluded' : 'Unmatched'}</span></td>
                  <td>
                    {!matched && !excluded && (
                      <div className="row" style={{ gap: 6 }}>
                        {sugg && <button className="btn ghost"
                          onClick={() => act(() => api.post(
                            `/businesses/${businessId}/banking/lines/${l.id}/match/${sugg.journalLineId}`))}>
                          Match</button>}
                        <select value={chosen[l.id] ?? ''}
                          onChange={e => setChosen(c => ({ ...c, [l.id]: e.target.value }))}>
                          <option value="">create as…</option>
                          {plAccounts.map((a: any) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}
                        </select>
                        <button className="btn ghost" disabled={!chosen[l.id]}
                          onClick={() => act(() => api.post(
                            `/businesses/${businessId}/banking/lines/${l.id}/create-transaction`,
                            { directAccountId: chosen[l.id], salesInvoiceId: null, purchaseInvoiceId: null }))}>
                          Create</button>
                        <button className="btn ghost"
                          onClick={() => act(() => api.post(`/businesses/${businessId}/banking/lines/${l.id}/exclude`))}>
                          Exclude</button>
                      </div>
                    )}
                  </td>
                </tr>
              );
            })}
            {lines.length === 0 && <tr><td colSpan={5} className="sub">No lines yet — import a statement.</td></tr>}
          </tbody>
        </table>
      )}
    </div>
  );
}
