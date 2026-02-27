import { useState, useCallback } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { formatDistanceToNow } from 'date-fns';
import { serversApi, dashboardApi, type ServerListParams } from '../api/client';
import { StatusBadge, CriticalityBadge } from '../components/StatusBadge';
import type { ServerListItem, ServerStatus, Criticality } from '../types';

const ENVIRONMENTS = ['production', 'staging', 'development', 'dr', 'lab'];
const CRITICALITIES: Criticality[] = ['critical', 'high', 'medium', 'low'];
const STATUSES: ServerStatus[] = ['green', 'yellow', 'red', 'gray'];

export function ServerCatalog() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const [filters, setFilters] = useState<ServerListParams>({
    page: 1,
    pageSize: 50,
    sortBy: 'hostname',
    sortDir: 'asc',
  });
  const [searchInput, setSearchInput] = useState('');

  const { data: summary } = useQuery({
    queryKey: ['dashboard'],
    queryFn: dashboardApi.summary,
    refetchInterval: 60_000,
  });

  const { data, isLoading, isFetching } = useQuery({
    queryKey: ['servers', filters],
    queryFn: () => serversApi.list(filters),
    refetchInterval: 30_000,
    placeholderData: (prev) => prev,
  });

  const pollMutation = useMutation({
    mutationFn: (id: string) => serversApi.triggerPoll(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['servers'] }),
  });

  const applyFilter = useCallback(
    (key: keyof ServerListParams, value: string | undefined) => {
      setFilters((f) => ({ ...f, [key]: value || undefined, page: 1 }));
    },
    []
  );

  const handleSearch = () => {
    applyFilter('search', searchInput);
  };

  const servers = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;

  return (
    <div className="min-h-screen bg-gray-50">
      {/* ── Header ── */}
      <header className="bg-white border-b border-gray-200 px-6 py-4">
        <div className="max-w-screen-2xl mx-auto flex items-center justify-between">
          <div className="flex items-center gap-3">
            <span className="text-2xl font-bold text-blue-700">ServerCat</span>
            <span className="text-gray-400">|</span>
            <span className="text-gray-600 text-sm">Server Catalog &amp; Polling</span>
          </div>
          <button
            onClick={() => navigate('/servers/new')}
            className="px-4 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700"
          >
            + Add Server
          </button>
        </div>
      </header>

      <div className="max-w-screen-2xl mx-auto px-6 py-6 space-y-6">
        {/* ── Dashboard Summary Cards ── */}
        {summary && (
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
            {Object.entries(summary.byStatus).map(([status, count]) => (
              <button
                key={status}
                onClick={() => applyFilter('status', status)}
                className={`bg-white rounded-xl border p-4 text-left hover:shadow-md transition-shadow
                  ${filters.status === status ? 'ring-2 ring-blue-500' : ''}`}
              >
                <div className="flex items-center justify-between">
                  <span className="text-sm text-gray-500 capitalize">{status}</span>
                  <span className={`h-3 w-3 rounded-full ${
                    status === 'green' ? 'bg-green-500' :
                    status === 'yellow' ? 'bg-yellow-500' :
                    status === 'red' ? 'bg-red-500' : 'bg-gray-400'
                  }`} />
                </div>
                <span className="text-2xl font-bold text-gray-900">{count}</span>
              </button>
            ))}
          </div>
        )}

        {/* ── Filters ── */}
        <div className="bg-white rounded-xl border border-gray-200 p-4">
          <div className="flex flex-wrap gap-3 items-end">
            <div className="flex-1 min-w-48">
              <label className="block text-xs font-medium text-gray-500 mb-1">Search</label>
              <div className="flex gap-1">
                <input
                  type="text"
                  value={searchInput}
                  onChange={(e) => setSearchInput(e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
                  placeholder="Hostname or display name..."
                  className="flex-1 rounded-lg border-gray-300 text-sm shadow-sm focus:border-blue-500 focus:ring-blue-500"
                />
                <button
                  onClick={handleSearch}
                  className="px-3 py-1.5 bg-gray-100 rounded-lg text-sm hover:bg-gray-200"
                >
                  Search
                </button>
              </div>
            </div>

            <FilterSelect
              label="Environment"
              value={filters.environment ?? ''}
              onChange={(v) => applyFilter('environment', v)}
              options={ENVIRONMENTS}
            />
            <FilterSelect
              label="Criticality"
              value={filters.criticality ?? ''}
              onChange={(v) => applyFilter('criticality', v)}
              options={CRITICALITIES}
            />
            <FilterSelect
              label="Status"
              value={filters.status ?? ''}
              onChange={(v) => applyFilter('status', v)}
              options={STATUSES}
            />

            {(filters.environment || filters.criticality || filters.status || filters.search) && (
              <button
                onClick={() => {
                  setFilters({ page: 1, pageSize: 50, sortBy: 'hostname', sortDir: 'asc' });
                  setSearchInput('');
                }}
                className="text-sm text-red-600 hover:underline"
              >
                Clear filters
              </button>
            )}
          </div>
        </div>

        {/* ── Table ── */}
        <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
          <div className="px-4 py-3 border-b border-gray-100 flex items-center justify-between">
            <span className="text-sm text-gray-600">
              {isFetching ? 'Refreshing...' : `${totalCount} server${totalCount !== 1 ? 's' : ''}`}
            </span>
          </div>

          {isLoading ? (
            <div className="text-center py-16 text-gray-400">Loading servers...</div>
          ) : servers.length === 0 ? (
            <div className="text-center py-16 text-gray-400">
              No servers found.{' '}
              <button className="text-blue-600 hover:underline" onClick={() => navigate('/servers/new')}>
                Add your first server
              </button>
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="min-w-full divide-y divide-gray-200 text-sm">
                <thead className="bg-gray-50">
                  <tr>
                    <th className="px-4 py-3 text-left font-medium text-gray-500">Status</th>
                    <th className="px-4 py-3 text-left font-medium text-gray-500">Hostname</th>
                    <th className="px-4 py-3 text-left font-medium text-gray-500">Environment</th>
                    <th className="px-4 py-3 text-left font-medium text-gray-500">Criticality</th>
                    <th className="px-4 py-3 text-left font-medium text-gray-500">Site</th>
                    <th className="px-4 py-3 text-left font-medium text-gray-500">OS</th>
                    <th className="px-4 py-3 text-left font-medium text-gray-500">Last Poll</th>
                    <th className="px-4 py-3 text-left font-medium text-gray-500">Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {servers.map((server: ServerListItem) => (
                    <tr
                      key={server.id}
                      className="hover:bg-gray-50 cursor-pointer"
                      onClick={() => navigate(`/servers/${server.id}`)}
                    >
                      <td className="px-4 py-3">
                        <StatusBadge status={server.status} />
                      </td>
                      <td className="px-4 py-3">
                        <div className="font-medium text-gray-900">{server.hostname}</div>
                        {server.displayName && server.displayName !== server.hostname && (
                          <div className="text-xs text-gray-500">{server.displayName}</div>
                        )}
                        {server.ipAddress && (
                          <div className="text-xs text-gray-400 font-mono">{server.ipAddress}</div>
                        )}
                      </td>
                      <td className="px-4 py-3">
                        <span className="capitalize text-gray-600">{server.environment}</span>
                      </td>
                      <td className="px-4 py-3">
                        <CriticalityBadge criticality={server.criticality} />
                      </td>
                      <td className="px-4 py-3 text-gray-600">{server.site ?? '—'}</td>
                      <td className="px-4 py-3 text-gray-500 text-xs">{server.osName ?? '—'}</td>
                      <td className="px-4 py-3 text-xs text-gray-500">
                        {server.lastPollAt
                          ? formatDistanceToNow(new Date(server.lastPollAt), { addSuffix: true })
                          : 'Never'}
                      </td>
                      <td className="px-4 py-3" onClick={(e) => e.stopPropagation()}>
                        <button
                          onClick={() => pollMutation.mutate(server.id)}
                          disabled={pollMutation.isPending}
                          className="text-xs text-blue-600 hover:text-blue-800 hover:underline disabled:opacity-50"
                        >
                          Poll now
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {/* Pagination */}
          {data && data.totalPages > 1 && (
            <div className="px-4 py-3 border-t border-gray-100 flex items-center justify-between">
              <span className="text-xs text-gray-500">
                Page {data.page} of {data.totalPages}
              </span>
              <div className="flex gap-2">
                <button
                  disabled={data.page <= 1}
                  onClick={() => setFilters((f) => ({ ...f, page: f.page! - 1 }))}
                  className="px-3 py-1 text-xs rounded-md border border-gray-300 hover:bg-gray-50 disabled:opacity-50"
                >
                  Previous
                </button>
                <button
                  disabled={data.page >= data.totalPages}
                  onClick={() => setFilters((f) => ({ ...f, page: f.page! + 1 }))}
                  className="px-3 py-1 text-xs rounded-md border border-gray-300 hover:bg-gray-50 disabled:opacity-50"
                >
                  Next
                </button>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function FilterSelect({
  label,
  value,
  onChange,
  options,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  options: string[];
}) {
  return (
    <div>
      <label className="block text-xs font-medium text-gray-500 mb-1">{label}</label>
      <select
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="rounded-lg border-gray-300 text-sm shadow-sm focus:border-blue-500 focus:ring-blue-500"
      >
        <option value="">All</option>
        {options.map((o) => (
          <option key={o} value={o} className="capitalize">
            {o}
          </option>
        ))}
      </select>
    </div>
  );
}
