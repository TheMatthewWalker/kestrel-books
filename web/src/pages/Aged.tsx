import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, gbp } from '../api';

export default function Aged() {
  const { businessId } = useParams();
  const [report, setReport] = useState<any>(null);
  useEffect(() => {
    api.get(`/businesses/${businessId}/reports/aged-debtors`).then(r => setReport(r.data));
  }, [businessId]);

  if (!report) return <div className="sub">Loading…</div>;
  const B = report.totals;
  return (
    <div>
      <h1>Aged debtors</h1>
      <div className="sub">As at {report.asOf} · by days overdue, net of unapplied credits</div>
      <table>
        <thead><tr>
          <th>Customer</th><th className="num">Current</th><th className="num">1–30</th>
          <th className="num">31–60</th><th className="num">61–90</th><th className="num">90+</th>
          <th className="num">Total</th>
        </tr></thead>
        <tbody>
          {report.rows.map((r: any) => (
            <tr key={r.contactId}>
              <td>{r.name}</td>
              <td className="num">{gbp(r.buckets.current)}</td>
              <td className="num">{gbp(r.buckets.days30)}</td>
              <td className="num">{gbp(r.buckets.days60)}</td>
              <td className="num">{gbp(r.buckets.days90)}</td>
              <td className={`num${r.buckets.older > 0 ? ' dr' : ''}`}>{gbp(r.buckets.older)}</td>
              <td className="num"><strong>{gbp(r.buckets.total)}</strong></td>
            </tr>
          ))}
          <tr>
            <td><strong>Totals</strong></td>
            <td className="num">{gbp(B.current)}</td><td className="num">{gbp(B.days30)}</td>
            <td className="num">{gbp(B.days60)}</td><td className="num">{gbp(B.days90)}</td>
            <td className="num">{gbp(B.older)}</td>
            <td className="num"><strong>{gbp(B.total)}</strong></td>
          </tr>
        </tbody>
      </table>
    </div>
  );
}
