import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, downloadFile, errorMessage } from '../api';
import { Field, useLoad } from '../components';

const ROLES: [number, string][] = [[0, 'Read only'], [1, 'Bookkeeper'], [3, 'Accountant'], [2, 'Owner']];
const roleName = (r: number | string) =>
  typeof r === 'string' ? r : (ROLES.find(([v]) => v === r)?.[1] ?? String(r));

export default function Users() {
  const { businessId } = useParams();
  const [users, reload] = useLoad<any[]>(`/businesses/${businessId}/users`);
  const [email, setEmail] = useState('');
  const [role, setRole] = useState(1);
  const [err, setErr] = useState('');

  const invite = async () => {
    setErr('');
    try {
      await api.post(`/businesses/${businessId}/users`, { email, role });
      setEmail(''); reload();
    } catch (e) { setErr(errorMessage(e)); }
  };

  const change = async (userId: string, newRole: number) => {
    try { await api.put(`/businesses/${businessId}/users/${userId}`, { role: newRole }); reload(); }
    catch (e) { alert(errorMessage(e)); }
  };

  const remove = async (userId: string, name: string) => {
    if (!confirm(`Remove ${name} from this client?`)) return;
    try { await api.delete(`/businesses/${businessId}/users/${userId}`); reload(); }
    catch (e) { alert(errorMessage(e)); }
  };

  return (
    <div>
      <h1>Users</h1>
      <div className="sub">
        Who can see this client, and what they can do. Owners manage users; accountants can file to HMRC;
        bookkeepers post transactions; read-only sees but cannot change.
      </div>
      <table>
        <thead><tr><th>Name</th><th>Email</th><th>Role</th><th /></tr></thead>
        <tbody>
          {(users ?? []).map((u: any) => (
            <tr key={u.userId ?? u.id}>
              <td>{u.displayName ?? '—'}</td><td>{u.email}</td>
              <td>
                <select value={typeof u.role === 'number' ? u.role : ''} 
                  onChange={e => change(u.userId ?? u.id, parseInt(e.target.value))}>
                  {ROLES.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
                </select>
                {typeof u.role !== 'number' && <span className="badge">{roleName(u.role)}</span>}
              </td>
              <td><button className="btn ghost"
                onClick={() => remove(u.userId ?? u.id, u.displayName ?? u.email)}>Remove</button></td>
            </tr>
          ))}
        </tbody>
      </table>

      <div className="card" style={{ marginTop: 14, maxWidth: 520 }}>
        <h2 style={{ marginTop: 0 }}>Invite someone</h2>
        <div className="sub">
          Public sign-up is closed by default — people are given access here, by an owner,
          rather than registering themselves.
        </div>
        <Field label="Email (must already have a KestrelBooks account)">
          <input value={email} onChange={e => setEmail(e.target.value)} /></Field>
        <Field label="Role">
          <select value={role} onChange={e => setRole(parseInt(e.target.value))}>
            {ROLES.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
          </select></Field>
        {err && <div className="err">{err}</div>}
        <button className="btn" style={{ marginTop: 12 }} disabled={!email} onClick={invite}>Grant access</button>
      </div>

      <div className="card" style={{ marginTop: 18, maxWidth: 520 }}>
        <h2 style={{ marginTop: 0 }}>Export everything</h2>
        <div className="sub">
          A complete copy of these books as a zip of CSV files — ledger, contacts, invoices,
          items, assets, VAT submissions and the audit trail. Keep one before any major change,
          and hand one over if the client ever leaves.
        </div>
        <button className="btn ghost" onClick={() => downloadFile(
          `/businesses/${businessId}/data/export`, `kestrelbooks-export.zip`)}>
          Download full export
        </button>
      </div>
    </div>
  );
}
