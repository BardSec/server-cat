# Architecture Reference — Server Catalog & Polling

## Stack Decision

**Chosen: Option A — .NET 8 (ASP.NET Core) + React + PostgreSQL + Hangfire**

### Justification for Windows-centric on-prem environment

| Factor | .NET 8 | Python | Node |
|--------|--------|--------|------|
| WinRM/PowerShell SDK | Native (`System.Management.Automation`) | `pywinrm` (third-party, less maintained) | `node-powershell` (thin wrapper only) |
| WMI/CIM | Native (`Microsoft.Management.Infrastructure`) | `wmi` module (limited) | Not practical |
| Windows DPAPI | Native (`System.Security.Cryptography.ProtectedData`) | ctypes hack | Not available |
| Windows Service hosting | `UseWindowsService()` built-in | NSSM wrapper needed | NSSM wrapper needed |
| Windows Authentication / AD | First-class, IIS Negotiate | Requires SSPI library | Requires third-party |
| Enterprise support | Strong (.NET is standard in Windows shops) | Variable | Variable |
| Background jobs | Hangfire (embedded dashboard, PostgreSQL backend) | Celery (needs Redis) | BullMQ (needs Redis) |

Python Option B or Node Option C are viable if the team prefers them, but they require additional infrastructure (Redis) and rely on third-party libraries for the Windows-native integrations that are first-class in .NET.

---

## Component Diagram

```
                        ┌──────────────────┐
                        │  Browser / Client │
                        └────────┬─────────┘
                                 │ HTTPS 443
                        ┌────────▼─────────┐
                        │  Nginx / IIS ARR  │  ← Reverse proxy + TLS termination
                        │  (TLS terminator) │
                        └────────┬─────────┘
                    ┌────────────┴────────────┐
                    │                         │
           ┌────────▼────────┐      ┌─────────▼──────────┐
           │  React SPA      │      │  ASP.NET Core API   │
           │  (static files) │      │  Port 5000 (HTTP)   │
           └─────────────────┘      └─────────┬───────────┘
                                              │
                                   ┌──────────▼──────────┐
                                   │   PostgreSQL 16      │
                                   │   Port 5432          │
                                   │   (local or LAN)     │
                                   └──────────┬───────────┘
                                              │
                                   ┌──────────▼──────────┐
                                   │  Hangfire Worker     │
                                   │  (in-process or      │
                                   │   separate service)  │
                                   └──────────┬───────────┘
                                              │
                        ┌─────────────────────┼────────────────────┐
                        │                     │                    │
               ┌────────▼──────┐   ┌──────────▼──────┐  ┌────────▼──────┐
               │ WinRM/PS      │   │  WMI/CIM         │  │  SNMP v2/v3   │
               │ Collector     │   │  Collector        │  │  Collector    │
               │ Port 5986     │   │  Port 135+dyn     │  │  Port 161/UDP │
               └────────┬──────┘   └──────────┬────────┘  └────────┬──────┘
                        │                     │                    │
                        └──────────┬──────────┘                    │
                                   │                               │
                        ┌──────────▼──────────────────────────────▼──────┐
                        │              Target Windows Servers              │
                        └──────────────────────────────────────────────────┘
```

---

## Solution Structure

```
server-cat/
├── docs/                          # Architecture, PRD, threat model
├── backend/
│   ├── ServerCat.sln
│   ├── ServerCat.Api/             # ASP.NET Core Web API (entry point)
│   │   ├── Controllers/
│   │   │   ├── ServersController.cs
│   │   │   ├── PollsController.cs
│   │   │   ├── NotesController.cs
│   │   │   ├── CredentialsController.cs
│   │   │   ├── GroupsController.cs
│   │   │   └── DashboardController.cs
│   │   ├── Middleware/
│   │   │   ├── ErrorHandlingMiddleware.cs
│   │   │   └── AuditMiddleware.cs
│   │   ├── Program.cs
│   │   └── appsettings.json
│   │
│   ├── ServerCat.Core/            # Domain: entities, interfaces, DTOs (no deps)
│   │   ├── Entities/
│   │   │   ├── Server.cs
│   │   │   ├── Poll.cs
│   │   │   ├── PollResult.cs
│   │   │   ├── Note.cs
│   │   │   ├── OperationalLog.cs
│   │   │   ├── CredentialReference.cs
│   │   │   └── ServerGroup.cs
│   │   ├── Interfaces/
│   │   │   ├── ICollector.cs
│   │   │   ├── ICredentialStore.cs
│   │   │   ├── IPollingService.cs
│   │   │   └── IServerRepository.cs
│   │   ├── DTOs/
│   │   │   ├── ServerDto.cs
│   │   │   ├── PollResultDto.cs
│   │   │   ├── NoteDto.cs
│   │   │   └── DashboardDto.cs
│   │   └── Enums/
│   │       ├── CollectorType.cs
│   │       ├── Criticality.cs
│   │       └── PollStatus.cs
│   │
│   └── ServerCat.Infrastructure/  # EF Core, collectors, security, jobs
│       ├── Data/
│       │   ├── ApplicationDbContext.cs
│       │   └── Migrations/
│       ├── Collectors/
│       │   ├── CollectorFactory.cs
│       │   ├── WinRmCollector.cs
│       │   ├── WmiCollector.cs
│       │   └── SnmpCollector.cs
│       ├── Security/
│       │   ├── DpapiCredentialStore.cs
│       │   └── VaultFile.cs
│       ├── Jobs/
│       │   └── ServerPollingJob.cs
│       └── Services/
│           └── PollingService.cs
│
├── frontend/
│   ├── package.json
│   ├── vite.config.ts
│   ├── tsconfig.json
│   ├── index.html
│   └── src/
│       ├── main.tsx
│       ├── App.tsx
│       ├── api/
│       │   └── client.ts
│       ├── types/
│       │   └── index.ts
│       ├── pages/
│       │   ├── ServerCatalog.tsx
│       │   ├── ServerDetail.tsx
│       │   └── AddEditServer.tsx
│       └── components/
│           ├── StatusBadge.tsx
│           ├── ServerTable.tsx
│           ├── DiskChart.tsx
│           └── NotesPanel.tsx
│
├── deploy/
│   ├── windows/
│   │   ├── Install-ServerCat.ps1
│   │   └── Uninstall-ServerCat.ps1
│   ├── nginx/
│   │   └── servercat.conf
│   └── postgres/
│       └── init.sql
│
└── scripts/
    ├── seed.sql
    └── Backup-ServerCat.ps1
```

---

## Data Flow: Poll Execution

```
Hangfire Scheduler (cron per server)
  │
  ▼
ServerPollingJob.Execute(serverId)
  │
  ├─ Load Server + CredentialReference from DB
  │
  ├─ DpapiCredentialStore.Unprotect(secretStoreKey) → plaintext credential
  │
  ├─ CollectorFactory.Create(server.CollectorType) → ICollector
  │
  ├─ ICollector.CollectAsync(server, credential) → CollectorResult
  │   ├─ WinRmCollector: opens WinRM session, runs PS scripts
  │   ├─ WmiCollector: opens CIM session, queries CIMV2
  │   └─ SnmpCollector: sends SNMP GET/WALK, parses MIB values
  │
  ├─ Zero out credential from memory
  │
  ├─ Write Poll (status=running) → DB
  ├─ Map CollectorResult → PollResult entity
  ├─ Write PollResult → DB
  ├─ Update Poll (status=success/partial/failed, duration)
  │
  └─ Write OperationalLog entry
```

---

## Hangfire Configuration

- **Storage:** PostgreSQL (`Hangfire.PostgreSql` package)
- **Dashboard:** `/hangfire` — protected with `[Authorize(Roles="Admin")]`
- **Retry:** 3 retries with exponential backoff (1 min, 5 min, 15 min)
- **Concurrency:** Max 10 concurrent polling jobs; per-server mutex via `DisableConcurrentExecution`
- **Recurring jobs:** Created/updated on server add/edit using `RecurringJob.AddOrUpdate(serverId, ...)`
- **Job queue:** Default queue for polls; separate `maintenance` queue for cleanup jobs

---

## Database Design Decisions

1. **JSONB for poll_results sub-fields** (disk_volumes, services, network_adapters, cpu_info): Avoids over-normalization for data that varies by OS and collector. GIN indexes on frequently queried JSON keys.

2. **Separate polls + poll_results tables**: `polls` tracks the run metadata (timing, status, error). `poll_results` holds the collected data. This allows querying "all failed polls" without loading result payloads.

3. **Soft delete on servers**: `is_active = false` preserves historical poll data. Hard delete cascade is available via admin endpoint only.

4. **operational_logs is append-only**: DB user has INSERT only (no UPDATE/DELETE) on this table.

5. **TimescaleDB** (optional, Phase 2): If `poll_results` grows > 10M rows, convert to a TimescaleDB hypertable partitioned by `collected_at`. For v1, standard PostgreSQL B-tree indexes on `(server_id, collected_at DESC)` are sufficient.
