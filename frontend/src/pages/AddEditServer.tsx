import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { serversApi, credentialsApi } from '../api/client';
import type { Credential } from '../types';

const ENVIRONMENTS = ['production', 'staging', 'development', 'dr', 'lab'];
const CRITICALITIES = ['critical', 'high', 'medium', 'low'];
const COLLECTOR_TYPES = ['winrm', 'wmi', 'snmp'];

interface FormData {
  hostname: string;
  ipAddress: string;
  displayName: string;
  environment: string;
  owner: string;
  site: string;
  criticality: string;
  osType: string;
  collectorType: string;
  credentialRefId: string;
  pollingEnabled: boolean;
  pollingIntervalMinutes: number;
  tags: string;
}

const DEFAULT_FORM: FormData = {
  hostname: '',
  ipAddress: '',
  displayName: '',
  environment: 'production',
  owner: '',
  site: '',
  criticality: 'medium',
  osType: 'windows',
  collectorType: 'winrm',
  credentialRefId: '',
  pollingEnabled: true,
  pollingIntervalMinutes: 60,
  tags: '',
};

export function AddEditServer() {
  const { id } = useParams<{ id?: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const isEdit = !!id;

  const [form, setForm] = useState<FormData>(DEFAULT_FORM);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [connectivityResult, setConnectivityResult] = useState<{
    success: boolean;
    checks: Array<{ name: string; passed: boolean; detail?: string }>;
    durationMs: number;
  } | null>(null);
  const [testingConnectivity, setTestingConnectivity] = useState(false);

  // Load existing server if editing
  useQuery({
    queryKey: ['server', id],
    queryFn: () => serversApi.get(id!),
    enabled: isEdit,
    gcTime: 0,
    refetchOnMount: true,
    select: (data) => {
      setForm({
        hostname: data.hostname,
        ipAddress: data.ipAddress ?? '',
        displayName: data.displayName ?? '',
        environment: data.environment,
        owner: data.owner ?? '',
        site: data.site ?? '',
        criticality: data.criticality,
        osType: data.osType,
        collectorType: data.collectorType,
        credentialRefId: data.credentialRef?.id ?? '',
        pollingEnabled: data.pollingEnabled,
        pollingIntervalMinutes: data.pollingIntervalMinutes,
        tags: data.tags.join(', '),
      });
      return data;
    },
  });

  const { data: credentials } = useQuery({
    queryKey: ['credentials'],
    queryFn: credentialsApi.list,
  });

  const createMutation = useMutation({
    mutationFn: (data: Record<string, unknown>) => serversApi.create(data),
    onSuccess: (server) => {
      queryClient.invalidateQueries({ queryKey: ['servers'] });
      navigate(`/servers/${server.id}`);
    },
  });

  const updateMutation = useMutation({
    mutationFn: (data: Record<string, unknown>) => serversApi.update(id!, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['server', id] });
      queryClient.invalidateQueries({ queryKey: ['servers'] });
      navigate(`/servers/${id}`);
    },
  });

  const validate = (): boolean => {
    const errs: Record<string, string> = {};
    if (!form.hostname.trim()) errs.hostname = 'Hostname is required';
    if (form.pollingIntervalMinutes < 5 || form.pollingIntervalMinutes > 10080)
      errs.pollingIntervalMinutes = 'Interval must be between 5 and 10,080 minutes (1 week)';
    if (form.ipAddress && !/^(\d{1,3}\.){3}\d{1,3}$/.test(form.ipAddress))
      errs.ipAddress = 'Invalid IP address format';
    setErrors(errs);
    return Object.keys(errs).length === 0;
  };

  const buildPayload = () => ({
    hostname: form.hostname.trim(),
    ipAddress: form.ipAddress.trim() || undefined,
    displayName: form.displayName.trim() || undefined,
    environment: form.environment,
    owner: form.owner.trim() || undefined,
    site: form.site.trim() || undefined,
    criticality: form.criticality,
    osType: form.osType,
    collectorType: form.collectorType,
    credentialRefId: form.credentialRefId || undefined,
    pollingEnabled: form.pollingEnabled,
    pollingIntervalMinutes: form.pollingIntervalMinutes,
    tags: form.tags ? form.tags.split(',').map((t) => t.trim()).filter(Boolean) : [],
  });

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!validate()) return;
    const payload = buildPayload();
    if (isEdit) await updateMutation.mutateAsync(payload);
    else await createMutation.mutateAsync(payload);
  };

  const handleConnectivityTest = async () => {
    if (!form.hostname.trim()) {
      setErrors({ hostname: 'Hostname is required for connectivity test' });
      return;
    }

    // For connectivity test we need a server ID. If adding new, save first.
    if (!isEdit) {
      alert('Save the server first, then run the connectivity test from the server detail page.');
      return;
    }

    setTestingConnectivity(true);
    setConnectivityResult(null);
    try {
      const result = await serversApi.testConnectivity(id!, {
        collectorType: form.collectorType,
        credentialRefId: form.credentialRefId || undefined,
      });
      setConnectivityResult(result);
    } catch {
      setConnectivityResult({ success: false, durationMs: 0, checks: [{ name: 'Error', passed: false, detail: 'Request failed' }] });
    } finally {
      setTestingConnectivity(false);
    }
  };

  const isSaving = createMutation.isPending || updateMutation.isPending;
  const mutationError = createMutation.error || updateMutation.error;

  return (
    <div className="min-h-screen bg-gray-50">
      <header className="bg-white border-b border-gray-200 px-6 py-4">
        <div className="max-w-3xl mx-auto flex items-center gap-2 text-sm text-gray-500">
          <button onClick={() => navigate('/')} className="hover:text-blue-600">← Catalog</button>
          <span>/</span>
          <span className="text-gray-900 font-medium">{isEdit ? 'Edit Server' : 'Add Server'}</span>
        </div>
      </header>

      <div className="max-w-3xl mx-auto px-6 py-8">
        <form onSubmit={handleSubmit} className="space-y-6">
          {/* Error banner */}
          {mutationError && (
            <div className="rounded-lg bg-red-50 border border-red-200 p-4 text-sm text-red-700">
              {(mutationError as { response?: { data?: { detail?: string } } })?.response?.data?.detail ?? 'An error occurred. Please try again.'}
            </div>
          )}

          {/* ── Identity ── */}
          <Section title="Server Identity">
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <Field label="Hostname *" error={errors.hostname}>
                <input
                  type="text"
                  value={form.hostname}
                  onChange={(e) => setForm({ ...form, hostname: e.target.value })}
                  disabled={isEdit}
                  placeholder="SERVER-01 or server.corp.local"
                  className="form-input"
                />
              </Field>
              <Field label="IP Address" error={errors.ipAddress}>
                <input
                  type="text"
                  value={form.ipAddress}
                  onChange={(e) => setForm({ ...form, ipAddress: e.target.value })}
                  placeholder="10.0.1.50"
                  className="form-input"
                />
              </Field>
              <Field label="Display Name">
                <input
                  type="text"
                  value={form.displayName}
                  onChange={(e) => setForm({ ...form, displayName: e.target.value })}
                  placeholder="Friendly name (optional)"
                  className="form-input"
                />
              </Field>
              <Field label="Owner">
                <input
                  type="text"
                  value={form.owner}
                  onChange={(e) => setForm({ ...form, owner: e.target.value })}
                  placeholder="Team or person"
                  className="form-input"
                />
              </Field>
            </div>
          </Section>

          {/* ── Classification ── */}
          <Section title="Classification">
            <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
              <Field label="Environment">
                <Select value={form.environment} onChange={(v) => setForm({ ...form, environment: v })} options={ENVIRONMENTS} />
              </Field>
              <Field label="Criticality">
                <Select value={form.criticality} onChange={(v) => setForm({ ...form, criticality: v })} options={CRITICALITIES} />
              </Field>
              <Field label="Site">
                <input
                  type="text"
                  value={form.site}
                  onChange={(e) => setForm({ ...form, site: e.target.value })}
                  placeholder="DC-East, HQ..."
                  className="form-input"
                />
              </Field>
            </div>
            <Field label="Tags (comma-separated)">
              <input
                type="text"
                value={form.tags}
                onChange={(e) => setForm({ ...form, tags: e.target.value })}
                placeholder="web, iis, sql"
                className="form-input"
              />
            </Field>
          </Section>

          {/* ── Polling ── */}
          <Section title="Polling Configuration">
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <Field label="Collector Type">
                <Select
                  value={form.collectorType}
                  onChange={(v) => setForm({ ...form, collectorType: v })}
                  options={COLLECTOR_TYPES}
                />
                {form.collectorType === 'snmp' && (
                  <p className="text-xs text-yellow-600 mt-1">⚠ SNMP collector is a stub in v1. Use WinRM for Windows targets.</p>
                )}
              </Field>
              <Field label="Credential Reference">
                <select
                  value={form.credentialRefId}
                  onChange={(e) => setForm({ ...form, credentialRefId: e.target.value })}
                  className="form-input"
                >
                  <option value="">None (unauthenticated)</option>
                  {(credentials?.items ?? []).map((c: Credential) => (
                    <option key={c.id} value={c.id}>
                      {c.name} ({c.authMethod})
                    </option>
                  ))}
                </select>
              </Field>
              <Field label="Polling Enabled">
                <label className="flex items-center gap-2 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={form.pollingEnabled}
                    onChange={(e) => setForm({ ...form, pollingEnabled: e.target.checked })}
                    className="rounded border-gray-300 text-blue-600"
                  />
                  <span className="text-sm text-gray-700">Enable automatic polling</span>
                </label>
              </Field>
              <Field label="Polling Interval (minutes)" error={errors.pollingIntervalMinutes}>
                <input
                  type="number"
                  value={form.pollingIntervalMinutes}
                  onChange={(e) => setForm({ ...form, pollingIntervalMinutes: parseInt(e.target.value, 10) })}
                  min={5}
                  max={10080}
                  className="form-input"
                />
              </Field>
            </div>
          </Section>

          {/* ── Connectivity Test ── */}
          {isEdit && (
            <Section title="Connectivity Test">
              <div className="flex items-center gap-3">
                <button
                  type="button"
                  onClick={handleConnectivityTest}
                  disabled={testingConnectivity}
                  className="px-4 py-2 text-sm border border-blue-600 text-blue-600 rounded-lg hover:bg-blue-50 disabled:opacity-50"
                >
                  {testingConnectivity ? 'Testing...' : 'Test Connectivity'}
                </button>
                <span className="text-xs text-gray-500">Tests reachability and authentication without running a full poll</span>
              </div>

              {connectivityResult && (
                <div className={`mt-3 rounded-lg border p-4 ${connectivityResult.success ? 'border-green-300 bg-green-50' : 'border-red-300 bg-red-50'}`}>
                  <div className="font-medium text-sm mb-2">
                    {connectivityResult.success ? '✅ All checks passed' : '❌ Some checks failed'} ({connectivityResult.durationMs}ms)
                  </div>
                  <div className="space-y-1">
                    {connectivityResult.checks.map((check, i) => (
                      <div key={i} className="flex items-start gap-2 text-sm">
                        <span>{check.passed ? '✓' : '✗'}</span>
                        <span className="font-medium">{check.name}:</span>
                        <span className="text-gray-600">{check.detail}</span>
                      </div>
                    ))}
                  </div>
                </div>
              )}
            </Section>
          )}

          {/* ── Actions ── */}
          <div className="flex items-center justify-end gap-3 pt-4">
            <button
              type="button"
              onClick={() => navigate(isEdit ? `/servers/${id}` : '/')}
              className="px-4 py-2 text-sm border border-gray-300 rounded-lg hover:bg-gray-50"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={isSaving}
              className="px-6 py-2 text-sm bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50"
            >
              {isSaving ? 'Saving...' : isEdit ? 'Save Changes' : 'Add Server'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="bg-white rounded-xl border border-gray-200 p-6">
      <h2 className="text-base font-semibold text-gray-900 mb-4">{title}</h2>
      <div className="space-y-4">{children}</div>
    </div>
  );
}

function Field({ label, children, error }: { label: string; children: React.ReactNode; error?: string }) {
  return (
    <div>
      <label className="block text-sm font-medium text-gray-700 mb-1">{label}</label>
      {children}
      {error && <p className="text-xs text-red-600 mt-1">{error}</p>}
    </div>
  );
}

function Select({ value, onChange, options }: { value: string; onChange: (v: string) => void; options: string[] }) {
  return (
    <select
      value={value}
      onChange={(e) => onChange(e.target.value)}
      className="form-input capitalize"
    >
      {options.map((o) => (
        <option key={o} value={o} className="capitalize">{o}</option>
      ))}
    </select>
  );
}
