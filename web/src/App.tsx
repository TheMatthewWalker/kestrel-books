import { Navigate, NavLink, Outlet, Route, Routes, useParams } from 'react-router-dom';
import { useAuth } from './auth';
import Login from './pages/Login';
import Practice from './pages/Practice';
import Clients from './pages/Clients';
import Reports from './pages/Reports';
import Aged from './pages/Aged';
import Invoices from './pages/Invoices';

export default function App() {
  const { signedIn } = useAuth();
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
          <button className="btn ghost" style={{ marginTop: 8, color: '#cfd3d8' }} onClick={signOut}>
            Sign out
          </button>
        </div>
      </aside>
      <main className="main"><Outlet /></main>
    </div>
  );
}

/** Per-client sub-navigation. Deeper modules (journals, banking, VAT…) follow the same pattern. */
function ClientShell() {
  const { businessId } = useParams();
  return (
    <div>
      <nav style={{ marginBottom: 18, display: 'flex', gap: 14 }}>
        <NavLink to={`/clients/${businessId}/reports`}>Reports</NavLink>
        <NavLink to={`/clients/${businessId}/aged`}>Aged debtors</NavLink>
        <NavLink to={`/clients/${businessId}/invoices`}>Invoices</NavLink>
      </nav>
      <Outlet />
    </div>
  );
}
