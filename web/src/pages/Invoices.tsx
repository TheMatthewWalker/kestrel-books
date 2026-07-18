import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, gbp } from '../api';

export default function Invoices() {
  const { businessId } = useParams();
  const [kind, setKind] = useState<'sales' | 'purchase'>('sales');
  const [items, setItems] = useState<any[]>([]);
  const [total, setTotal] = useState(0);

  useEffect(() => {
    api.get(`/businesses/${businessId}/${kind}-invoices`, { params: { page: 1, pageSize: 100 } })
      .then(r => {
        setItems(r.data);
        setTotal(parseInt(r.headers['x-total-count'] ?? '0', 10));
      });
  }, [businessId, kind]);

  return (
    <div>
      <div className="row">
        <h1>Invoices</h1>
        <div className="spacer" />
        <button className={`btn${kind === 'sales' ? '' : ' ghost'}`} onClick={() => setKind('sales')}>Sales</button>
        <button className={`btn${kind === 'purchase' ? '' : ' ghost'}`} onClick={() => setKind('purchase')}>Purchase</button>
      </div>
      <div className="sub">{total} total · latest 100 shown · creation and posting stay in the mobile app for now</div>
      <table>
        <thead><tr>
          <th>Number</th><th>Contact</th><th>Date</th><th>Due</th>
          <th className="num">Gross</th><th className="num">Outstanding</th><th>Status</th>
        </tr></thead>
        <tbody>
          {items.map(i => (
            <tr key={i.id}>
              <td>{i.number}</td><td>{i.contact ?? i.customer ?? i.vendor}</td>
              <td>{i.date}</td><td>{i.dueDate}</td>
              <td className="num">{gbp(i.grossTotal)}</td>
              <td className="num">{gbp(i.grossTotal - i.amountPaid)}</td>
              <td><span className={`badge${i.status === 'Posted' || i.status === 1 ? ' posted' : ''}`}>
                {typeof i.status === 'number' ? ['Draft', 'Posted', 'Void'][i.status] : i.status}
              </span></td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
