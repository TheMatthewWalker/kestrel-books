import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { useLoad } from '../components';

const ACTION = ['Created', 'Updated', 'Deleted'];

/** Who changed what. Read-only, and Accountant-or-above by design. */
export default function Audit() {
  const { businessId } = useParams();
  const [type, setType] = useState('');
  const [entries] = useLoad<any[]>(
    `/businesses/${businessId}/audit?page=1&pageSize=200${type ? `&entityType=${type}` : ''}`, [type]);
  const [expanded, setExpanded] = useState<string | null>(null);

  const types = ['', 'SalesInvoice', 'PurchaseInvoice', 'Customer', 'Vendor', 'Item',
    'Account', 'FixedAsset', 'UserBusinessAccess', 'MoneyTransaction'];

  const describe = (changesJson: string) => {
    try {
      const changes = JSON.parse(changesJson);
      const keys = Object.keys(changes);
      if (keys.length === 0) return '—';
      return keys.slice(0, 4).map(k =>
        `${k}: ${changes[k].from ?? '(new)'} → ${changes[k].to ?? '(cleared)'}`).join(', ')
        + (keys.length > 4 ? ` (+${keys.length - 4} more)` : '');
    } catch { return changesJson; }
  };

  return (
    <div>
      <h1>Audit trail</h1>
      <div className="sub">
        Every change to the mutable records, captured automatically. Posted journals are absent
        on purpose — they never change, and corrections leave a reversal instead.
      </div>

      <div className="row" style={{ maxWidth: 400 }}>
        <div style={{ flex: 1 }}>
          <label>Filter by record type</label>
          <select value={type} onChange={e => setType(e.target.value)}>
            {types.map(t => <option key={t} value={t}>{t || 'Everything'}</option>)}
          </select>
        </div>
      </div>

      <table style={{ marginTop: 14 }}>
        <thead><tr><th>When</th><th>Who</th><th>Record</th><th>Action</th><th>Changes</th></tr></thead>
        <tbody>
          {(entries ?? []).map((a: any) => (
            <tr key={a.id} className="click" onClick={() => setExpanded(expanded === a.id ? null : a.id)}>
              <td>{new Date(a.atUtc).toLocaleString('en-GB')}</td>
              <td>{a.userName ?? '—'}</td>
              <td>{a.entityType}</td>
              <td><span className={`badge${a.action === 0 ? ' posted' : a.action === 2 ? ' overdue' : ''}`}>
                {ACTION[a.action] ?? a.action}</span></td>
              <td style={{ fontSize: 12 }}>
                {expanded === a.id
                  ? <pre style={{ margin: 0, whiteSpace: 'pre-wrap' }}>
                      {JSON.stringify(JSON.parse(a.changes || '{}'), null, 1)}</pre>
                  : describe(a.changes)}
              </td>
            </tr>
          ))}
          {(entries ?? []).length === 0 &&
            <tr><td colSpan={5} className="sub">No changes recorded yet.</td></tr>}
        </tbody>
      </table>
    </div>
  );
}
