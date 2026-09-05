import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { TimesheetApi } from '../api/timesheetApi';
import { describeApiError } from '../api/client';
import { useCurrentUser } from '../auth/AuthContext';
import { weekStartOf } from '../utils/week';
import type { Timesheet } from '../types';

export default function Dashboard() {
  const user = useCurrentUser();
  const navigate = useNavigate();

  const [timesheets, setTimesheets] = useState<Timesheet[]>([]);
  const [loading, setLoading] = useState(true);
  const [generating, setGenerating] = useState(false);
  const [error, setError] = useState('');

  const weekStart = weekStartOf();

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      setTimesheets(await TimesheetApi.getMine());
    } catch (err) {
      // Previously this had no catch at all, so any failure left the page on "Loading…".
      setError(describeApiError(err, 'Could not load your timesheets.'));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const generate = async () => {
    setGenerating(true);
    setError('');
    try {
      const sheet = await TimesheetApi.generate(weekStart);
      navigate(`/timesheet/${sheet.id}`);
    } catch (err) {
      setError(describeApiError(err, 'Could not generate this week’s timesheet.'));
    } finally {
      setGenerating(false);
    }
  };

  return (
    <>
      <div className="page-header">
        <span className="eyebrow">Step 3–4 · Fetch + AI Processing</span>
        <h1>Welcome back, {user.fullName.split(' ')[0]}</h1>
        <p>Generate this week's timesheet from your commits, tickets and meetings in one click.</p>
      </div>

      {error && <div className="card card-error" role="alert">{error}</div>}

      <div className="card card-split">
        <div>
          <strong style={{ fontSize: 15 }}>Week of {weekStart}</strong>
          <p className="muted">
            Pulls from every connected tool — GitHub, Jira, Azure DevOps, Outlook.
            Regenerating replaces this week rather than adding a second copy.
          </p>
        </div>
        <button className="btn btn-accent" onClick={generate} disabled={generating}>
          {generating ? 'Generating…' : 'Generate this week’s timesheet'}
        </button>
      </div>

      <div className="card">
        <strong style={{ fontSize: 15 }}>Your timesheets</strong>

        {loading ? (
          <p className="muted">Loading…</p>
        ) : timesheets.length === 0 ? (
          <p className="muted">No timesheets yet — generate your first one above.</p>
        ) : (
          <table className="ts-table" style={{ marginTop: 12 }}>
            <thead>
              <tr>
                <th>Week</th>
                <th>Status</th>
                <th>Total hours</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {timesheets.map(t => (
                <tr key={t.id}>
                  <td>{t.weekStartDate} → {t.weekEndDate}</td>
                  <td><span className={`status-pill status-${t.status}`}>{t.status}</span></td>
                  <td>{t.totalHours}h</td>
                  <td>
                    <button className="btn btn-outline btn-sm" onClick={() => navigate(`/timesheet/${t.id}`)}>
                      Open
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </>
  );
}
