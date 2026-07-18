import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api';

export default function Clients() {
  const [items, setItems] = useState<any[]>([]);
  const nav = useNavigate();
  useEffect(() => { api.get('/businesses').then(r => setItems(r.data)); }, []);

  return (
    <div>
      <h1>Clients</h1>
      <div className="sub">Every business you can access, with your role in each.</div>
      <table>
        <thead><tr><th>Name</th><th>VAT no.</th><th>Your role</th></tr></thead>
        <tbody>
          {items.map(b => (
            <tr key={b.id} className="click" onClick={() => nav(`/clients/${b.id}`)}>
              <td>{b.name}</td>
              <td>{b.vatNumber ?? '—'}</td>
              <td><span className="badge">{['Owner', 'Bookkeeper', 'ReadOnly', 'Accountant'][b.role] ?? b.role}</span></td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
