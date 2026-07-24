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
          <Route path="users" element={<Users />} />
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

const TABS: [string, string][] = [
  ['reports', 'Reports'], ['aged', 'Aged'], ['invoices', 'Invoices'],
  ['credit-notes', 'Credit notes'], ['money', 'Money'], ['banking', 'Banking'],
  ['journals', 'Journals'], ['contacts', 'Contacts'], ['recurring', 'Recurring'],
  ['vat', 'VAT'], ['opening', 'Opening'], ['periods', 'Periods'], ['users', 'Users'],
];

function ClientShell() {
  const { businessId } = useParams();
  return (
    <div>
      <nav style={{ marginBottom: 18, display: 'flex', gap: 14, flexWrap: 'wrap' }}>
        {TABS.map(([path, label]) =>
          <NavLink key={path} to={`/clients/${businessId}/${path}`}>{label}</NavLink>)}
      </nav>
      <Outlet />
    </div>
  );
}
