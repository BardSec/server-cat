import { useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { formatDistanceToNow, format } from 'date-fns';
import { serversApi, pollsApi } from '../api/client';
import { StatusBadge, CriticalityBadge, PollStatusBadge, DiskBar } from '../components/StatusBadge';
import { NotesPanel } from '../components/NotesPanel';
import type { DiskVolume, NetworkAdapter, ServiceInfo, PollSummary } from '../types';

type Tab = 'overview' | 'services' | 'storage' | 'network' | 'history' | 'notes';

export function ServerDetail() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [activeTab, setActiveTab] = useState<Tab>('overview');
  const [serviceFilter, setServiceFilter] = useState('');

  const { data: server, isLoading: serverLoading } = useQuery({
    queryKey: ['server', id],
    queryFn: () => serversApi.get(id!),
    enabled: !!id,
  });

  const { data: latestPoll, isLoading: pollLoading } = useQuery({
    queryKey: ['poll-latest', id],
    queryFn: () => pollsApi.getLatest(id!),
    enabled: !!id,
    retry: false,
  });

  const { data: pollHistory } = useQuery({
    queryKey: ['poll-history', id],
    queryFn: () => pollsApi.listForServer(id!, { pageSize: 20 }),
    enabled: activeTab === 'history' && !!id,
  });

  const pollMutation = useMutation({
    mutationFn: () => serversApi.triggerPoll(id!),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['server', id] });
      queryClient.invalidateQueries({ queryKey: ['poll-latest', id] });
    },
  });

  if (serverLoading) {
    return <div className="min-h-screen bg-gray-50 flex items-center justify-center text-gray-500">Loading...</div>;
  }

  if (!server) {
    return <div className="min-h-screen bg-gray-50 flex items-center justify-center text-gray-500">Server not found.</div>;
  }

  const result = latestPoll;
  const diskVolumes: DiskVolume[] = (result?.diskVolumes as DiskVolume[]) ?? [];
  const networkAdapters: NetworkAdapter[] = (result?.networkAdapters as NetworkAdapter[]) ?? [];
  const services: ServiceInfo[] = (result?.services as ServiceInfo[]) ?? [];

  const filteredServices = serviceFilter
    ? services.filter(
        (s) =>
          s.name.toLowerCase().includes(serviceFilter.toLowerCase()) ||
          s.displayName.toLowerCase().includes(serviceFilter.toLowerCase())
      )
    : services;

  const formatUptime = (seconds?: number) => {
    if (!seconds) return '—';
    const d = Math.floor(seconds / 86400);
    const h = Math.floor((seconds % 86400) / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    return `${d}d ${h}h ${m}m`;
  };

  const TABS: { id: Tab; label: string; count?: number }[] = [
    { id: 'overview', label: 'Overview' },
    { id: 'services', label: 'Services', count: services.length },
    { id: 'storage', label: 'Storage', count: diskVolumes.length },
    { id: 'network', label: 'Network', count: networkAdapters.length },
    { id: 'history', label: 'History' },
    { id: 'notes', label: 'Notes' },
  ];

  return (
    <div className="min-h-screen bg-gray-50">
      {/* ── Header ── */}
      <header className="bg-white border-b border-gray-200 px-6 py-4">
        <div className="max-w-screen-xl mx-auto">
          <div className="flex items-center gap-2 text-sm text-gray-500 mb-1">
            <button onClick={() => navigate('/')} className="hover:text-blue-600">← Catalog</button>
            <span>/</span>
            <span className="text-gray-900 font-medium">{server.hostname}</span>
          </div>
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-3">
              <h1 className="text-xl font-bold text-gray-900">{server.displayName ?? server.hostname}</h1>
              <StatusBadge status={server.status} />
              <CriticalityBadge criticality={server.criticality} />
              {server.pendingReboot && (
                <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-orange-100 text-orange-800">
                  ⚠ Reboot Pending
                </span>
              )}
            </div>
            <div className="flex items-center gap-2">
              <button
                onClick={() => navigate(`/servers/${id}/edit`)}
                className="px-3 py-1.5 text-sm border border-gray-300 rounded-lg hover:bg-gray-50"
              >
                Edit
              </button>
              <button
                onClick={() => pollMutation.mutate()}
                disabled={pollMutation.isPending}
                className="px-3 py-1.5 text-sm bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50"
              >
                {pollMutation.isPending ? 'Polling...' : 'Poll Now'}
              </button>
            </div>
          </div>

          {/* Meta row */}
          <div className="mt-2 flex flex-wrap gap-4 text-sm text-gray-500">
            <span>IP: <span className="font-mono text-gray-700">{server.ipAddress ?? '—'}</span></span>
            <span>Site: <span className="text-gray-700">{server.site ?? '—'}</span></span>
            <span>Owner: <span className="text-gray-700">{server.owner ?? '—'}</span></span>
            <span>Environment: <span className="capitalize text-gray-700">{server.environment}</span></span>
            <span>Collector: <span className="font-mono text-gray-700">{server.collectorType}</span></span>
            {server.lastPollAt && (
              <span>
                Last poll:{' '}
                <span className="text-gray-700">
                  {formatDistanceToNow(new Date(server.lastPollAt), { addSuffix: true })}
                </span>
              </span>
            )}
          </div>
        </div>
      </header>

      {/* ── Tabs ── */}
      <div className="bg-white border-b border-gray-200 px-6">
        <div className="max-w-screen-xl mx-auto flex gap-0">
          {TABS.map((tab) => (
            <button
              key={tab.id}
              onClick={() => setActiveTab(tab.id)}
              className={`px-4 py-3 text-sm font-medium border-b-2 -mb-px transition-colors ${
                activeTab === tab.id
                  ? 'border-blue-600 text-blue-600'
                  : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
              }`}
            >
              {tab.label}
              {tab.count !== undefined && (
                <span className="ml-1.5 text-xs bg-gray-100 text-gray-600 px-1.5 py-0.5 rounded-full">
                  {tab.count}
                </span>
              )}
            </button>
          ))}
        </div>
      </div>

      {/* ── Tab Content ── */}
      <div className="max-w-screen-xl mx-auto px-6 py-6">
        {/* Overview */}
        {activeTab === 'overview' && (
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            <InfoCard title="Operating System">
              <InfoRow label="OS Name" value={result?.osName} />
              <InfoRow label="Version" value={result?.osVersion} />
              <InfoRow label="Build" value={result?.osBuild} monospace />
              <InfoRow label="Install Date" value={result?.installDate ? format(new Date(result.installDate), 'yyyy-MM-dd') : undefined} />
            </InfoCard>

            <InfoCard title="Uptime">
              <InfoRow label="Last Boot" value={result?.lastBootTime ? format(new Date(result.lastBootTime), 'yyyy-MM-dd HH:mm') : undefined} />
              <InfoRow label="Uptime" value={formatUptime(result?.uptimeSeconds ?? undefined)} />
              <InfoRow label="Last Update" value={result?.lastUpdateInstalled ?? undefined} />
            </InfoCard>

            <InfoCard title="Processor">
              <InfoRow label="Model" value={result?.cpuInfo?.model} />
              <InfoRow label="Sockets" value={String(result?.cpuInfo?.sockets ?? '—')} />
              <InfoRow label="Cores/Socket" value={String(result?.cpuInfo?.coresPerSocket ?? '—')} />
              <InfoRow label="Logical CPUs" value={String(result?.cpuInfo?.logicalProcessors ?? '—')} />
            </InfoCard>

            <InfoCard title="Memory">
              {result?.memoryTotalMb ? (
                <>
                  <InfoRow label="Total RAM" value={`${(result.memoryTotalMb / 1024).toFixed(1)} GB`} />
                  <InfoRow label="Available" value={`${((result.memoryAvailableMb ?? 0) / 1024).toFixed(1)} GB`} />
                  <div className="mt-2">
                    <DiskBar
                      freePct={result.memoryTotalMb > 0
                        ? ((result.memoryAvailableMb ?? 0) / result.memoryTotalMb) * 100
                        : 0}
                    />
                  </div>
                </>
              ) : (
                <span className="text-gray-400 text-sm">Not collected</span>
              )}
            </InfoCard>

            <InfoCard title="Network Identity">
              <InfoRow label="Hostname" value={result?.hostnameResolved} monospace />
              <InfoRow label="FQDN" value={result?.fqdn} monospace />
              <InfoRow label="Domain" value={result?.domain} />
            </InfoCard>

            <InfoCard title="Catalog Info">
              <InfoRow label="Groups" value={server.groups.map(g => g.name).join(', ') || '—'} />
              <InfoRow label="Tags" value={server.tags.join(', ') || '—'} />
              <InfoRow label="Credential" value={server.credentialRef?.name} />
              <InfoRow label="Poll Interval" value={`${server.pollingIntervalMinutes} min`} />
            </InfoCard>
          </div>
        )}

        {/* Storage */}
        {activeTab === 'storage' && (
          <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
            {diskVolumes.length === 0 ? (
              <div className="text-center py-16 text-gray-400">
                {pollLoading ? 'Loading...' : 'No disk data collected yet.'}
              </div>
            ) : (
              <table className="min-w-full divide-y divide-gray-200 text-sm">
                <thead className="bg-gray-50">
                  <tr>
                    <th className="px-4 py-3 text-left font-medium text-gray-500">Drive</th>
                    <th className="px-4 py-3 text-left font-medium text-gray-500">Label</th>
                    <th className="px-4 py-3 text-left font-medium text-gray-500">Filesystem</th>
                    <th className="px-4 py-3 text-left font-medium text-gray-500">Size</th>
                    <th className="px-4 py-3 text-left font-medium text-gray-500">Free</th>
                    <th className="px-4 py-3 text-left font-medium text-gray-500 w-48">Usage</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {diskVolumes.map((vol: DiskVolume) => (
                    <tr key={vol.drive} className={vol.freePct < 10 ? 'bg-red-50' : vol.freePct < 20 ? 'bg-yellow-50' : ''}>
                      <td className="px-4 py-3 font-mono font-bold text-gray-900">{vol.drive}</td>
                      <td className="px-4 py-3 text-gray-600">{vol.label ?? '—'}</td>
                      <td className="px-4 py-3 text-gray-500">{vol.filesystem ?? '—'}</td>
                      <td className="px-4 py-3 text-gray-700">{vol.sizeGb.toFixed(1)} GB</td>
                      <td className="px-4 py-3 text-gray-700">{vol.freeGb.toFixed(1)} GB</td>
                      <td className="px-4 py-3"><DiskBar freePct={vol.freePct} /></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        )}

        {/* Services */}
        {activeTab === 'services' && (
          <div className="space-y-3">
            <div className="flex gap-2">
              <input
                type="text"
                value={serviceFilter}
                onChange={(e) => setServiceFilter(e.target.value)}
                placeholder="Filter services..."
                className="rounded-lg border-gray-300 text-sm shadow-sm focus:border-blue-500 focus:ring-blue-500"
              />
              <span className="text-sm text-gray-500 self-center">
                {filteredServices.length} of {services.length} services
              </span>
            </div>

            <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
              {services.length === 0 ? (
                <div className="text-center py-16 text-gray-400">No service data collected yet.</div>
              ) : (
                <table className="min-w-full divide-y divide-gray-200 text-sm">
                  <thead className="bg-gray-50">
                    <tr>
                      <th className="px-4 py-3 text-left font-medium text-gray-500">Service Name</th>
                      <th className="px-4 py-3 text-left font-medium text-gray-500">Display Name</th>
                      <th className="px-4 py-3 text-left font-medium text-gray-500">Status</th>
                      <th className="px-4 py-3 text-left font-medium text-gray-500">Start Type</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100">
                    {filteredServices.map((svc: ServiceInfo) => (
                      <tr key={svc.name} className={
                        svc.status === 'Stopped' && svc.startType === 'Automatic'
                          ? 'bg-red-50' : ''
                      }>
                        <td className="px-4 py-2 font-mono text-xs text-gray-700">{svc.name}</td>
                        <td className="px-4 py-2 text-gray-600">{svc.displayName}</td>
                        <td className="px-4 py-2">
                          <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${
                            svc.status === 'Running' ? 'bg-green-100 text-green-800' :
                            svc.status === 'Stopped' ? 'bg-red-100 text-red-800' :
                            'bg-gray-100 text-gray-600'
                          }`}>
                            {svc.status}
                          </span>
                        </td>
                        <td className="px-4 py-2 text-gray-500">{svc.startType}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          </div>
        )}

        {/* Network */}
        {activeTab === 'network' && (
          <div className="space-y-4">
            {networkAdapters.length === 0 ? (
              <div className="bg-white rounded-xl border border-gray-200 text-center py-16 text-gray-400">
                No network data collected yet.
              </div>
            ) : (
              networkAdapters.map((adapter: NetworkAdapter, idx: number) => (
                <div key={idx} className="bg-white rounded-xl border border-gray-200 p-4">
                  <h3 className="font-medium text-gray-900 mb-3">{adapter.name}</h3>
                  <div className="grid grid-cols-2 md:grid-cols-3 gap-3 text-sm">
                    <InfoRow label="IP Address" value={adapter.ipAddress} monospace />
                    <InfoRow label="Subnet Mask" value={adapter.subnetMask} monospace />
                    <InfoRow label="MAC Address" value={adapter.macAddress} monospace />
                    <InfoRow label="Default Gateway" value={adapter.defaultGateway} monospace />
                    <InfoRow label="DNS Servers" value={adapter.dnsServers?.join(', ')} monospace />
                  </div>
                </div>
              ))
            )}
          </div>
        )}

        {/* History */}
        {activeTab === 'history' && (
          <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
            <table className="min-w-full divide-y divide-gray-200 text-sm">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-4 py-3 text-left font-medium text-gray-500">Started</th>
                  <th className="px-4 py-3 text-left font-medium text-gray-500">Duration</th>
                  <th className="px-4 py-3 text-left font-medium text-gray-500">Status</th>
                  <th className="px-4 py-3 text-left font-medium text-gray-500">Triggered By</th>
                  <th className="px-4 py-3 text-left font-medium text-gray-500">Error</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {(pollHistory?.items ?? []).map((poll: PollSummary) => (
                  <tr key={poll.id}>
                    <td className="px-4 py-3 text-gray-700">
                      {format(new Date(poll.startedAt), 'yyyy-MM-dd HH:mm:ss')}
                    </td>
                    <td className="px-4 py-3 text-gray-500">
                      {poll.durationMs ? `${(poll.durationMs / 1000).toFixed(1)}s` : '—'}
                    </td>
                    <td className="px-4 py-3">
                      <PollStatusBadge status={poll.status} />
                    </td>
                    <td className="px-4 py-3 text-gray-500 capitalize">
                      {poll.triggeredBy}
                      {poll.triggeredActor && ` (${poll.triggeredActor})`}
                    </td>
                    <td className="px-4 py-3 text-xs text-red-600 max-w-xs truncate">
                      {poll.errorMessage ?? ''}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            {(!pollHistory || pollHistory.items.length === 0) && (
              <div className="text-center py-16 text-gray-400">No poll history yet.</div>
            )}
          </div>
        )}

        {/* Notes */}
        {activeTab === 'notes' && <NotesPanel serverId={id!} />}
      </div>
    </div>
  );
}

function InfoCard({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="bg-white rounded-xl border border-gray-200 p-4">
      <h3 className="text-sm font-semibold text-gray-700 mb-3 pb-2 border-b border-gray-100">{title}</h3>
      <div className="space-y-2">{children}</div>
    </div>
  );
}

function InfoRow({ label, value, monospace }: { label: string; value?: string | null; monospace?: boolean }) {
  return (
    <div className="flex justify-between gap-2 text-sm">
      <span className="text-gray-500 shrink-0">{label}</span>
      <span className={`text-gray-900 text-right truncate ${monospace ? 'font-mono text-xs' : ''}`}>
        {value ?? '—'}
      </span>
    </div>
  );
}
