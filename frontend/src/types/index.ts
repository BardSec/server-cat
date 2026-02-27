// ── Server Catalog ────────────────────────────────────────────────────────────

export type ServerStatus = 'green' | 'yellow' | 'red' | 'gray';
export type Criticality = 'critical' | 'high' | 'medium' | 'low';
export type Environment = 'production' | 'staging' | 'development' | 'dr' | 'lab';
export type PollStatus = 'running' | 'success' | 'partial' | 'failed';

export interface ServerListItem {
  id: string;
  hostname: string;
  displayName?: string;
  ipAddress?: string;
  environment: Environment;
  criticality: Criticality;
  site?: string;
  owner?: string;
  osName?: string;
  tags: string[];
  pollingEnabled: boolean;
  pollingIntervalMinutes: number;
  status: ServerStatus;
  lastPollAt?: string;
  lastPollStatus?: PollStatus;
  isActive: boolean;
  createdAt: string;
}

export interface CredentialRef {
  id: string;
  name: string;
  authMethod: string;
  usernameHint?: string;
}

export interface Group {
  id: string;
  name: string;
  color: string;
}

export interface ServerDetail extends ServerListItem {
  osType: string;
  collectorType: string;
  credentialRef?: CredentialRef;
  groups: Group[];
  updatedAt: string;
  createdBy: string;
}

// ── Poll Results ──────────────────────────────────────────────────────────────

export interface PollSummary {
  id: string;
  serverId: string;
  startedAt: string;
  completedAt?: string;
  status: PollStatus;
  collectorType?: string;
  triggeredBy: string;
  triggeredActor?: string;
  durationMs?: number;
  errorMessage?: string;
}

export interface CpuInfo {
  model: string;
  sockets: number;
  coresPerSocket: number;
  logicalProcessors: number;
}

export interface DiskVolume {
  drive: string;
  label?: string;
  filesystem?: string;
  sizeGb: number;
  freeGb: number;
  freePct: number;
}

export interface NetworkAdapter {
  name: string;
  ipAddress?: string;
  subnetMask?: string;
  macAddress?: string;
  defaultGateway?: string;
  dnsServers: string[];
}

export interface ServiceInfo {
  name: string;
  displayName: string;
  status: 'Running' | 'Stopped' | 'Paused' | string;
  startType: 'Automatic' | 'Manual' | 'Disabled' | string;
}

export interface PollResult {
  poll: PollSummary;
  hostnameResolved?: string;
  fqdn?: string;
  domain?: string;
  osName?: string;
  osVersion?: string;
  osBuild?: string;
  installDate?: string;
  lastBootTime?: string;
  uptimeSeconds?: number;
  cpuInfo?: CpuInfo;
  memoryTotalMb?: number;
  memoryAvailableMb?: number;
  diskVolumes?: DiskVolume[];
  networkAdapters?: NetworkAdapter[];
  services?: ServiceInfo[];
  lastUpdateInstalled?: string;
  pendingReboot?: boolean;
}

// ── Notes ─────────────────────────────────────────────────────────────────────

export interface Note {
  id: string;
  serverId: string;
  content: string;
  isPinned: boolean;
  createdAt: string;
  updatedAt?: string;
  createdBy: string;
  updatedBy?: string;
}

// ── Operational Log ───────────────────────────────────────────────────────────

export interface OperationalLog {
  id: string;
  serverId?: string;
  eventType: string;
  severity: 'info' | 'warning' | 'error' | 'critical';
  summary: string;
  details?: unknown;
  actor: string;
  createdAt: string;
}

// ── Credentials ───────────────────────────────────────────────────────────────

export interface Credential {
  id: string;
  name: string;
  description?: string;
  authMethod: string;
  usernameHint?: string;
  serversUsingCount: number;
  createdAt: string;
  createdBy: string;
}

// ── Paging ────────────────────────────────────────────────────────────────────

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

// ── Dashboard ─────────────────────────────────────────────────────────────────

export interface DashboardSummary {
  totalServers: number;
  activeServers: number;
  byStatus: Record<string, number>;
  byCriticality: Record<string, number>;
  byEnvironment: Record<string, number>;
  recentFailures: Array<{ serverId: string; hostname: string; failedAt: string; error?: string }>;
  pollsLast24H: number;
  failedPollsLast24H: number;
}
