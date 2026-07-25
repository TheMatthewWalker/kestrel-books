import { Navigate, NavLink, Outlet, Route, Routes, useParams } from 'react-router-dom';
import { useAuth } from './auth';
import Login from './pages/Login';
import Practice from './pages/Practice';
import Clients from './pages/Clients';
import Reports from './pages/Reports';
import Aged from './pages/Aged';
import Invoices from './pages/Invoices';
import Contacts from './pages/Contacts';
import Journals from './pages/Journals';
import CreditNotes from './pages/CreditNotes';
import Money from './pages/Money';
import Banking from './pages/Banking';
import Vat from './pages/Vat';
import Opening from './pages/Opening';
import Periods from './pages/Periods';
import Users from './pages/Users';
import Recurring from './pages/Recurring';
import Items from './pages/Items';
import Inventory from './pages/Inventory';
import Assets from './pages/Assets';
import Production from './pages/Production';
import Receipts from './pages/Receipts';
import PeriodEnd from './pages/PeriodEnd';
import Audit from './pages/Audit';
import BankRules from './pages/BankRules';

export default function App() {
  const { ready, signedIn } = useAuth();
  if (!ready) return <div className="login"><div className="sub">Restoring session…</div></div>;
  if (!signedIn) return <Login />;
  return (
    <Routes>
      <Route element={<Shell />}>
        <Route index element={<Navigate to="/practice" replace />} />
        <Route path="/practice" element={<Practice />} />
        <Route path="/clients" element={<Clients />} />
        <Route path="/clients/:businessId" element={<ClientShell />}>
          <Route index element={<Navigate to="reports" replace />} />
          <Route path="reports" element={<Reports />} />
          <Route path="aged" element={<Aged />} />
          <Route path="invoices" element={<Invoices />} />
          <Route path="credit-notes" element={<CreditNotes />} />
          <Route path="money" element={<Money />} />
          <Route path="banking" element={<Banking />} />
          <Route path="journals" element={<Journals />} />
          <Route path="contacts" element={<Contacts />} />
          <Route path="recurring" element={<Recurring />} />
          <Route path="vat" element={<Vat />} />
          <Route path="opening" element={<Opening />} />
          <Route path="periods" element={<Periods />} />
          <Route path="period-end" element={<PeriodEnd />} />
          <Route path="audit" element={<Audit />} />
          <Route path="bank-rules" element={<BankRules />} />
          <Route path="users" element={<Users />} />
          <Route path="items" element={<Items />} />
          <Route path="inventory" element={<Inventory />} />
          <Route path="assets" element={<Assets />} />
          <Route path="production" element={<Production />} />
          <Route path="receipts" element={<Receipts />} />
        </Route>
      </Route>
    </Routes>
  );
}

function Shell() {
  const { displayName, signOut } = useAuth();
  return (
    <div className="shell">
      <aside className="side">
        <div className="brand">KestrelBooks</div>
        <nav>
          <NavLink to="/practice">Practice overview</NavLink>
          <NavLink to="/clients">Clients</NavLink>
        </nav>
        <div className="foot">
          {displayName}<br />
          <button className="btn ghost" style={{ marginTop: 8, color: '#cfd3d8' }}
            onClick={() => { void signOut(); }}>Sign out</button>
        </div>
      </aside>
      <main className="main"><Outlet /></main>
    </div>
  );
}

const TAB_GROUPS: [string, [string, string][]][] = [
  ['Review', [['reports', 'Reports'], ['aged', 'Ageing'], ['journals', 'Journals'],
    ['period-end', 'Accruals']]],
  ['Sales & purchases', [['invoices', 'Invoices'], ['credit-notes', 'Credit notes'],
    ['recurring', 'Recurring'], ['contacts', 'Contacts']]],
  ['Money', [['money', 'Money'], ['banking', 'Banking'], ['bank-rules', 'Rules'], ['receipts', 'Receipts']]],
  ['Stock & assets', [['items', 'Items'], ['inventory', 'Inventory'],
    ['production', 'Production'], ['assets', 'Assets']]],
  ['Compliance', [['vat', 'VAT'], ['periods', 'Periods'], ['opening', 'Opening'],
    ['users', 'Users'], ['audit', 'Audit trail']]],
];

function ClientShell() {
  const { businessId } = useParams();
  return (
    <div>
      <nav className="clienttabs">
        {TAB_GROUPS.map(([group, tabs]) => (
          <div key={group} className="tabgroup">
            <span className="tabgroup-label">{group}</span>
            {tabs.map(([path, label]) =>
              <NavLink key={path} to={`/clients/${businessId}/${path}`}>{label}</NavLink>)}
          </div>
        ))}
      </nav>
      <Outlet />
    </div>
  );
}
