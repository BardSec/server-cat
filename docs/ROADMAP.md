# Phased Roadmap — Server Catalog & Polling

---

## v1.0 — Core Foundation (This Scaffold)

**Theme:** Get a working, secure, maintainable foundation running on Windows.

| Feature | Status |
|---------|--------|
| Server catalog CRUD | ✅ Scaffolded |
| WinRM/PowerShell collector (Windows) | ✅ Scaffolded |
| WMI/CIM collector (stub → full) | ✅ Stub |
| SNMP v2/v3 collector (stub) | ✅ Stub |
| Hangfire scheduler with per-server intervals | ✅ Scaffolded |
| PostgreSQL schema + EF Core migrations | ✅ Scaffolded |
| DPAPI credential store | ✅ Scaffolded |
| React UI: catalog table + server detail | ✅ Scaffolded |
| Notes + operational log | ✅ Scaffolded |
| RBAC (Viewer / Engineer / Admin) | ✅ Scaffolded |
| Windows Service deployment | ✅ Scripts provided |
| Health endpoint | ✅ Included |
| Connectivity test endpoint | ✅ Included |

**Danger Zones in v1:**
1. **WMI/DCOM permissions** — DCOM requires precise DACL settings per server. Registry edits and Component Services configuration are error-prone and differ between OS versions. Use WinRM as default; only fall back to WMI if WinRM is unavailable.
2. **WinRM certificate trust** — Targets with self-signed certs require either a trusted internal CA chain or `WSManFlagSkipCACheck` (avoid in production). Budget time for certificate management.
3. **Kerberos delegation** — If polling servers in other domains or forests, Kerberos delegation must be explicitly configured. Without it, fall back to NTLM which cannot use JEA constrained endpoints.
4. **Firewall coordination** — In segmented networks, WinRM outbound (5986) and return traffic to the app server may need routing changes. Test connectivity before bulk-adding servers.
5. **Memory leaks in WinRM/PS sessions** — The PowerShell Runspace pool can leak if sessions are not properly disposed. The collector wraps all sessions in `using` blocks; verify under load.

---

## v1.5 — Operational Hardening

**Theme:** Make it reliable and observable enough for production operations.

| Feature | Notes |
|---------|-------|
| **Alerting integrations** | Webhook to SCOM/Zabbix when server goes red. Email via SMTP relay. |
| **Linux support** | SSH/SFTP collector using `Renci.SshNet`. Collect from `/proc`, `df`, `systemctl`. |
| **WMI/CIM collector completion** | Full implementation replacing the v1 stub. WMI over WinRM preferred over DCOM. |
| **Configurable status rules** | Admin UI to set threshold values (disk %, service watch list, patch age). Currently hardcoded. |
| **Bulk import** | CSV/JSON import for onboarding many servers at once. |
| **Export** | Export catalog + latest snapshot to CSV/XLSX for reports. |
| **ServiceNow CMDB sync** | One-way push of catalog data to ServiceNow CI table via REST API. |
| **UI improvements** | Column chooser, saved filters, dark mode. |
| **Background data retention** | Automated Hangfire job to purge old poll_results per retention policy. |
| **Metrics endpoint** | Prometheus-format `/metrics` for Grafana dashboards. |

**Danger Zones in v1.5:**
- **SNMP complexity** — SNMPv3 `authPriv` with SHA-256 and AES-128 requires exact USM user configuration on each device. SNMP MIBs vary widely by vendor. Plan for significant per-device validation.
- **SSH key management** — For Linux support, SSH private keys must be managed as carefully as WinRM credentials. Use DPAPI vault. Rotate keys annually minimum.

---

## v2.0 — Platform Capabilities

**Theme:** Transform from a polling tool into a platform with integrations, compliance, and scale.

| Feature | Notes |
|---------|-------|
| **Compliance views** | CIS benchmark checks (configurable), patch compliance % dashboards. |
| **TimescaleDB** | Migrate poll_results to TimescaleDB hypertables for efficient time-series queries at scale (>10M rows). |
| **Distributed polling** | Multiple polling worker nodes for large environments (1000+ servers). Use Hangfire `ServerCount` scaling. |
| **AD/LDAP discovery** | Scan AD OU and auto-suggest servers to onboard. |
| **Ansible/Salt integration** | Push discovered inventory into Ansible inventory or SaltStack grains. |
| **Change detection** | Compare snapshots and alert on configuration drift (new service, IP change, OS build change). |
| **Software licensing** | Installed software analysis with vendor normalization and license count aggregation. |
| **REST/GraphQL API** | Public API versioning (v1, v2) for external integrations. |
| **Multi-tenancy** | Namespace servers by tenant/department for MSPs or large enterprises. |
| **Mobile-responsive UI** | PWA for on-call engineers. |
| **SAML / OIDC SSO** | Replace Windows Auth with SSO for cloud-integrated environments. |

**Danger Zones in v2:**
- **Scale of polling:** 1000 servers × 60-minute interval = ~16 polls/minute average, with bursts. A single Hangfire worker with 10 concurrent jobs handles ~600 servers. At 1000+, run 2+ worker instances and partition servers across queues.
- **Poll result storage growth:** At 1000 servers × 24 polls/day × 365 days, `poll_results` reaches ~8.7M rows per year. Plan TimescaleDB migration before hitting 5M rows. Index bloat on JSONB GIN indexes is significant at this scale.
- **Installed software collection overhead:** Running `Get-Package` or registry enumeration on a server can take 30–120 seconds and noticeably loads the target. Collect separately on a longer interval (daily, not hourly). Warn users in the UI.
- **Credential sprawl:** As the number of credential references grows, rotation becomes a manual burden. v2 should integrate with CyberArk/HashiCorp Vault for auto-rotation via the `ICredentialStore` interface.

---

## Risk Register

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| WMI DCOM firewall issues | High | Medium | Default to WinRM; document DCOM as optional |
| Credential vault file lost without backup | Medium | Critical | Backup vault file daily; document restore procedure |
| Poll worker thread starvation | Medium | High | Per-server mutex, configurable concurrency limit |
| PostgreSQL disk full (poll data) | Low | High | Automated retention job + storage monitoring |
| DPAPI tied to dead service account | Low | Critical | Store vault backup; re-encryption admin command |
| WinRM HTTPS cert expiry | Medium | Medium | Alert on cert expiry 30 days before; automate with internal CA |
| Collector causes load on production server | Low | High | Rate-limit polls; document expected overhead; offer off-hours scheduling |

---

## Quick-Start Acceptance Checklist (v1 Done)

```
□ PostgreSQL running, schema applied, seed data loaded
□ API starts without errors: GET /health → 200 Healthy
□ Frontend loads: https://servercat.corp.local
□ Can add a server via UI
□ Connectivity test passes for at least one target
□ Manual poll succeeds: POST /api/servers/{id}/poll
□ Latest poll result visible in server detail tabs
□ Note can be added and appears in notes tab
□ Operational log shows server.created and poll.success entries
□ Hangfire dashboard accessible at /hangfire (admin only)
□ Windows Service survives a reboot
□ Backup script runs successfully
□ Restore tested from backup
```
