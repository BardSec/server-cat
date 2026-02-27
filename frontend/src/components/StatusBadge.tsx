import type { ServerStatus, PollStatus, Criticality } from '../types';

// ── Server health status ──────────────────────────────────────────────────────

const STATUS_CONFIG: Record<ServerStatus, { label: string; dot: string; bg: string; text: string }> = {
  green: { label: 'Healthy', dot: 'bg-green-500', bg: 'bg-green-100', text: 'text-green-800' },
  yellow: { label: 'Warning', dot: 'bg-yellow-500', bg: 'bg-yellow-100', text: 'text-yellow-800' },
  red: { label: 'Critical', dot: 'bg-red-500', bg: 'bg-red-100', text: 'text-red-800' },
  gray: { label: 'Unknown', dot: 'bg-gray-400', bg: 'bg-gray-100', text: 'text-gray-600' },
};

export function StatusBadge({ status }: { status: ServerStatus }) {
  const cfg = STATUS_CONFIG[status];
  return (
    <span className={`inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-medium ${cfg.bg} ${cfg.text}`}>
      <span className={`h-2 w-2 rounded-full ${cfg.dot}`} />
      {cfg.label}
    </span>
  );
}

// ── Poll status ───────────────────────────────────────────────────────────────

const POLL_CONFIG: Record<PollStatus, { label: string; color: string }> = {
  success: { label: 'Success', color: 'text-green-700 bg-green-50' },
  partial: { label: 'Partial', color: 'text-yellow-700 bg-yellow-50' },
  failed: { label: 'Failed', color: 'text-red-700 bg-red-50' },
  running: { label: 'Running', color: 'text-blue-700 bg-blue-50' },
};

export function PollStatusBadge({ status }: { status: PollStatus }) {
  const cfg = POLL_CONFIG[status];
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${cfg.color}`}>
      {status === 'running' && (
        <svg className="animate-spin -ml-0.5 mr-1.5 h-3 w-3" viewBox="0 0 24 24" fill="none">
          <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
          <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
        </svg>
      )}
      {cfg.label}
    </span>
  );
}

// ── Criticality ───────────────────────────────────────────────────────────────

const CRIT_CONFIG: Record<Criticality, { label: string; color: string }> = {
  critical: { label: 'Critical', color: 'text-red-700 bg-red-50 ring-red-600/20' },
  high: { label: 'High', color: 'text-orange-700 bg-orange-50 ring-orange-600/20' },
  medium: { label: 'Medium', color: 'text-yellow-700 bg-yellow-50 ring-yellow-600/20' },
  low: { label: 'Low', color: 'text-green-700 bg-green-50 ring-green-600/20' },
};

export function CriticalityBadge({ criticality }: { criticality: Criticality }) {
  const cfg = CRIT_CONFIG[criticality];
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ring-1 ring-inset ${cfg.color}`}>
      {cfg.label}
    </span>
  );
}

// ── Disk free % ───────────────────────────────────────────────────────────────

export function DiskStatusColor(freePct: number): string {
  if (freePct < 10) return 'text-red-600';
  if (freePct < 20) return 'text-yellow-600';
  return 'text-green-600';
}

export function DiskBar({ freePct }: { freePct: number }) {
  const usedPct = 100 - freePct;
  const color = freePct < 10 ? 'bg-red-500' : freePct < 20 ? 'bg-yellow-500' : 'bg-green-500';
  return (
    <div className="flex items-center gap-2">
      <div className="flex-1 bg-gray-200 rounded-full h-2 overflow-hidden">
        <div className={`h-2 rounded-full ${color}`} style={{ width: `${usedPct}%` }} />
      </div>
      <span className={`text-xs font-medium w-12 text-right ${DiskStatusColor(freePct)}`}>
        {freePct.toFixed(1)}% free
      </span>
    </div>
  );
}
