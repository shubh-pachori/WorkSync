import { useState } from 'react';
import { formatDayLabel } from '../utils/week';
import type { TimesheetEntry } from '../types';

interface Props {
  entries: TimesheetEntry[];
  editable: boolean;
  onSave: (entryId: string, hours: number, description: string) => Promise<void>;
}

export default function TimesheetTable({ entries, editable, onSave }: Props) {
  const [editingId, setEditingId] = useState<string | null>(null);
  const [draftHours, setDraftHours] = useState(0);
  const [draftDesc, setDraftDesc] = useState('');
  const [saving, setSaving] = useState(false);

  const startEdit = (entry: TimesheetEntry) => {
    setEditingId(entry.id);
    setDraftHours(entry.hours);
    setDraftDesc(entry.description);
  };

  const save = async (id: string) => {
    setSaving(true);
    try {
      await onSave(id, draftHours, draftDesc);
      setEditingId(null);
    } finally {
      setSaving(false);
    }
  };

  const totalHours = Math.round(entries.reduce((sum, e) => sum + e.hours, 0) * 100) / 100;

  return (
    <table className="ts-table">
      <thead>
        <tr>
          <th style={{ width: '18%' }}>Date</th>
          <th>AI-generated activity</th>
          <th style={{ width: '10%' }}>Hours</th>
          {editable && <th style={{ width: '10%' }}></th>}
        </tr>
      </thead>
      <tbody>
        {entries.map(e => (
          <tr key={e.id}>
            <td>{formatDayLabel(e.date)}</td>
            <td>
              {editingId === e.id ? (
                <textarea
                  className="desc-edit"
                  value={draftDesc}
                  onChange={ev => setDraftDesc(ev.target.value)}
                  aria-label="Activity description"
                />
              ) : (
                <>
                  {e.description}
                  {e.isEdited && <span className="edited-flag">(edited)</span>}
                </>
              )}
            </td>
            <td>
              {editingId === e.id ? (
                <input
                  className="hours-input"
                  type="number"
                  step="0.5"
                  min="0"
                  max="24"
                  value={draftHours}
                  onChange={ev => setDraftHours(Math.min(24, Math.max(0, parseFloat(ev.target.value) || 0)))}
                  aria-label="Hours"
                />
              ) : (
                <span className="hours-badge">{e.hours}h</span>
              )}
            </td>
            {editable && (
              <td>
                {editingId === e.id ? (
                  <div style={{ display: 'flex', gap: 6 }}>
                    <button className="btn btn-accent btn-sm" disabled={saving} onClick={() => void save(e.id)}>
                      {saving ? '…' : 'Save'}
                    </button>
                    <button className="btn btn-outline btn-sm" disabled={saving} onClick={() => setEditingId(null)}>
                      Cancel
                    </button>
                  </div>
                ) : (
                  <button className="btn btn-outline btn-sm" onClick={() => startEdit(e)}>Edit</button>
                )}
              </td>
            )}
          </tr>
        ))}
        <tr className="total-row">
          <td></td>
          <td style={{ fontWeight: 600 }}>Total</td>
          <td style={{ fontWeight: 600 }}>{totalHours}h</td>
          {editable && <td></td>}
        </tr>
      </tbody>
    </table>
  );
}
