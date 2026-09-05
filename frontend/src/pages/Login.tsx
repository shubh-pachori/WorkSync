import { useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import { describeApiError } from '../api/client';

/**
 * Step 1: sign in.
 *
 * Two-step when the account has an authenticator enrolled: the password is checked first,
 * and the server hands back only a short-lived token until a valid code arrives.
 */
export default function Login() {
  const { login, completeTotp, cancelTotp, awaitingTotp } = useAuth();

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [code, setCode] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  const submitPassword = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!email || !password) return;

    setLoading(true);
    setError('');
    try {
      await login(email, password);
    } catch (err) {
      setError(describeApiError(err, 'Sign in failed.'));
    } finally {
      setLoading(false);
    }
  };

  const submitCode = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!code.trim()) return;

    setLoading(true);
    setError('');
    try {
      await completeTotp(code.trim());
    } catch (err) {
      setError(err instanceof Error && !('response' in err)
        ? err.message
        : describeApiError(err, 'That code was not accepted.'));
      setCode('');
    } finally {
      setLoading(false);
    }
  };

  const startOver = () => {
    cancelTotp();
    setCode('');
    setPassword('');
    setError('');
  };

  return (
    <div className="login-screen">
      <div className="login-card">
        <div className="login-mark">T</div>

        {awaitingTotp ? (
          <>
            <h1>Two-step verification</h1>
            <p>Enter the six-digit code from your authenticator app.</p>

            <form onSubmit={submitCode}>
              <div className="field">
                <label htmlFor="code">Verification code</label>
                <input
                  id="code"
                  className="code-input"
                  inputMode="text"
                  autoComplete="one-time-code"
                  autoFocus
                  value={code}
                  onChange={e => setCode(e.target.value)}
                  placeholder="123456"
                  maxLength={20}
                  required
                />
              </div>

              {error && <p className="form-error" role="alert">{error}</p>}

              <button className="btn btn-primary" style={{ width: '100%' }} disabled={loading}>
                {loading ? 'Verifying…' : 'Verify'}
              </button>
            </form>

            <p className="login-hint">
              Lost your device? Enter one of your recovery codes above instead.
              <br />
              <button type="button" className="link-button" onClick={startOver}>
                Start over
              </button>
            </p>
          </>
        ) : (
          <>
            <h1>AI Timesheet Generator</h1>
            <p>Sign in to auto-generate this week's timesheet from your commits, tickets and meetings.</p>

            <form onSubmit={submitPassword}>
              <div className="field">
                <label htmlFor="email">Work email</label>
                <input
                  id="email"
                  type="email"
                  autoComplete="username"
                  value={email}
                  onChange={e => setEmail(e.target.value)}
                  placeholder="priya@company.com"
                  required
                />
              </div>

              <div className="field">
                <label htmlFor="password">Password</label>
                <input
                  id="password"
                  type="password"
                  autoComplete="current-password"
                  value={password}
                  onChange={e => setPassword(e.target.value)}
                  placeholder="••••••••"
                  required
                />
              </div>

              {error && <p className="form-error" role="alert">{error}</p>}

              <button className="btn btn-primary" style={{ width: '100%' }} disabled={loading}>
                {loading ? 'Signing in…' : 'Sign in'}
              </button>
            </form>

            <p className="login-hint">
              Demo accounts — <strong>priya@company.com</strong> (employee) and{' '}
              <strong>sarah@company.com</strong> (manager), password <strong>Demo@123</strong>.
            </p>
          </>
        )}
      </div>
    </div>
  );
}
