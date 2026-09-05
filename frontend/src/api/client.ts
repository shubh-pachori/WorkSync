import axios, { AxiosError, AxiosRequestConfig } from 'axios';
import type { ProblemDetails } from '../types';

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5080/api';

export const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: { 'Content-Type': 'application/json' },
  // Required for the httpOnly refresh cookie to be sent and received cross-origin.
  withCredentials: true
});

/**
 * The access token lives in a module variable, never in sessionStorage. It is short-lived
 * (15 minutes) and an XSS bug cannot read it out of storage; the long-lived refresh token
 * is an httpOnly cookie that JavaScript cannot see at all.
 */
let accessToken: string | null = null;
let onUnauthorized: (() => void) | null = null;
let refreshHandler: (() => Promise<string | null>) | null = null;

/** De-duplicates concurrent refreshes: ten parallel 401s trigger one refresh call. */
let inFlightRefresh: Promise<string | null> | null = null;

export function setAccessToken(token: string | null): void {
  accessToken = token;
}

export function getAccessToken(): string | null {
  return accessToken;
}

/** Called when the session is definitively over, so the app can show the login screen. */
export function setUnauthorizedHandler(handler: (() => void) | null): void {
  onUnauthorized = handler;
}

/** Supplied by AuthContext: performs the refresh and returns the new access token. */
export function setRefreshHandler(handler: (() => Promise<string | null>) | null): void {
  refreshHandler = handler;
}

/** Auth endpoints must never trigger a refresh — that would recurse. */
function isAuthFlowRequest(url: string | undefined): boolean {
  if (!url) return false;
  return url.includes('/auth/refresh')
    || url.includes('/auth/login')
    || url.includes('/auth/logout');
}

apiClient.interceptors.request.use(config => {
  if (accessToken) {
    config.headers.Authorization = `Bearer ${accessToken}`;
  }
  return config;
});

apiClient.interceptors.response.use(
  response => response,
  async (error: AxiosError<ProblemDetails>) => {
    const original = error.config as (AxiosRequestConfig & { _retried?: boolean }) | undefined;

    // A 401 on a normal call usually means the 15-minute access token expired. Refresh
    // once, transparently, and replay the request; the user sees nothing.
    if (
      error.response?.status === 401 &&
      original &&
      !original._retried &&
      !isAuthFlowRequest(original.url) &&
      refreshHandler
    ) {
      original._retried = true;

      inFlightRefresh ??= refreshHandler().finally(() => {
        inFlightRefresh = null;
      });

      const token = await inFlightRefresh;

      if (token) {
        original.headers = { ...original.headers, Authorization: `Bearer ${token}` };
        return apiClient(original);
      }

      // Refresh failed: the refresh token expired, was revoked, or reuse was detected.
      onUnauthorized?.();
    }

    return Promise.reject(error);
  }
);

/** Pulls a human-readable message out of an RFC 7807 body, with sensible fallbacks. */
export function describeApiError(error: unknown, fallback = 'Something went wrong.'): string {
  if (!axios.isAxiosError(error)) return fallback;

  const problem = error.response?.data as ProblemDetails | undefined;

  if (problem?.errors) {
    const first = Object.values(problem.errors).flat()[0];
    if (first) return first;
  }

  if (problem?.detail) return problem.detail;
  if (problem?.title) return problem.title;

  if (error.response?.status === 429) {
    return 'Too many attempts. Wait a minute and try again.';
  }

  if (error.code === 'ERR_NETWORK') {
    return 'Could not reach the API. Is the gateway running on :5080?';
  }

  return fallback;
}
