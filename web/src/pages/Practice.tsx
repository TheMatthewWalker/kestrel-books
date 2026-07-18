import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, gbp } from '../api';

const KIND = ['VAT return', 'Year end', 'Overdue debtors', 'VAT check'];

export default function Practice() {
  const [data, setData] = useState<any>(null);
  const nav = useNavigate();

  useEffect(() => {
    api.get('/practice/dashboard', { params: { horizonDays: 60 } }).then(r => setData(r.data));
  }, []);

  if (!data) return <div className="sub">Loading…</div>;

  return (
    <div>
      <h1>Practice overview</h1>
      <div className="sub">{data.clientCount} clients · next 60 days</div>
      <div className="cards">
        <div className="card"><div className="label">Receivables</div>
          <div className="value">{gbp(data.totalReceivables)}</div></div>
        <div className="card"><div className="label">Overdue</div>
          <div className={`value${data.totalOverdue > 0 ? ' bad' : ''}`}>{gbp(data.totalOverdue)}</div></div>
        <div className="card"><div className="label">Payables</div>
          <div className="value">{gbp(data.totalPayables)}</div></div>
        <div className="card"><div className="label">VAT returns due soon</div>
          <div className={`value${data.vatReturnsDueSoon > 0 ? ' bad' : ''}`}>{data.vatReturnsDueSoon}</div></div>
      </div>

      <h2>Deadlines</h2>
      <table>
        <thead><tr><th>Client</th><th>What</th><th>Detail</th><th className="num">Due</th></tr></thead>
        <tbody>
          {data.deadlines.map((d: any, i: number) => (
            <tr key={i} className="click" onClick={() => nav(`/clients/${d.businessId}`)}>
              <td>{d.businessName}</td>
              <td>{KIND[d.kind]}</td>
              <td className="sub" style={{ marginBottom: 0 }}>{d.detail}</td>
              <td className="num">
                {d.actioned ? <span className="badge posted">done</span>
                  : d.kind === 2 ? <span className="badge overdue">chase</span>
                  : d.daysUntil < 0 ? <span className="badge overdue">overdue</span>
                  : `${d.daysUntil}d`}
              </td>
            </tr>
          ))}
          {data.deadlines.length === 0 && <tr><td colSpan={4} className="sub">Nothing due. Enviable.</td></tr>}
        </tbody>
      </table>
    </div>
  );
}
