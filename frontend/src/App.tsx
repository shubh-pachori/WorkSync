import { Navigate, Route, Routes } from 'react-router-dom';
import { useAuth } from './auth/AuthContext';
import Login from './pages/Login';
import Navbar from './components/Navbar';
import Dashboard from './pages/Dashboard';
import ConnectAccounts from './pages/ConnectAccounts';
import TimesheetReview from './pages/TimesheetReview';
import Analytics from './pages/Analytics';
import ChatAssistant from './pages/ChatAssistant';
import Security from './pages/Security';

/** Route guard: manager-only screens are no longer reachable by employees. */
function ManagerRoute({ children }: { children: React.ReactNode }) {
  const { isManager } = useAuth();
  return isManager ? <>{children}</> : <Navigate to="/" replace />;
}

export default function App() {
  const { user, ready } = useAuth();

  // Avoid flashing the login screen while a stored session is being restored.
  if (!ready) {
    return <div className="login-screen"><div className="login-card"><p>Loading…</p></div></div>;
  }

  if (!user) {
    return <Login />;
  }

  return (
    <div className="app-shell">
      <Navbar />
      <div className="main">
        <Routes>
          <Route path="/" element={<Dashboard />} />
          <Route path="/connect" element={<ConnectAccounts />} />
          <Route path="/timesheet/:id" element={<TimesheetReview />} />
          <Route path="/chat" element={<ChatAssistant />} />
          <Route path="/security" element={<Security />} />
          <Route path="/analytics" element={<ManagerRoute><Analytics /></ManagerRoute>} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </div>
    </div>
  );
}
