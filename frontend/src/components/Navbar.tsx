import { NavLink } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';

export default function Navbar() {
  const { user, isManager, logout } = useAuth();
  if (!user) return null;

  const linkClass = ({ isActive }: { isActive: boolean }) => (isActive ? 'active' : '');

  return (
    <aside className="sidebar">
      <div className="brand">
        Timesheet<small>AI Generator</small>
      </div>

      <nav>
        <NavLink to="/" end className={linkClass}>Dashboard</NavLink>
        <NavLink to="/connect" className={linkClass}>Connect Accounts</NavLink>
        <NavLink to="/chat" className={linkClass}>AI Chat</NavLink>
        <NavLink to="/security" className={linkClass}>
          Security
          {/* A quiet nudge rather than a modal: enrolment is opt-in. */}
          {!user.totpEnabled && <span className="nav-badge" title="Two-factor authentication is off">!</span>}
        </NavLink>
        {/* Analytics is a manager screen; it used to be shown to everyone and simply
            returned an empty page for employees. */}
        {isManager && <NavLink to="/analytics" className={linkClass}>Team &amp; Approvals</NavLink>}
      </nav>

      <div className="user-chip">
        <div className="user-chip-row">
          <div className="avatar" aria-hidden="true">
            {user.fullName.split(' ').map(part => part[0]).join('').slice(0, 2).toUpperCase()}
          </div>
          <div>
            <strong className="user-name">{user.fullName}</strong>
            <span className="user-role">{user.role}</span>
          </div>
        </div>

        <div className="user-email">{user.email}</div>

        <button className="btn btn-outline btn-sm" style={{ width: '100%' }} onClick={() => void logout()}>
          Sign out
        </button>
      </div>
    </aside>
  );
}
