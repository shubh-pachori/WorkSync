export type Role = 'Employee' | 'Manager' | 'Admin';

export interface User {
  id: string;
  fullName: string;
  email: string;
  role: Role;
  managerId: string | null;
  totpEnabled: boolean;
}

/**
 * The login response. When requiresTotp is true only mfaToken is set: the password was
 * accepted but no session exists yet. The refresh token is never in this body — it arrives
 * as an httpOnly cookie.
 */
export interface AuthResponse {
  requiresTotp: boolean;
  mfaToken: string | null;
  user: User | null;
  accessToken: string | null;
  expiresAtUtc: string | null;
}

export interface TotpStatus {
  enabled: boolean;
  enabledAt: string | null;
  recoveryCodesRemaining: number;
}

export interface TotpSetup {
  /** Base32 secret, for users who cannot scan the QR code. */
  secret: string;
  /** otpauth:// URI rendered as the QR code. */
  otpAuthUri: string;
}

export interface RecoveryCodes {
  codes: string[];
}

export interface ConnectionStatus {
  provider: string;
  isConnected: boolean;
  connectedAt: string | null;
  lastError: string | null;
}

export interface TimesheetEntry {
  id: string;
  date: string;
  description: string;
  hours: number;
  devHours: number;
  meetingHours: number;
  reviewHours: number;
  isEdited: boolean;
}

export type TimesheetStatus = 'Draft' | 'Generated' | 'Submitted' | 'Approved' | 'Rejected';

export interface Timesheet {
  id: string;
  userId: string;
  weekStartDate: string;
  weekEndDate: string;
  status: TimesheetStatus;
  weeklySummary: string | null;
  entries: TimesheetEntry[];
  missingHourPrompts: string[];
  totalHours: number;
}

export interface Activity {
  id: string;
  source: string;
  title: string;
  status: string | null;
  activityDate: string;
  estimatedHours: number | null;
}

export interface TeamAnalytics {
  byStatus: Record<string, number>;
  weeklyHours: { week: string; totalHours: number }[];
  perEmployee: { employee: string; totalHours: number; submitted: number; approved: number }[];
}

/** RFC 7807 problem document returned by both services. */
export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}
