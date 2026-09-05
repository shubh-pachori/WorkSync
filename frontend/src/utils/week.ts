/**
 * Monday of the week containing `date`, as a plain YYYY-MM-DD string.
 *
 * The previous implementation computed the Monday in local time and then serialised it
 * with toISOString(), which is UTC. For anyone east of UTC before ~05:30 IST that shifted
 * the result back a day, so "this week" became the previous Sunday and a second timesheet
 * was created for the same week. Formatting the local date parts directly avoids the
 * timezone round-trip entirely; the server also snaps whatever it receives to a Monday.
 */
export function weekStartOf(date: Date = new Date()): string {
  const day = date.getDay(); // 0 = Sunday
  const monday = new Date(
    date.getFullYear(),
    date.getMonth(),
    date.getDate() - day + (day === 0 ? -6 : 1)
  );
  return toIsoDate(monday);
}

export function toIsoDate(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** Formats a YYYY-MM-DD string without letting the browser reinterpret it as UTC. */
export function formatDayLabel(isoDate: string): string {
  const [year, month, day] = isoDate.split('-').map(Number);
  if (!year || !month || !day) return isoDate;

  return new Date(year, month - 1, day).toLocaleDateString(undefined, {
    weekday: 'long',
    month: 'short',
    day: 'numeric'
  });
}
