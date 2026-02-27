# Product Requirements Document — Server Catalog & Polling (v1)

**Status:** Draft
**Authors:** Platform Engineering Team
**Date:** 2026-02-27
**Stack:** .NET 8 (ASP.NET Core) + React + PostgreSQL + Hangfire

---

## 1. Problem Statement

Operations teams manage hundreds of Windows servers across multiple sites with no single authoritative inventory. Stale spreadsheets, no visibility into patch health or disk capacity, and inconsistent manual audits drive up incident response times and cause compliance gaps.

This application creates a **live, queryable catalog** of all servers with automated polling so engineers have one reliable place for current state and history.

---

## 2. Goals

| # | Goal |
|---|------|
| G1 | Single catalog of all managed servers with structured metadata |
| G2 | Automated periodic polling to collect OS, hardware, network, services, and patch data |
| G3 | Web UI with filtering, server detail drill-down, and change history |
| G4 | Structured + free-form notes system per server |
| G5 | Credentials never stored in plaintext; use Windows DPAPI by default |
| G6 | Works in segmented networks (firewall-safe, port-specific connectivity) |
| G7 | Role-based access: Read-only viewers, Engineers, Admins |

---

## 3. Non-Goals (v1)

- **No agent installation on targets** — agentless polling only
- **No Linux support** — Phase 2; stubs are included but untested
- **No configuration management or remediation** — read-only from targets
- **No real-time streaming** — polling is periodic, not push-based
- **No public cloud integrations** (AWS/Azure/GCP APIs) — Phase 3
- **No full software license audit** — installed software is optional and explicitly warned as expensive
- **No ServiceNow or CMDB sync** — Phase 2 integration
- **No automated alerting / PagerDuty** — status flags are visible in UI only; alert routing is Phase 2

---

## 4. User Personas

| Persona | Description |
|---------|-------------|
| **Infrastructure Engineer** | Adds servers, reviews polls, adds operational notes |
| **Operations Manager** | Views dashboards, filters by criticality/site, exports reports |
| **Security Analyst** | Reviews patch status, service inventory, audit logs |
| **System Administrator** | Manages credentials references, configures polling intervals |
| **Read-only Viewer** | View-only access to catalog and server detail |

---

## 5. User Stories

### Catalog Management
- **US-01** As an engineer, I can add a server (hostname/IP, environment, site, owner, criticality) so it enters the managed inventory.
- **US-02** As an engineer, I can edit server metadata without losing poll history.
- **US-03** As an engineer, I can soft-delete a server (mark inactive) without losing historical data.
- **US-04** As an engineer, I can assign one or more tags/groups to a server (e.g., "SQL Servers", "DMZ").
- **US-05** As an admin, I can manage credential references that map to secrets in the secure store (no actual passwords in the UI or database).

### Polling
- **US-06** As the system, I poll each enabled server on its configured interval (default 60 minutes).
- **US-07** As an engineer, I can trigger an immediate poll on any server.
- **US-08** As an engineer, I can see the last poll result (success/partial/failed) at a glance on the catalog table.
- **US-09** As an engineer, I can see the full poll history for a server and compare snapshots.
- **US-10** As an admin, I can configure per-server polling intervals (15–1440 minutes).

### Server Detail & History
- **US-11** As an engineer, I can view the current OS, hardware, disk, network, and service state in tabs.
- **US-12** As an engineer, I can see disk volumes colored by free-space thresholds (green ≥ 20 %, yellow 10–20 %, red < 10 %).
- **US-13** As an engineer, I can see which services changed status between two polls.
- **US-14** As an engineer, I can view the full history of all polls (when run, duration, status).

### Notes & Audit
- **US-15** As an engineer, I can add a free-form note to any server (supports markdown).
- **US-16** As an engineer, I can pin an important note to the top of the notes list.
- **US-17** As the system, every create/update/delete of any entity is recorded in the operational log with actor and timestamp.
- **US-18** As a security analyst, I can filter the operational log by event type, actor, and date range.

### Connectivity & Validation
- **US-19** As an engineer, I can run a "connectivity test" before saving a server to verify the polling path is reachable.
- **US-20** As an engineer, I see validation errors when I submit a server with a duplicate hostname or missing required fields.

### Access Control
- **US-21** As an admin, I can assign users to roles: Viewer, Engineer, Admin.
- **US-22** As the system, unauthenticated requests return 401; unauthorized role operations return 403.

---

## 6. Status Rules (v1)

Status is derived — not stored — at query time from the most recent poll_result:

| Color | Condition |
|-------|-----------|
| **Green** | Last poll < 2× interval ago AND status = success AND no red sub-checks |
| **Yellow** | Last poll 2–4× interval ago OR status = partial OR any yellow sub-check |
| **Red** | Last poll > 4× interval ago OR status = failed OR any red sub-check |
| **Gray** | Never polled OR polling disabled |

Sub-checks that contribute to yellow/red:
- Disk free % < 10 % → red; < 20 % → yellow
- Critical service stopped → red; non-critical stopped → yellow
- Last patch > 90 days → yellow; > 180 days → red

---

## 7. Acceptance Criteria (v1 Done)

- [ ] Server can be added, edited, and soft-deleted via UI and API
- [ ] WinRM collector retrieves all v1 data categories successfully from a test Windows Server 2019+ host
- [ ] Hangfire scheduler runs polls on configured intervals and stores results
- [ ] UI shows catalog table with green/yellow/red indicators
- [ ] Server detail shows all tabs with latest data and history list
- [ ] Notes can be added/edited/pinned
- [ ] All mutations appear in operational_logs
- [ ] Credentials are stored encrypted (DPAPI) and never returned in API responses
- [ ] Application installs and runs as a Windows Service
- [ ] All API endpoints return correct 2xx/4xx/5xx with structured error bodies
