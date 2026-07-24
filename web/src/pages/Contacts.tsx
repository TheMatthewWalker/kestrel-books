import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, errorMessage } from '../api';
import { Chips, Field, useLoad } from '../components';

export default function Contacts() {
  const { businessId } = useParams();
  const [kind, setKind] = useState<'customers' | 'vendors'>('customers');
  const [items, reload] = useLoad<any[]>(`/businesses/${businessId}/${kind}`, [kind]);
  const [showForm, setShowForm] = useState(false);
  const [f, setF] = useState({ name: '', email: '', phone: '', addressLine1: '', addressLine2: '',
    city: '', postcode: '', vatNumber: '', paymentTermsDays: 30 });
  const [err, setErr] = useState('');

  const save = async () => {
    setErr('');
    try {
      await api.post(`/businesses/${businessId}/${kind}`, f);
      setShowForm(false);
      setF({ ...f, name: '', email: '', vatNumber: '' });
      reload();
    } catch (e) { setErr(errorMessage(e)); }
  };

  return (
    <div>
      <div className="row">
        <h1>Contacts</h1><div className="spacer" />
        <Chips options={[['customers', 'Customers'], ['vendors', 'Suppliers']]} value={kind} onChange={setKind} />
      </div>
      <div className="sub">{kind === 'customers' ? 'Who owes you.' : 'Who you owe.'}</div>
      <table>
        <thead><tr><th>Name</th><th>Email</th><th>Terms</th><th>VAT no.</th></tr></thead>
        <tbody>
          {(items ?? []).map((c: any) => (
            <tr key={c.id}><td>{c.name}</td><td>{c.email ?? '—'}</td>
              <td>{c.paymentTermsDays} days</td><td>{c.vatNumber ?? '—'}</td></tr>
          ))}
        </tbody>
      </table>
      {showForm ? (
        <div className="card" style={{ marginTop: 14, maxWidth: 520 }}>
          <Field label="Name"><input value={f.name} onChange={e => setF({ ...f, name: e.target.value })} /></Field>
          <Field label="Email (for invoice PDFs and statements)">
            <input value={f.email} onChange={e => setF({ ...f, email: e.target.value })} /></Field>
          <div className="row">
            <Field label="Payment terms (days)">
              <input type="number" value={f.paymentTermsDays}
                onChange={e => setF({ ...f, paymentTermsDays: parseInt(e.target.value) || 30 })} /></Field>
            <Field label="VAT number">
              <input value={f.vatNumber} onChange={e => setF({ ...f, vatNumber: e.target.value })} /></Field>
          </div>
          <Field label="Address line 1"><input value={f.addressLine1}
            onChange={e => setF({ ...f, addressLine1: e.target.value })} /></Field>
          <div className="row">
            <Field label="City"><input value={f.city} onChange={e => setF({ ...f, city: e.target.value })} /></Field>
            <Field label="Postcode"><input value={f.postcode} onChange={e => setF({ ...f, postcode: e.target.value })} /></Field>
          </div>
          {err && <div className="err">{err}</div>}
          <div className="row" style={{ marginTop: 14 }}>
            <button className="btn" onClick={save} disabled={!f.name}>Save</button>
            <button className="btn ghost" onClick={() => setShowForm(false)}>Cancel</button>
          </div>
        </div>
      ) : (
        <button className="btn" style={{ marginTop: 14 }} onClick={() => setShowForm(true)}>
          New {kind === 'customers' ? 'customer' : 'supplier'}
        </button>
      )}
    </div>
  );
}
