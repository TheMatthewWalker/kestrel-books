import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, errorMessage, gbp } from '../api';
import { Chips, Field, useLoad } from '../components';

const MATCH: [number, string][] = [[0, 'Contains'], [1, 'Starts with'], [2, 'Exactly']];
const DIRECTION: [number, string][] = [[0, 'Any'], [1, 'Money in'], [2, 'Money out']];

export default function BankRules() {
  const { businessId } = useParams();
  const [rules, reload] = useLoad<any[]>(`/businesses/${businessId}/bank-rules`);
  const [accounts] = useLoad<any[]>(`/businesses/${businessId}/accounts`);
  const [showForm, setShowForm] = useState(false);
  const [applyBank, setApplyBank] = useState('');
  const [result, setResult] = useState<any>(null);
  const [f, setF] = useState({
    name: '', bankAccountId: '', matchType: 0, matchText: '', direction: 0,
    minAmount: '', maxAmount: '', accountId: '', autoPost: false, priority: '100',
  });
  const [err, setErr] = useState('');

  const banks = (accounts ?? []).filter((a: any) => a.isBank);
  const codeable = (accounts ?? []).filter((a: any) => !a.isBank);
  const accountLabel = (id: string) => {
    const a = (accounts ?? []).find((x: any) => x.id === id);
    return a ? `${a.code} ${a.name}` : '—';
  };

  const save = async () => {
    setErr('');
    try {
      await api.post(`/businesses/${businessId}/bank-rules`, {
        name: f.name || f.matchText, bankAccountId: f.bankAccountId || null,
        matchType: f.matchType, matchText: f.matchText, direction: f.direction,
        minAmount: f.minAmount ? parseFloat(f.minAmount) : null,
        maxAmount: f.maxAmount ? parseFloat(f.maxAmount) : null,
        accountId: f.accountId, vatRate: 4, vendorId: null, customerId: null,
        autoPost: f.autoPost, priority: parseInt(f.priority) || 100,
      });
      setShowForm(false); setF({ ...f, name: '', matchText: '' });
      reload();
    } catch (e) { setErr(errorMessage(e)); }
  };

  const act = async (fn: () => Promise<unknown>) => {
    try { await fn(); reload(); } catch (e) { alert(errorMessage(e)); }
  };

  const apply = async () => {
    try {
      const r = await api.post(`/businesses/${businessId}/bank-rules/apply`, {},
        { params: { bankAccountId: applyBank } });
      setResult(r.data);
      reload();
    } catch (e) { alert(errorMessage(e)); }
  };

  return (
    <div>
      <div className="row"><h1>Bank rules</h1><div className="spacer" />
        <button className="btn" onClick={() => setShowForm(s => !s)}>{showForm ? 'Close' : 'New rule'}</button></div>
      <div className="sub">
        Code the same transaction once instead of every month. Rules are checked in priority order,
        so a specific rule can sit in front of a general one — "AMAZON PRIME" before "AMAZON".
      </div>

      {showForm && (
        <div className="card" style={{ marginBottom: 18, maxWidth: 820 }}>
          <div className="row">
            <div style={{ flex: 2 }}><Field label="When the description">
              <input value={f.matchText} onChange={e => setF({ ...f, matchText: e.target.value })}
                placeholder="BRITISH GAS" /></Field></div>
            <Field label="Match"><select value={f.matchType}
              onChange={e => setF({ ...f, matchType: parseInt(e.target.value) })}>
              {MATCH.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select></Field>
          </div>
          <div className="row">
            <Field label="Code it to">
              <select value={f.accountId} onChange={e => setF({ ...f, accountId: e.target.value })}>
                <option value="">— account —</option>
                {codeable.map((a: any) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}
              </select></Field>
            <Field label="Direction"><select value={f.direction}
              onChange={e => setF({ ...f, direction: parseInt(e.target.value) })}>
              {DIRECTION.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select></Field>
            <Field label="Bank account">
              <select value={f.bankAccountId} onChange={e => setF({ ...f, bankAccountId: e.target.value })}>
                <option value="">Any</option>
                {banks.map((a: any) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}
              </select></Field>
          </div>
          <div className="row">
            <Field label="Min amount (optional)">
              <input value={f.minAmount} onChange={e => setF({ ...f, minAmount: e.target.value })} /></Field>
            <Field label="Max amount (optional)">
              <input value={f.maxAmount} onChange={e => setF({ ...f, maxAmount: e.target.value })} /></Field>
            <Field label="Priority (lower runs first)">
              <input value={f.priority} onChange={e => setF({ ...f, priority: e.target.value })} /></Field>
          </div>
          <label>What should it do?</label>
          <Chips options={[['suggest', 'Suggest, I confirm'], ['auto', 'Post automatically']]}
            value={f.autoPost ? 'auto' : 'suggest'} onChange={v => setF({ ...f, autoPost: v === 'auto' })} />
          <div className="sub" style={{ marginTop: 6 }}>
            Start with suggestions. Switch a rule to automatic once you have watched it get a few right.
          </div>
          {err && <div className="err">{err}</div>}
          <button className="btn" style={{ marginTop: 12 }}
            disabled={!f.matchText || !f.accountId} onClick={save}>Save rule</button>
        </div>
      )}

      <div className="card" style={{ maxWidth: 620, marginBottom: 18 }}>
        <h2 style={{ marginTop: 0 }}>Run the rules</h2>
        <div className="row" style={{ alignItems: 'flex-end' }}>
          <Field label="Bank account">
            <select value={applyBank} onChange={e => setApplyBank(e.target.value)}>
              <option value="">— choose —</option>
              {banks.map((a: any) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}
            </select></Field>
          <button className="btn" disabled={!applyBank} onClick={apply}>Apply to unmatched lines</button>
        </div>
        {result && (
          <div className="sub" style={{ marginTop: 10 }}>
            {result.matched} line(s) matched a rule · {result.posted} posted automatically ·
            {' '}{result.suggestions.length} awaiting your confirmation on the Banking page.
          </div>
        )}
      </div>

      <table>
        <thead><tr><th>Match</th><th>Type</th><th>Codes to</th><th>Direction</th>
          <th className="num">Range</th><th className="num">Priority</th>
          <th className="num">Used</th><th>Mode</th><th /></tr></thead>
        <tbody>
          {(rules ?? []).map((r: any) => (
            <tr key={r.id}>
              <td>{r.matchText}</td>
              <td>{MATCH.find(([v]) => v === r.matchType)?.[1]}</td>
              <td>{accountLabel(r.accountId)}</td>
              <td>{DIRECTION.find(([v]) => v === r.direction)?.[1]}</td>
              <td className="num">
                {r.minAmount || r.maxAmount
                  ? `${r.minAmount ? gbp(r.minAmount) : '—'} to ${r.maxAmount ? gbp(r.maxAmount) : '—'}`
                  : 'any'}
              </td>
              <td className="num">{r.priority}</td>
              <td className="num">{r.timesApplied}</td>
              <td><span className={`badge${r.autoPost ? ' posted' : ''}`}>
                {r.enabled ? (r.autoPost ? 'Automatic' : 'Suggests') : 'Off'}</span></td>
              <td>
                <div className="row" style={{ gap: 6 }}>
                  <button className="btn ghost" onClick={() => act(() =>
                    api.post(`/businesses/${businessId}/bank-rules/${r.id}/toggle`, {},
                      { params: { enabled: !r.enabled } }))}>{r.enabled ? 'Disable' : 'Enable'}</button>
                  <button className="btn ghost" onClick={() => {
                    if (confirm(`Delete the rule for "${r.matchText}"?`))
                      act(() => api.delete(`/businesses/${businessId}/bank-rules/${r.id}`));
                  }}>Delete</button>
                </div>
              </td>
            </tr>
          ))}
          {(rules ?? []).length === 0 &&
            <tr><td colSpan={9} className="sub">No rules yet — the first one usually pays for itself.</td></tr>}
        </tbody>
      </table>
    </div>
  );
}
