import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, errorMessage, gbp } from '../api';
import { Chips, Field, useLoad } from '../components';

export default function CreditControl() {
  const { businessId } = useParams();
  const [stages, reloadStages] = useLoad<any[]>(`/businesses/${businessId}/credit-control/stages`);
  const [history, reloadHistory] = useLoad<any[]>(`/businesses/${businessId}/credit-control/history`);
  const [view, setView] = useState('chase');
  const [preview, setPreview] = useState<any>(null);
  const [editing, setEditing] = useState<any>(null);

  const act = async (fn: () => Promise<unknown>) => {
    try { await fn(); reloadStages(); reloadHistory(); } catch (e) { alert(errorMessage(e)); }
  };

  const loadPreview = async () => {
    try {
      const r = await api.get(`/businesses/${businessId}/credit-control/preview`);
      setPreview(r.data);
    } catch (e) { alert(errorMessage(e)); }
  };

  const sendAll = async () => {
    if (!confirm('Send these reminders now? They go to your clients\' customers.')) return;
    try {
      const r = await api.post(`/businesses/${businessId}/credit-control/run`);
      alert(`${r.data.sent} reminder(s) sent.`);
      setPreview(null); reloadHistory();
    } catch (e) { alert(errorMessage(e)); }
  };

  const saveStage = async () => {
    try {
      const body = {
        name: editing.name, daysOverdue: parseInt(editing.daysOverdue) || 0,
        subject: editing.subject, body: editing.body,
        attachStatement: !!editing.attachStatement, enabled: editing.enabled !== false,
      };
      if (editing.id) await api.put(`/businesses/${businessId}/credit-control/stages/${editing.id}`, body);
      else await api.post(`/businesses/${businessId}/credit-control/stages`, body);
      setEditing(null); reloadStages();
    } catch (e) { alert(errorMessage(e)); }
  };

  return (
    <div>
      <h1>Credit control</h1>
      <div className="sub">
        Chasing debt is the job that most reliably gets postponed, and postponing it is exactly
        what makes it harder. A polite reminder at seven days collects far more than a firm one at ninety.
      </div>

      <Chips options={[['chase', 'Chase now'], ['ladder', 'The ladder'], ['history', 'What was sent']]}
        value={view} onChange={setView} />

      {view === 'chase' && (
        <div style={{ marginTop: 14 }}>
          <div className="row">
            <button className="btn" onClick={loadPreview}>Show me who needs chasing</button>
            {preview && preview.candidates.some((c: any) => c.sendable) &&
              <button className="btn ghost" onClick={sendAll}>Send them all</button>}
          </div>

          {preview && (
            <>
              <div className="sub" style={{ marginTop: 12 }}>
                {preview.candidates.length} to chase · {preview.skipped} already reminded at their current stage
              </div>
              <table>
                <thead><tr><th>Customer</th><th>Invoice</th><th className="num">Overdue</th>
                  <th className="num">Outstanding</th><th>Stage</th><th>Ready</th></tr></thead>
                <tbody>
                  {preview.candidates.map((c: any) => (
                    <tr key={c.invoiceId}>
                      <td>{c.customerName}</td><td>{c.invoiceNumber}</td>
                      <td className="num dr">{c.daysOverdue} days</td>
                      <td className="num">{gbp(c.outstanding)}</td>
                      <td>{c.stageName}{c.attachStatement ? ' + statement' : ''}</td>
                      <td>{c.sendable
                        ? <span className="badge posted">ready</span>
                        : <span className="badge overdue" title={c.blocker}>{c.blocker}</span>}</td>
                    </tr>
                  ))}
                  {preview.candidates.length === 0 &&
                    <tr><td colSpan={6} className="sub">Nobody needs chasing. Enjoy it.</td></tr>}
                </tbody>
              </table>
            </>
          )}
        </div>
      )}

      {view === 'ladder' && (
        <div style={{ marginTop: 14 }}>
          {(stages ?? []).length === 0 && (
            <div className="card" style={{ maxWidth: 560 }}>
              <div className="sub">
                No ladder yet. The default is three rungs — a gentle nudge at 7 days,
                something firmer at 21, and a final notice at 45 — all editable afterwards.
              </div>
              <button className="btn" onClick={() => act(() =>
                api.post(`/businesses/${businessId}/credit-control/stages/seed-defaults`))}>
                Create the default ladder
              </button>
            </div>
          )}

          {(stages ?? []).map((s: any) => (
            <div key={s.id} className="card" style={{ marginBottom: 10, maxWidth: 780 }}>
              <div className="row">
                <strong>{s.name}</strong>
                <span className="badge">{s.daysOverdue} days overdue</span>
                {s.attachStatement && <span className="badge">statement attached</span>}
                {!s.enabled && <span className="badge overdue">off</span>}
                <div className="spacer" />
                <button className="btn ghost" onClick={() => setEditing({ ...s })}>Edit</button>
                <button className="btn ghost" onClick={() => {
                  if (confirm(`Delete the "${s.name}" stage?`))
                    act(() => api.delete(`/businesses/${businessId}/credit-control/stages/${s.id}`));
                }}>Delete</button>
              </div>
              <div className="sub" style={{ marginTop: 8, marginBottom: 0 }}>{s.subject}</div>
              <pre style={{ whiteSpace: 'pre-wrap', fontSize: 12, margin: '8px 0 0' }}>{s.body}</pre>
            </div>
          ))}

          {(stages ?? []).length > 0 && !editing &&
            <button className="btn" onClick={() => setEditing({
              name: '', daysOverdue: '60', subject: '', body: '', attachStatement: false, enabled: true,
            })}>Add a stage</button>}

          {editing && (
            <div className="card" style={{ maxWidth: 780 }}>
              <div className="row">
                <Field label="Stage name">
                  <input value={editing.name}
                    onChange={e => setEditing({ ...editing, name: e.target.value })} /></Field>
                <Field label="Days overdue">
                  <input value={editing.daysOverdue}
                    onChange={e => setEditing({ ...editing, daysOverdue: e.target.value })} /></Field>
              </div>
              <Field label="Subject">
                <input value={editing.subject}
                  onChange={e => setEditing({ ...editing, subject: e.target.value })} /></Field>
              <label>Message</label>
              <textarea rows={8} style={{ width: '100%', padding: 10, borderRadius: 7,
                border: '1px solid var(--rule)', font: 'inherit' }}
                value={editing.body} onChange={e => setEditing({ ...editing, body: e.target.value })} />
              <div className="sub" style={{ marginTop: 6 }}>
                Placeholders: {'{customer} {invoice} {amount} {due} {days} {business}'}
              </div>
              <Chips options={[['no', 'Message only'], ['yes', 'Attach statement PDF']]}
                value={editing.attachStatement ? 'yes' : 'no'}
                onChange={v => setEditing({ ...editing, attachStatement: v === 'yes' })} />
              <div className="row" style={{ marginTop: 12 }}>
                <button className="btn" disabled={!editing.name || !editing.subject} onClick={saveStage}>Save</button>
                <button className="btn ghost" onClick={() => setEditing(null)}>Cancel</button>
              </div>
            </div>
          )}
        </div>
      )}

      {view === 'history' && (
        <table style={{ marginTop: 14 }}>
          <thead><tr><th>Sent</th><th>Customer</th><th>Invoice</th><th>Stage</th>
            <th className="num">Overdue then</th><th className="num">Outstanding then</th><th>To</th></tr></thead>
          <tbody>
            {(history ?? []).map((h: any) => (
              <tr key={h.id}>
                <td>{new Date(h.sentAtUtc).toLocaleDateString('en-GB')}</td>
                <td>{h.customerName}</td><td>{h.invoiceNumber}</td><td>{h.stageName}</td>
                <td className="num">{h.daysOverdueAtSend} days</td>
                <td className="num">{gbp(h.outstandingAtSend)}</td>
                <td>{h.sentTo}</td>
              </tr>
            ))}
            {(history ?? []).length === 0 &&
              <tr><td colSpan={7} className="sub">Nothing sent yet.</td></tr>}
          </tbody>
        </table>
      )}
    </div>
  );
}
