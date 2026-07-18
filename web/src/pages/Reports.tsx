import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, gbp } from '../api';

/** Trial balance + P&L side by side — the practitioner's health check. */
export default function Reports() {
  const { businessId } = useParams();
  const [tb, setTb] = useState<any>(null);
  const [pl, setPl] = useState<any>(null);

  useEffect(() => {
    const today = new Date().toISOString().slice(0, 10);
    const yearStart = `${new Date().getFullYear()}-01-01`;
    api.get(`/businesses/${businessId}/reports/trial-balance`, { params: { asOf: today } })
      .then(r => setTb(r.data));
    api.get(`/businesses/${businessId}/reports/profit-and-loss`, { params: { from: yearStart, to: today } })
      .then(r => setPl(r.data));
  }, [businessId]);

  return (
    <div>
      <h1>Reports</h1>
      <div className="sub">Live from the ledger — every figure is the sum of posted journal lines.</div>

      {pl && (
        <div className="cards">
          <div className="card"><div className="label">Income (YTD)</div>
            <div className="value">{gbp(pl.income?.reduce((s: number, r: any) => s + r.amount, 0) ?? 0)}</div></div>
          <div className="card"><div className="label">Expenses (YTD)</div>
            <div className="value">{gbp(pl.expenses?.reduce((s: number, r: any) => s + r.amount, 0) ?? 0)}</div></div>
          <div className="card"><div className="label">Profit (YTD)</div>
            <div className={`value${pl.total < 0 ? ' bad' : ''}`}>{gbp(pl.total)}</div></div>
        </div>
      )}

      <h2>Trial balance</h2>
      {tb && (
        <table>
          <thead><tr><th>Code</th><th>Account</th><th className="num">Debit</th><th className="num">Credit</th></tr></thead>
          <tbody>
            {tb.rows.map((r: any) => (
              <tr key={r.code}>
                <td>{r.code}</td><td>{r.name}</td>
                <td className="num dr">{r.debit ? gbp(r.debit) : ''}</td>
                <td className="num cr">{r.credit ? gbp(r.credit) : ''}</td>
              </tr>
            ))}
            <tr>
              <td /><td><strong>Totals</strong></td>
              <td className="num"><strong>{gbp(tb.totalDebits)}</strong></td>
              <td className="num"><strong>{gbp(tb.totalCredits)}</strong></td>
            </tr>
          </tbody>
        </table>
      )}
    </div>
  );
}
