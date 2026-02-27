import axios from 'axios';
import type {
  Credential,
  DashboardSummary,
  Note,
  OperationalLog,
  PagedResult,
  PollResult,
  PollSummary,
  ServerDetail,
  ServerListItem,
} from '../types';

const api = axios.create({
  baseURL: '/api',
  headers: { 'Content-Type': 'application/json' },
});

// Auth interceptor — attach JWT token from sessionStorage
api.interceptors.request.use((config) => {
  const token = sessionStorage.getItem('auth_token');
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

// Global error handler
api.interceptors.response.use(
  (res) => res,
  (err) => {
    if (err.response?.status === 401) {
      sessionStorage.removeItem('auth_token');
      window.location.href = '/login';
    }
    return Promise.reject(err);
  }
);

// ── Servers ───────────────────────────────────────────────────────────────────

export interface ServerListParams {
  environment?: string;
  criticality?: string;
  site?: string;
  status?: string;
  search?: string;
  tags?: string[];
  page?: number;
  pageSize?: number;
  sortBy?: string;
  sortDir?: string;
}

export const serversApi = {
  list: (params: ServerListParams = {}) =>
    api.get<PagedResult<ServerListItem>>('/servers', { params }).then((r) => r.data),

  get: (id: string) =>
    api.get<ServerDetail>(`/servers/${id}`).then((r) => r.data),

  create: (data: Record<string, unknown>) =>
    api.post<ServerDetail>('/servers', data).then((r) => r.data),

  update: (id: string, data: Record<string, unknown>) =>
    api.put<ServerDetail>(`/servers/${id}`, data).then((r) => r.data),

  delete: (id: string, hard = false) =>
    api.delete(`/servers/${id}`, { params: { hard } }),

  testConnectivity: (id: string, body: { collectorType: string; credentialRefId?: string }) =>
    api.post(`/servers/${id}/connectivity-test`, body).then((r) => r.data),

  triggerPoll: (id: string) =>
    api.post(`/servers/${id}/poll`).then((r) => r.data),
};

// ── Polls ─────────────────────────────────────────────────────────────────────

export const pollsApi = {
  listForServer: (serverId: string, params = {}) =>
    api.get<PagedResult<PollSummary>>(`/servers/${serverId}/polls`, { params }).then((r) => r.data),

  getLatest: (serverId: string) =>
    api.get<PollResult>(`/servers/${serverId}/polls/latest`).then((r) => r.data),

  get: (pollId: string) =>
    api.get<PollResult>(`/polls/${pollId}`).then((r) => r.data),
};

// ── Notes ─────────────────────────────────────────────────────────────────────

export const notesApi = {
  list: (serverId: string, params = {}) =>
    api.get<PagedResult<Note>>(`/servers/${serverId}/notes`, { params }).then((r) => r.data),

  create: (serverId: string, data: { content: string; isPinned?: boolean }) =>
    api.post<Note>(`/servers/${serverId}/notes`, data).then((r) => r.data),

  update: (noteId: string, data: { content?: string; isPinned?: boolean }) =>
    api.put<Note>(`/notes/${noteId}`, data).then((r) => r.data),

  delete: (noteId: string) =>
    api.delete(`/notes/${noteId}`),
};

// ── Operational Logs ──────────────────────────────────────────────────────────

export const logsApi = {
  forServer: (serverId: string, params = {}) =>
    api.get<PagedResult<OperationalLog>>(`/servers/${serverId}/logs`, { params }).then((r) => r.data),

  all: (params = {}) =>
    api.get<PagedResult<OperationalLog>>('/operational-logs', { params }).then((r) => r.data),
};

// ── Credentials ───────────────────────────────────────────────────────────────

export const credentialsApi = {
  list: () =>
    api.get<PagedResult<Credential>>('/credentials').then((r) => r.data),

  create: (data: Record<string, unknown>) =>
    api.post<Credential>('/credentials', data).then((r) => r.data),

  update: (id: string, data: Record<string, unknown>) =>
    api.put<Credential>(`/credentials/${id}`, data).then((r) => r.data),

  delete: (id: string) =>
    api.delete(`/credentials/${id}`),
};

// ── Dashboard ─────────────────────────────────────────────────────────────────

export const dashboardApi = {
  summary: () =>
    api.get<DashboardSummary>('/dashboard/summary').then((r) => r.data),
};

export default api;
