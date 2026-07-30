import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, errorMessage, gbp, today } from '../api';
import { Chips, Field, useLoad } from '../components';

export default function Insights() {
  const { businessId } = useParams();
  const [view, setView] = useState('cash');
  const [forecast] = useLoad<any>(`/businesses/${businessId}/insights/cash-flow-forecast?weeks=13`);
  const [from, setFrom] = useState(`${new Date().getFullYear()}-01-01`);
  const [to, setTo] = useState(today());
  const [gap, setGap] = useState<any>(null);
  const [email, setEmail] = useState('');

  const findGaps = async () => {
    try {
      const r = await api.get(`/businesses/${businessId}/insights/records-gap`,
        { params: { from, to, threshold: 50 } });
      setGap(r.data);
    } catch (e) { alert(errorMessage(e)); }
  };

  const request = async () => {
    try {
      const r = await api.post(`/businesses/${businessId}/insights/records-gap/request`, {
        toEmail: email, from, to, threshold: 50,
      });
      alert(r.data.message ?? `Asked for ${r.data.requested} record(s), itemised, sent to ${r.data.sentTo}.`);
    } catch (e) { alert(errorMessage(e)); }
  };

  const maxFlow = forecast
    ? Math.max(...forecast.weeks.map((w: any) => Math.max(w.inflows, w.outflows)), 1) : 1;

  return (
    <div>
      <h1>Insights</h1>
      <div className="sub">
        Two things the ledger can answer that a bolt-on tool cannot: when the cash actually
        arrives, and exactly which paperwork is missing.
      </div>

      <Chips options={[['cash', 'Cash flow'], ['records', 'Missing records']]}
        value={view} onChange={setView} />

      {view === 'cash' && forecast && (
        <div style={{ marginTop: 16 }}>
          <div className="cards">
            <div className="card"><div className="label">Bank today</div>
              <div className="value">{gbp(forecast.openingBalance)}</div></div>
            <div className="card"><div className="label">Lowest point</div>
              <div className={`value${forecast.goesNegative ? ' bad' : ''}`}>
                {gbp(forecast.lowestBalance)}</div></div>
            <div className="card"><div className="label">When</div>
              <div className="value" style={{ fontSize: 16 }}>{forecast.lowestWeek ?? '—'}</div></div>
          </div>

          <div className="sub" style={{ marginTop: 10 }}>{forecast.basis}</div>
          {forecast.goesNegative && (
            <div className="err">
              On these assumptions the bank goes overdrawn. Worth chasing receipts early
              or agreeing terms before it happens rather than after.
            </div>
          )}

          <h2>Next 13 weeks</h2>
          <table>
            <thead><tr><th>Week beginning</th><th className="num">In</th><th className="num">Out</th>
              <th className="num">Net</th><th className="num">Balance</th><th /></tr></thead>
            <tbody>
              {forecast.weeks.map((w: any) => (
                <tr key={w.weekStart}>
                  <td>{w.weekStart}</td>
                  <td className="num cr">{w.inflows ? gbp(w.inflows) : ''}</td>
                  <td className="num dr">{w.outflows ? gbp(w.outflows) : ''}</td>
                  <td className={`num ${w.net >= 0 ? 'cr' : 'dr'}`}>{gbp(w.net)}</td>
                  <td className={`num${w.closingBalance < 0 ? ' dr' : ''}`}>
                    <strong>{gbp(w.closingBalance)}</strong></td>
                  <td style={{ width: 160 }}>
                    <div style={{ display: 'flex', height: 10, gap: 1 }}>
                      <div style={{ width: `${(w.inflows / maxFlow) * 70}px`,
                        background: 'var(--credit)', borderRadius: 2 }} />
                      <div style={{ width: `${(w.outflows / maxFlow) * 70}px`,
                        background: 'var(--debit)', borderRadius: 2 }} />
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          {forecast.payers.length > 0 && (
            <>
              <h2>How your customers actually pay</h2>
              <div className="sub">
                Measured from settled invoices. Anyone well above their terms is financing
                themselves at this client's expense.
              </div>
              <table>
                <thead><tr><th>Customer</th><th className="num">Invoices measured</th>
                  <th className="num">Average days to pay</th><th className="num">Terms</th>
                  <th className="num">Days late</th></tr></thead>
                <tbody>
                  {forecast.payers.map((p: any, i: number) => (
                    <tr key={i}>
                      <td>{p.customerName}</td>
                      <td className="num">{p.invoicesSettled}</td>
                      <td className="num">{p.averageDaysToPay}</td>
                      <td className="num">{p.termsDays}</td>
                      <td className={`num ${p.daysLate > 0 ? 'dr' : 'cr'}`}>
                        {p.daysLate > 0 ? `+${p.daysLate}` : p.daysLate}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}
        </div>
      )}

      {view === 'records' && (
        <div style={{ marginTop: 16 }}>
          <div className="row" style={{ alignItems: 'flex-end', maxWidth: 620 }}>
            <Field label="From"><input type="date" value={from}
              onChange={e => setFrom(e.target.value)} /></Field>
            <Field label="To"><input type="date" value={to}
              onChange={e => setTo(e.target.value)} /></Field>
            <button className="btn" onClick={findGaps}>Find what's missing</button>
          </div>

          {gap && (
            <>
              <div className="cards" style={{ marginTop: 14 }}>
                <div className="card"><div className="label">Missing documents</div>
                  <div className={`value${gap.count > 0 ? ' bad' : ''}`}>{gap.count}</div></div>
                <div className="card"><div className="label">Value covered</div>
                  <div className="value">{gbp(gap.totalValue)}</div></div>
                <div className="card"><div className="label">VAT recovery at risk</div>
                  <div className={`value${gap.recoverableVatAtRisk > 0 ? ' bad' : ''}`}>
                    {gbp(gap.recoverableVatAtRisk)}</div></div>
              </div>

              {gap.count > 0 && (
                <div className="card" style={{ marginTop: 14, maxWidth: 620 }}>
                  <div className="sub">
                    Send the client this exact list rather than a vague request for receipts —
                    that difference is what gets it actioned.
                  </div>
                  <div className="row" style={{ alignItems: 'flex-end' }}>
                    <Field label="Email the client">
                      <input value={email} onChange={e => setEmail(e.target.value)}
                        placeholder="client@example.com" /></Field>
                    <button className="btn" disabled={!email} onClick={request}>Ask for these</button>
                  </div>
                </div>
              )}

              <table style={{ marginTop: 14 }}>
                <thead><tr><th>Date</th><th>Type</th><th>Reference</th>
                  <th>Detail</th><th className="num">Amount</th></tr></thead>
                <tbody>
                  {gap.items.map((i: any, idx: number) => (
                    <tr key={idx}>
                      <td>{i.date}</td><td>{i.kind}</td><td>{i.reference}</td>
                      <td>{i.description}</td>
                      <td className="num">{gbp(i.amount)}</td>
                    </tr>
                  ))}
                  {gap.items.length === 0 &&
                    <tr><td colSpan={5} className="sub">Everything over £50 has a document attached.</td></tr>}
                </tbody>
              </table>
            </>
          )}
        </div>
      )}
    </div>
  );
}
