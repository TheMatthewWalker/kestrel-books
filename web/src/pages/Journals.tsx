import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, errorMessage, gbp, today } from '../api';
import { Field, useLoad } from '../components';

type Line = { accountId: string; debit: string; credit: string; description: string };
const blank = (): Line => ({ accountId: '', debit: '', credit: '', description: '' });

export default function Journals() {
  const { businessId } = useParams();
  const [journals, reload] = useLoad<any[]>(`/businesses/${businessId}/journals?page=1&pageSize=50`);
  const [accounts] = useLoad<any[]>(`/businesses/${businessId}/accounts`);
  const [showForm, setShowForm] = useState(false);
  const [date, setDate] = useState(today());
  const [reference, setReference] = useState('');
  const [narrative, setNarrative] = useState('');
  const [lines, setLines] = useState<Line[]>([blank(), blank()]);
  const [err, setErr] = useState('');

  const totals = lines.reduce((t, l) => ({
    dr: t.dr + (parseFloat(l.debit) || 0), cr: t.cr + (parseFloat(l.credit) || 0),
  }), { dr: 0, cr: 0 });
  const balanced = Math.abs(totals.dr - totals.cr) < 0.005 && totals.dr > 0;

  const set = (i: number, patch: Partial<Line>) =>
    setLines(ls => ls.map((l, idx) => idx === i ? { ...l, ...patch } : l));

  const save = async (post: boolean) => {
    setErr('');
    try {
      const r = await api.post(`/businesses/${businessId}/journals`, {
        date, reference, narrative,
        lines: lines.filter(l => l.accountId && (l.debit || l.credit)).map(l => ({
          accountId: l.accountId, debit: parseFloat(l.debit) || 0,
          credit: parseFloat(l.credit) || 0, description: l.description || null,
        })),
      });
      if (post) await api.post(`/businesses/${businessId}/journals/${r.data.id}/post`);
      setShowForm(false); setLines([blank(), blank()]); setReference(''); setNarrative('');
      reload();
    } catch (e) { setErr(errorMessage(e)); }
  };

  const act = async (id: string, action: 'post' | 'reverse') => {
    try {
      await api.post(`/businesses/${businessId}/journals/${id}/${action}`,
        action === 'reverse' ? { date: today() } : {});
      reload();
    } catch (e) { alert(errorMessage(e)); }
  };

  return (
    <div>
      <div className="row"><h1>Journals</h1><div className="spacer" />
        <button className="btn" onClick={() => setShowForm(s => !s)}>
          {showForm ? 'Close' : 'New journal'}</button></div>
      <div className="sub">Every financial event, as balanced double entry.</div>

      {showForm && (
        <div className="card" style={{ marginBottom: 18 }}>
          <div className="row">
            <Field label="Date"><input type="date" value={date} onChange={e => setDate(e.target.value)} /></Field>
            <Field label="Reference"><input value={reference} onChange={e => setReference(e.target.value)} /></Field>
            <div style={{ flex: 2 }}><Field label="Narrative">
              <input value={narrative} onChange={e => setNarrative(e.target.value)} /></Field></div>
          </div>
          <table style={{ marginTop: 12 }}>
            <thead><tr><th>Account</th><th>Description</th>
              <th className="num">Debit</th><th className="num">Credit</th></tr></thead>
            <tbody>
              {lines.map((l, i) => (
                <tr key={i}>
                  <td><select value={l.accountId} onChange={e => set(i, { accountId: e.target.value })}>
                    <option value="">— account —</option>
                    {(accounts ?? []).map((a: any) =>
                      <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}
                  </select></td>
                  <td><input value={l.description} onChange={e => set(i, { description: e.target.value })} /></td>
                  <td><input style={{ textAlign: 'right' }} value={l.debit}
                    onChange={e => set(i, { debit: e.target.value, credit: '' })} /></td>
                  <td><input style={{ textAlign: 'right' }} value={l.credit}
                    onChange={e => set(i, { credit: e.target.value, debit: '' })} /></td>
                </tr>
              ))}
              <tr><td colSpan={2}><button className="btn ghost" onClick={() => setLines(ls => [...ls, blank()])}>
                + line</button></td>
                <td className="num"><strong>{gbp(totals.dr)}</strong></td>
                <td className="num"><strong>{gbp(totals.cr)}</strong></td></tr>
            </tbody>
          </table>
          {!balanced && totals.dr + totals.cr > 0 &&
            <div className="err">Debits and credits must be equal — that's the whole idea.</div>}
          {err && <div className="err">{err}</div>}
          <div className="row" style={{ marginTop: 12 }}>
            <button className="btn" disabled={!balanced} onClick={() => save(true)}>Save &amp; post</button>
            <button className="btn ghost" disabled={!balanced} onClick={() => save(false)}>Save draft</button>
          </div>
        </div>
      )}

      <table>
        <thead><tr><th>#</th><th>Date</th><th>Reference</th><th>Narrative</th><th>Status</th><th /></tr></thead>
        <tbody>
          {(journals ?? []).map((j: any) => (
            <tr key={j.id}>
              <td>{j.number || '—'}</td><td>{j.date}</td><td>{j.reference}</td><td>{j.narrative}</td>
              <td><span className={`badge${j.status === 1 || j.status === 'Posted' ? ' posted' : ''}`}>
                {typeof j.status === 'number' ? ['Draft', 'Posted', 'Reversed'][j.status] : j.status}</span></td>
              <td>
                {(j.status === 0 || j.status === 'Draft') &&
                  <button className="btn ghost" onClick={() => act(j.id, 'post')}>Post</button>}
                {(j.status === 1 || j.status === 'Posted') &&
                  <button className="btn ghost" onClick={() => act(j.id, 'reverse')}>Reverse</button>}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
