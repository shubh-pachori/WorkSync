import { apiClient } from './client';
import type {
  Activity, AuthResponse, ConnectionStatus, RecoveryCodes, TeamAnalytics,
  Timesheet, TotpSetup, TotpStatus, User
} from '../types';

export const AuthApi = {
  /** Step one. May come back with requiresTotp instead of a session. */
  login: (email: string, password: string) =>
    apiClient.post<AuthResponse>('/auth/login', { email, password }).then(r => r.data),

  /** Step two: an authenticator code, or a recovery code. */
  loginTotp: (mfaToken: string, code: string) =>
    apiClient.post<AuthResponse>('/auth/login/totp', { mfaToken, code }).then(r => r.data),

  /** Exchanges the refresh cookie for a new access token. Also restores a session on load. */
  refresh: () => apiClient.post<AuthResponse>('/auth/refresh').then(r => r.data),

  me: () => apiClient.get<User>('/auth/me').then(r => r.data),

  logout: () => apiClient.post('/auth/logout').then(() => undefined)
};

export const TwoFactorApi = {
  status: () => apiClient.get<TotpStatus>('/auth/totp/status').then(r => r.data),

  /** Begins enrolment; 2FA stays off until a code confirms it. */
  beginSetup: () => apiClient.post<TotpSetup>('/auth/totp/setup').then(r => r.data),

  enable: (code: string) =>
    apiClient.post<RecoveryCodes>('/auth/totp/enable', { code }).then(r => r.data),

  disable: (password: string, code: string) =>
    apiClient.post('/auth/totp/disable', { password, code }).then(() => undefined),

  regenerateRecoveryCodes: (code: string) =>
    apiClient.post<RecoveryCodes>('/auth/totp/recovery-codes', { code }).then(r => r.data)
};

export const IntegrationApi = {
  // No user id: the server uses the caller's token.
  connect: (provider: string, accessToken: string) =>
    apiClient.post('/integrations/connect', { provider, accessToken }).then(r => r.data),

  status: () => apiClient.get<ConnectionStatus[]>('/integrations/status').then(r => r.data),

  disconnect: (provider: string) =>
    apiClient.delete(`/integrations/${provider}`).then(r => r.data)
};

export const TimesheetApi = {
  generate: (weekStartDate: string) =>
    apiClient.post<Timesheet>('/timesheets/generate', { weekStartDate }).then(r => r.data),

  getMine: () => apiClient.get<Timesheet[]>('/timesheets/mine').then(r => r.data),

  getById: (id: string) => apiClient.get<Timesheet>(`/timesheets/${id}`).then(r => r.data),

  updateEntry: (timesheetId: string, entryId: string, hours: number, description: string) =>
    apiClient.put(`/timesheets/${timesheetId}/entries/${entryId}`, { hours, description }).then(r => r.data),

  submit: (id: string) => apiClient.post(`/timesheets/${id}/submit`).then(r => r.data)
};

export const ApprovalApi = {
  getPending: () => apiClient.get<Timesheet[]>('/approvals/pending').then(r => r.data),

  decide: (timesheetId: string, approve: boolean, comments?: string) =>
    apiClient.post(`/approvals/${timesheetId}/decision`, { approve, comments }).then(r => r.data)
};

export const ActivityApi = {
  getMine: () => apiClient.get<Activity[]>('/activities/mine').then(r => r.data)
};

export const AnalyticsApi = {
  getTeamAnalytics: () => apiClient.get<TeamAnalytics>('/analytics/team').then(r => r.data)
};

export const ChatApi = {
  ask: (question: string) =>
    apiClient.post<{ answer: string }>('/chat/ask', { question }).then(r => r.data)
};
