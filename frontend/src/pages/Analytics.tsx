import { useCallback, useEffect, useState } from 'react';
import { AnalyticsApi, ApprovalApi } from '../api/timesheetApi';
import { describeApiError } from '../api/client';
import HourChart from '../components/HourChart';
import type { TeamAnalytics, Timesheet } from '../types';

/** Step 7–8: manager dashboard — pending approvals plus team productivity. */
export default function Analytics() {
  const [pending, setPending] = useState<Timesheet[]>([]);
  const [analytics, setAnalytics] = useState<TeamAnalytics | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [deciding, setDeciding] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [p, a] = await Promise.all([ApprovalApi.getPending(), AnalyticsApi.getTeamAnalytics()]);
      setPending(p);
      setAnalytics(a);
      setError('');
    } catch (err) {
      setError(describeApiError(err, 'Could not load team data.'));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const decide = async (timesheetId: string, approve: boolean) => {
    setDeciding(timesheetId);
    try {
      await ApprovalApi.decide(timesheetId, approve);
      await load();
    } catch (err) {
      setError(describeApiError(err, 'Could not record that decision.'));
    } finally {
      setDeciding(null);
    }
  };

  const weeklyLabels = analytics?.weeklyHours.map(w => w.week) ?? [];
  const weeklyData = analytics?.weeklyHours.map(w => w.totalHours) ?? [];

  return (
    <>
      <div className="page-header">
        <span className="eyebrow">Step 7–8 · Manager Dashboard</span>
        <h1>Team analytics &amp; approvals</h1>
        <p>Review submitted timesheets and track team productivity.</p>
      </div>

      {error && <div className="card card-error" role="alert">{error}</div>}

      <div className="card">
        <strong style={{ fontSize: 15 }}>Pending approvals</strong>

        {loading ? (
          <p className="muted">Loading…</p>
        ) : pending.length === 0 ? (
          <p className="muted">Nothing waiting on you right now.</p>
        ) : (
          <table className="ts-table" style={{ marginTop: 12 }}>
            <thead>
              <tr><th>Employee &amp; week</th><th>Total hours</th><th></th></tr>
            </thead>
            <tbody>
              {pending.map(t => (
                <tr key={t.id}>
                  <td>
                    <div>{t.weeklySummary?.split(' — ')[0]}</div>
                    <div className="muted">{t.weekStartDate} → {t.weekEndDate}</div>
                  </td>
                  <td>{t.totalHours}h</td>
                  <td style={{ display: 'flex', gap: 8 }}>
                    <button
                      className="btn btn-accent btn-sm"
                      disabled={deciding === t.id}
                      onClick={() => void decide(t.id, true)}
                    >
                      Approve
                    </button>
                    <button
                      className="btn btn-outline btn-sm"
                      disabled={deciding === t.id}
                      onClick={() => void decide(t.id, false)}
                    >
                      Reject
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {weeklyLabels.length > 0 && (
        <div className="card">
          <strong style={{ fontSize: 15 }}>Weekly hours across team</strong>
          <div style={{ marginTop: 12 }}>
            <HourChart labels={weeklyLabels} data={weeklyData} label="Total hours" />
          </div>
        </div>
      )}

      {analytics && analytics.perEmployee.length > 0 && (
        <div className="card">
          <strong style={{ fontSize: 15 }}>Per employee</strong>
          <table className="ts-table" style={{ marginTop: 12 }}>
            <thead>
              <tr><th>Employee</th><th>Total hours</th><th>Submitted</th><th>Approved</th></tr>
            </thead>
            <tbody>
              {analytics.perEmployee.map(row => (
                <tr key={row.employee}>
                  <td>{row.employee}</td>
                  <td>{row.totalHours}h</td>
                  <td>{row.submitted}</td>
                  <td>{row.approved}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}
