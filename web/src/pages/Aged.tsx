import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, downloadFile, errorMessage, gbp } from '../api';
import { Chips, useLoad } from '../components';

export default function Aged() {
  const { businessId } = useParams();
  const [kind, setKind] = useState<'debtors' | 'creditors'>('debtors');
  const [report] = useLoad<any>(`/businesses/${businessId}/reports/aged-${kind}`, [kind]);
  const [statement, setStatement] = useState<any>(null);

  const openStatement = async (contactId: string) => {
    try {
      const r = await api.get(`/businesses/${businessId}/reports/customer-statement/${contactId}`);
      setStatement({ ...r.data, contactId });
    } catch (e) { alert(errorMessage(e)); }
  };

  if (!report) return <div className="sub">Loading…</div>;
  const T = report.totals ?? {};

  if (statement) {
    return (
      <div>
        <div className="row">
          <h1>Statement — {statement.contactName}</h1><div className="spacer" />
          <button className="btn ghost" onClick={() => downloadFile(
            `/businesses/${businessId}/reports/customer-statement/${statement.contactId}/pdf`,
            `statement-${statement.contactName}.pdf`)}>Download PDF</button>
          <button className="btn ghost" onClick={async () => {
            try {
              const r = await api.post(
                `/businesses/${businessId}/reports/customer-statement/${statement.contactId}/email`);
              alert(`Emailed to ${r.data.sentTo}`);
            } catch (e) { alert(errorMessage(e)); }
          }}>Email</button>
          <button className="btn" onClick={() => setStatement(null)}>Back</button>
        </div>
        <div className="sub">Open items as at {statement.asOf}</div>
        <table>
          <thead><tr><th>Type</th><th>Number</th><th>Date</th><th>Due</th>
            <th className="num">Days overdue</th><th className="num">Outstanding</th></tr></thead>
          <tbody>
            {(statement.items ?? []).map((i: any, idx: number) => (
              <tr key={idx}>
                <td>{i.kind}</td><td>{i.number}</td><td>{i.date}</td><td>{i.dueDate}</td>
                <td className="num">{i.daysOverdue > 0 ? i.daysOverdue : '—'}</td>
                <td className="num">{gbp(i.outstanding)}</td>
              </tr>
            ))}
            <tr><td colSpan={5}><strong>Total due</strong></td>
              <td className="num"><strong>{gbp(statement.totalDue)}</strong></td></tr>
          </tbody>
        </table>
      </div>
    );
  }

  return (
    <div>
      <div className="row">
        <h1>Ageing</h1><div className="spacer" />
        <Chips options={[['debtors', 'Debtors'], ['creditors', 'Creditors']]} value={kind} onChange={setKind} />
      </div>
      <div className="sub">
        As at {report.asOf} · by days overdue, net of unapplied credit notes
        {kind === 'debtors' && ' · click a row for the statement'}
      </div>
      <table>
        <thead><tr>
          <th>{kind === 'debtors' ? 'Customer' : 'Supplier'}</th>
          <th className="num">Current</th><th className="num">1–30</th><th className="num">31–60</th>
          <th className="num">61–90</th><th className="num">90+</th><th className="num">Total</th>
        </tr></thead>
        <tbody>
          {(report.rows ?? []).map((r: any) => (
            <tr key={r.contactId} className={kind === 'debtors' ? 'click' : ''}
              onClick={kind === 'debtors' ? () => openStatement(r.contactId) : undefined}>
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
            <td className="num">{gbp(T.current ?? 0)}</td><td className="num">{gbp(T.days30 ?? 0)}</td>
            <td className="num">{gbp(T.days60 ?? 0)}</td><td className="num">{gbp(T.days90 ?? 0)}</td>
            <td className="num">{gbp(T.older ?? 0)}</td>
            <td className="num"><strong>{gbp(T.total ?? 0)}</strong></td>
          </tr>
        </tbody>
      </table>
    </div>
  );
}
