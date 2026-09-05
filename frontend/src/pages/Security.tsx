import { useCallback, useEffect, useState } from 'react';
import QRCode from 'qrcode';
import { TwoFactorApi } from '../api/timesheetApi';
import { describeApiError } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import type { TotpSetup, TotpStatus } from '../types';

type Stage = 'idle' | 'scanning' | 'codes';

/**
 * Two-factor enrolment.
 *
 * Enrolment is deliberately two-phase: /totp/setup stores a secret but leaves 2FA off, and
 * only /totp/enable — which requires a live code — turns it on. A user who closes this page
 * halfway through is not locked out of their account.
 */
export default function Security() {
  const { logout, reloadUser } = useAuth();

  const [status, setStatus] = useState<TotpStatus | null>(null);
  const [stage, setStage] = useState<Stage>('idle');
  const [setup, setSetup] = useState<TotpSetup | null>(null);
  const [qrDataUrl, setQrDataUrl] = useState<string | null>(null);
  const [recoveryCodes, setRecoveryCodes] = useState<string[]>([]);

  const [code, setCode] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');

  const loadStatus = useCallback(async () => {
    try {
      setStatus(await TwoFactorApi.status());
    } catch (err) {
      setError(describeApiError(err, 'Could not load your security settings.'));
    }
  }, []);

  useEffect(() => { void loadStatus(); }, [loadStatus]);

  const beginSetup = async () => {
    setBusy(true);
    setError('');
    try {
      const result = await TwoFactorApi.beginSetup();
      setSetup(result);
      // Rendered locally — the secret never goes to a third-party QR service.
      setQrDataUrl(await QRCode.toDataURL(result.otpAuthUri, { width: 220, margin: 1 }));
      setStage('scanning');
    } catch (err) {
      setError(describeApiError(err, 'Could not start setup.'));
    } finally {
      setBusy(false);
    }
  };

  const confirmEnable = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError('');
    try {
      const result = await TwoFactorApi.enable(code.trim());
      setRecoveryCodes(result.codes);
      setStage('codes');
      setCode('');
      await loadStatus();
      await reloadUser();
    } catch (err) {
      setError(describeApiError(err, 'That code was not accepted.'));
    } finally {
      setBusy(false);
    }
  };

  const disable = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError('');
    try {
      await TwoFactorApi.disable(password, code.trim());
      setPassword('');
      setCode('');
      setStage('idle');
      setNotice('Two-factor authentication is off. You have been signed out of other devices.');
      await loadStatus();
      await reloadUser();
    } catch (err) {
      setError(describeApiError(err, 'Could not turn off two-factor authentication.'));
    } finally {
      setBusy(false);
    }
  };

  const regenerate = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError('');
    try {
      const result = await TwoFactorApi.regenerateRecoveryCodes(code.trim());
      setRecoveryCodes(result.codes);
      setStage('codes');
      setCode('');
      await loadStatus();
    } catch (err) {
      setError(describeApiError(err, 'Could not regenerate your recovery codes.'));
    } finally {
      setBusy(false);
    }
  };

  const downloadCodes = () => {
    const blob = new Blob(
      [`AI Timesheet recovery codes\nGenerated ${new Date().toISOString()}\n\n${recoveryCodes.join('\n')}\n`],
      { type: 'text/plain' }
    );

    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = 'ai-timesheet-recovery-codes.txt';
    link.click();
    URL.revokeObjectURL(url);
  };

  return (
    <>
      <div className="page-header">
        <span className="eyebrow">Account · Security</span>
        <h1>Two-factor authentication</h1>
        <p>Protect your account with a code from an authenticator app on your phone.</p>
      </div>

      {error && <div className="card card-error" role="alert">{error}</div>}
      {notice && <div className="card card-accent">{notice}</div>}

      {/* ---- The recovery codes, shown exactly once ---- */}
      {stage === 'codes' && (
        <div className="card card-warn">
          <strong className="card-warn-title">Save your recovery codes</strong>
          <p className="muted">
            Each code works once, and this is the only time they are shown. Store them
            somewhere safe — they are how you get in if you lose your phone.
          </p>

          <ul className="recovery-grid">
            {recoveryCodes.map(c => <li key={c}><code>{c}</code></li>)}
          </ul>

          <div style={{ display: 'flex', gap: 10, marginTop: 12 }}>
            <button className="btn btn-outline btn-sm" onClick={downloadCodes}>Download</button>
            <button className="btn btn-accent btn-sm" onClick={() => { setStage('idle'); setRecoveryCodes([]); }}>
              I've saved them
            </button>
          </div>
        </div>
      )}

      {/* ---- Enrolment ---- */}
      {stage === 'scanning' && setup && (
        <div className="card">
          <strong style={{ fontSize: 15 }}>Scan this with your authenticator app</strong>
          <p className="muted">
            Google Authenticator, Microsoft Authenticator, Authy, 1Password — any of them.
          </p>

          <div className="totp-setup">
            {qrDataUrl && <img className="totp-qr" src={qrDataUrl} alt="Two-factor setup QR code" />}

            <div>
              <p className="muted" style={{ marginTop: 0 }}>Can't scan it? Enter this key by hand:</p>
              <code className="totp-secret">{setup.secret}</code>

              <form onSubmit={confirmEnable} style={{ marginTop: 16 }}>
                <div className="field">
                  <label htmlFor="enable-code">Enter the six-digit code to confirm</label>
                  <input
                    id="enable-code"
                    className="code-input"
                    inputMode="numeric"
                    autoComplete="one-time-code"
                    value={code}
                    onChange={e => setCode(e.target.value)}
                    placeholder="123456"
                    maxLength={6}
                    required
                  />
                </div>

                <div style={{ display: 'flex', gap: 10 }}>
                  <button className="btn btn-accent" disabled={busy}>
                    {busy ? 'Verifying…' : 'Turn on two-factor'}
                  </button>
                  <button type="button" className="btn btn-outline" onClick={() => setStage('idle')}>
                    Cancel
                  </button>
                </div>
              </form>
            </div>
          </div>
        </div>
      )}

      {/* ---- Current state ---- */}
      {stage === 'idle' && status && (
        <div className="card">
          <div className="card-split">
            <div>
              <div className="name">
                <span className={`dot ${status.enabled ? 'dot-on' : 'dot-off'}`} />
                {status.enabled ? 'Two-factor authentication is on' : 'Two-factor authentication is off'}
              </div>
              <p className="muted">
                {status.enabled
                  ? `Enabled ${status.enabledAt ? new Date(status.enabledAt).toLocaleDateString() : ''} · ${status.recoveryCodesRemaining} recovery codes left`
                  : 'Your account is protected by a password alone.'}
              </p>
            </div>

            {!status.enabled && (
              <button className="btn btn-accent" onClick={beginSetup} disabled={busy}>
                {busy ? 'Preparing…' : 'Set up'}
              </button>
            )}
          </div>

          {status.enabled && status.recoveryCodesRemaining <= 2 && (
            <p className="form-error" style={{ marginTop: 12 }}>
              Only {status.recoveryCodesRemaining} recovery codes left. Generate a new set below.
            </p>
          )}

          {status.enabled && (
            <div className="totp-manage">
              <form onSubmit={regenerate} className="totp-form">
                <strong>New recovery codes</strong>
                <p className="muted">Replaces any unused codes you still have.</p>
                <div className="field">
                  <label htmlFor="regen-code">Authenticator code</label>
                  <input
                    id="regen-code"
                    className="code-input"
                    inputMode="text"
                    value={code}
                    onChange={e => setCode(e.target.value)}
                    placeholder="123456"
                    required
                  />
                </div>
                <button className="btn btn-outline btn-sm" disabled={busy}>Generate</button>
              </form>

              <form onSubmit={disable} className="totp-form">
                <strong>Turn off two-factor</strong>
                <p className="muted">Needs your password as well as a code.</p>
                <div className="field">
                  <label htmlFor="disable-password">Password</label>
                  <input
                    id="disable-password"
                    type="password"
                    autoComplete="current-password"
                    value={password}
                    onChange={e => setPassword(e.target.value)}
                    required
                  />
                </div>
                <div className="field">
                  <label htmlFor="disable-code">Authenticator or recovery code</label>
                  <input
                    id="disable-code"
                    className="code-input"
                    inputMode="text"
                    value={code}
                    onChange={e => setCode(e.target.value)}
                    required
                  />
                </div>
                <button className="btn btn-outline btn-sm" disabled={busy}>Turn off</button>
              </form>
            </div>
          )}
        </div>
      )}

      <div className="card">
        <strong style={{ fontSize: 15 }}>Sessions</strong>
        <p className="muted">
          Signing out ends this session everywhere it was started from. Turning two-factor on
          or off also signs out every other device.
        </p>
        <button className="btn btn-outline btn-sm" style={{ marginTop: 10 }} onClick={() => void logout()}>
          Sign out
        </button>
      </div>
    </>
  );
}
