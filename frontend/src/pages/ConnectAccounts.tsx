import { useCallback, useEffect, useState } from 'react';
import { IntegrationApi } from '../api/timesheetApi';
import { describeApiError } from '../api/client';
import type { ConnectionStatus } from '../types';

const PROVIDER_META: Record<string, { label: string; sub: string }> = {
  GitHub: { label: 'GitHub', sub: 'Commits, pull requests, code reviews' },
  AzureDevOps: { label: 'Azure DevOps', sub: 'Work items, boards, pull requests' },
  Jira: { label: 'Jira', sub: 'Issues, sprints, ticket status' },
  OutlookCalendar: { label: 'Outlook Calendar', sub: 'Meetings & events' },
  TeamsCalendar: { label: 'Teams Calendar', sub: 'Calls & meetings (optional)' }
};

/**
 * Step 2: Connect Accounts.
 *
 * In production "Connect" opens each provider's OAuth consent screen. For the demo it
 * stores a placeholder token. A provider whose last sync failed now says so instead of
 * quietly showing a green dot while the backend substituted mock data.
 */
export default function ConnectAccounts() {
  const [statuses, setStatuses] = useState<ConnectionStatus[]>([]);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    try {
      setStatuses(await IntegrationApi.status());
      setError('');
    } catch (err) {
      setError(describeApiError(err, 'Could not load your connections.'));
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const toggle = async (provider: string, isConnected: boolean) => {
    setBusy(provider);
    try {
      if (isConnected) {
        await IntegrationApi.disconnect(provider);
      } else {
        await IntegrationApi.connect(provider, `demo-token-${provider.toLowerCase()}`);
      }
      await load();
    } catch (err) {
      setError(describeApiError(err, `Could not update ${provider}.`));
    } finally {
      setBusy(null);
    }
  };

  return (
    <>
      <div className="page-header">
        <span className="eyebrow">Step 2 · Connect Accounts</span>
        <h1>Connect your work tools</h1>
        <p>The AI engine only reads activity from tools you explicitly connect.</p>
      </div>

      {error && <div className="card card-error" role="alert">{error}</div>}

      <div className="card">
        {Object.entries(PROVIDER_META).map(([key, meta]) => {
          const status = statuses.find(s => s.provider === key);
          const isConnected = status?.isConnected ?? false;
          const hasError = Boolean(status?.lastError);

          return (
            <div className="provider-tile" key={key}>
              <div>
                <div className="name">
                  <span className={`dot ${!isConnected ? 'dot-off' : hasError ? 'dot-warn' : 'dot-on'}`} />
                  {meta.label}
                </div>
                <div className="sub">{meta.sub}</div>
                {hasError && <div className="sub sub-error">{status?.lastError}</div>}
              </div>

              <button
                className={isConnected ? 'btn btn-outline btn-sm' : 'btn btn-primary btn-sm'}
                disabled={busy === key}
                onClick={() => void toggle(key, isConnected)}
              >
                {busy === key ? '…' : isConnected ? 'Disconnect' : 'Connect'}
              </button>
            </div>
          );
        })}
      </div>

      <p className="muted" style={{ marginTop: 12 }}>
        With nothing connected, generating a timesheet uses a built-in sample week so you can
        still walk through the flow.
      </p>
    </>
  );
}
