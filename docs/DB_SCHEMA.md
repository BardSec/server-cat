# Database Schema — Server Catalog & Polling

**Database:** PostgreSQL 16
**Extensions:** `uuid-ossp`, `pgcrypto` (for `gen_random_uuid()`)

---

## Full SQL Schema

```sql
-- ============================================================
-- Extensions
-- ============================================================
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- ============================================================
-- ENUM types
-- ============================================================
CREATE TYPE server_environment AS ENUM ('production', 'staging', 'development', 'dr', 'lab');
CREATE TYPE server_criticality AS ENUM ('critical', 'high', 'medium', 'low');
CREATE TYPE os_type AS ENUM ('windows', 'linux', 'unknown');
CREATE TYPE auth_method AS ENUM ('winrm_kerberos', 'winrm_ntlm', 'winrm_basic', 'wmi_dcom', 'snmp_v2', 'snmp_v3');
CREATE TYPE poll_status AS ENUM ('running', 'success', 'partial', 'failed');
CREATE TYPE poll_trigger AS ENUM ('scheduler', 'manual', 'api');
CREATE TYPE log_severity AS ENUM ('info', 'warning', 'error', 'critical');
CREATE TYPE user_role AS ENUM ('viewer', 'engineer', 'admin');

-- ============================================================
-- credential_references
-- No actual secrets stored here. Secrets live in the vault file.
-- ============================================================
CREATE TABLE credential_references (
    id                  UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    name                VARCHAR(255) NOT NULL UNIQUE,
    description         TEXT,
    auth_method         auth_method  NOT NULL,
    username_hint       VARCHAR(255),           -- display hint only (e.g. "CORP\svc-poll")
    secret_store_key    VARCHAR(500) NOT NULL,   -- opaque key into vault (e.g. "vault:guid")
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by          VARCHAR(255) NOT NULL DEFAULT 'system'
);

-- ============================================================
-- servers
-- Core catalog table
-- ============================================================
CREATE TABLE servers (
    id                          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    hostname                    VARCHAR(255) NOT NULL,
    ip_address                  INET,
    display_name                VARCHAR(255),
    environment                 server_environment NOT NULL DEFAULT 'production',
    owner                       VARCHAR(255),           -- team or person responsible
    site                        VARCHAR(100),           -- physical/logical site (e.g. "DC-East")
    criticality                 server_criticality NOT NULL DEFAULT 'medium',
    os_type                     os_type NOT NULL DEFAULT 'windows',
    collector_type              VARCHAR(50) NOT NULL DEFAULT 'winrm', -- winrm, wmi, snmp
    credential_ref_id           UUID REFERENCES credential_references(id) ON DELETE SET NULL,
    polling_enabled             BOOLEAN NOT NULL DEFAULT TRUE,
    polling_interval_minutes    INTEGER NOT NULL DEFAULT 60
                                    CHECK (polling_interval_minutes BETWEEN 5 AND 10080),
    tags                        TEXT[] NOT NULL DEFAULT '{}',
    is_active                   BOOLEAN NOT NULL DEFAULT TRUE,
    created_at                  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at                  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by                  VARCHAR(255) NOT NULL DEFAULT 'system',

    CONSTRAINT servers_hostname_unique UNIQUE (hostname)
);

-- Indexes for common catalog filters
CREATE INDEX idx_servers_environment  ON servers(environment) WHERE is_active;
CREATE INDEX idx_servers_criticality  ON servers(criticality) WHERE is_active;
CREATE INDEX idx_servers_site         ON servers(site) WHERE is_active;
CREATE INDEX idx_servers_active       ON servers(is_active);
CREATE INDEX idx_servers_tags         ON servers USING GIN(tags);

-- ============================================================
-- server_groups
-- Named logical groups (e.g., "SQL Servers", "DMZ")
-- ============================================================
CREATE TABLE server_groups (
    id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    name        VARCHAR(100) NOT NULL UNIQUE,
    description TEXT,
    color       VARCHAR(7)   NOT NULL DEFAULT '#6B7280',  -- hex color for UI badge
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE TABLE server_group_memberships (
    server_id   UUID NOT NULL REFERENCES servers(id) ON DELETE CASCADE,
    group_id    UUID NOT NULL REFERENCES server_groups(id) ON DELETE CASCADE,
    added_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    added_by    VARCHAR(255),
    PRIMARY KEY (server_id, group_id)
);

-- ============================================================
-- polls
-- One row per poll execution run
-- ============================================================
CREATE TABLE polls (
    id              UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    server_id       UUID        NOT NULL REFERENCES servers(id) ON DELETE CASCADE,
    started_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at    TIMESTAMPTZ,
    status          poll_status NOT NULL DEFAULT 'running',
    collector_type  VARCHAR(50),                -- which collector was used
    triggered_by    poll_trigger NOT NULL DEFAULT 'scheduler',
    triggered_actor VARCHAR(255),               -- user who triggered (if manual)
    error_message   TEXT,
    duration_ms     INTEGER
);

CREATE INDEX idx_polls_server_id   ON polls(server_id, started_at DESC);
CREATE INDEX idx_polls_status      ON polls(status);
CREATE INDEX idx_polls_started_at  ON polls(started_at DESC);

-- ============================================================
-- poll_results
-- Structured inventory snapshot per poll
-- JSONB columns use GIN indexes for querying inside documents
-- ============================================================
CREATE TABLE poll_results (
    id                      UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    poll_id                 UUID        NOT NULL UNIQUE REFERENCES polls(id) ON DELETE CASCADE,
    server_id               UUID        NOT NULL REFERENCES servers(id) ON DELETE CASCADE,
    collected_at            TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    -- Host identity
    hostname_resolved       VARCHAR(255),
    fqdn                    VARCHAR(500),
    domain                  VARCHAR(255),

    -- OS
    os_name                 VARCHAR(255),       -- e.g., "Windows Server 2019 Standard"
    os_version              VARCHAR(100),       -- e.g., "10.0.17763"
    os_build                VARCHAR(50),        -- e.g., "17763.6532"
    install_date            TIMESTAMPTZ,

    -- Uptime
    last_boot_time          TIMESTAMPTZ,
    uptime_seconds          BIGINT,

    -- Hardware
    -- cpu_info: {"model":"Intel Xeon E5-2690","sockets":2,"cores_per_socket":8,"logical_processors":32}
    cpu_info                JSONB,
    memory_total_mb         BIGINT,
    memory_available_mb     BIGINT,

    -- Storage
    -- disk_volumes: [{"drive":"C:","label":"System","filesystem":"NTFS","size_gb":127.9,"free_gb":45.2,"free_pct":35.3}]
    disk_volumes            JSONB,

    -- Network
    -- network_adapters: [{"name":"Ethernet0","ip":"10.0.1.50","subnet":"255.255.255.0","mac":"00:11:22:33:44:55","gateway":"10.0.1.1","dns_servers":["10.0.0.10","10.0.0.11"]}]
    network_adapters        JSONB,

    -- Services (Windows)
    -- services: [{"name":"Spooler","display_name":"Print Spooler","status":"Running","start_type":"Automatic"}]
    services                JSONB,

    -- Patches
    last_update_installed   DATE,               -- last Windows Update install date (best-effort)
    pending_reboot          BOOLEAN,

    -- Installed software (optional — expensive to collect)
    -- installed_software: [{"name":"Microsoft Visual C++ 2019","version":"14.28.29910","vendor":"Microsoft","install_date":"2023-01-15"}]
    installed_software      JSONB,

    -- Raw collector output (for debugging and re-parsing)
    raw_data                JSONB,

    CONSTRAINT poll_results_poll_id_unique UNIQUE (poll_id)
);

-- GIN indexes for querying inside JSONB columns
CREATE INDEX idx_poll_results_server_collected ON poll_results(server_id, collected_at DESC);
CREATE INDEX idx_poll_results_disk_volumes     ON poll_results USING GIN(disk_volumes);
CREATE INDEX idx_poll_results_services         ON poll_results USING GIN(services);
CREATE INDEX idx_poll_results_collected_at     ON poll_results(collected_at DESC);

-- ============================================================
-- notes
-- Free-form markdown notes per server
-- ============================================================
CREATE TABLE notes (
    id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    server_id   UUID        NOT NULL REFERENCES servers(id) ON DELETE CASCADE,
    content     TEXT        NOT NULL,
    is_pinned   BOOLEAN     NOT NULL DEFAULT FALSE,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMPTZ,
    created_by  VARCHAR(255) NOT NULL,
    updated_by  VARCHAR(255)
);

CREATE INDEX idx_notes_server_id ON notes(server_id, created_at DESC);
CREATE INDEX idx_notes_pinned    ON notes(server_id, is_pinned) WHERE is_pinned;

-- ============================================================
-- operational_logs
-- Immutable structured audit trail
-- App DB user has INSERT only — no UPDATE or DELETE
-- ============================================================
CREATE TABLE operational_logs (
    id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    server_id   UUID        REFERENCES servers(id) ON DELETE SET NULL,  -- nullable: system events
    event_type  VARCHAR(100) NOT NULL,   -- e.g., "server.created", "poll.failed", "note.added"
    severity    log_severity NOT NULL DEFAULT 'info',
    summary     TEXT        NOT NULL,
    details     JSONB,                   -- structured diff or context (no secrets)
    actor       VARCHAR(255) NOT NULL,   -- user identity or 'system'
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_oplogs_server_id   ON operational_logs(server_id, created_at DESC);
CREATE INDEX idx_oplogs_event_type  ON operational_logs(event_type);
CREATE INDEX idx_oplogs_actor       ON operational_logs(actor);
CREATE INDEX idx_oplogs_created_at  ON operational_logs(created_at DESC);
CREATE INDEX idx_oplogs_severity    ON operational_logs(severity);

-- ============================================================
-- app_users (local auth fallback — skip if using Windows Auth only)
-- ============================================================
CREATE TABLE app_users (
    id              UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    username        VARCHAR(255) NOT NULL UNIQUE,
    display_name    VARCHAR(255),
    role            user_role    NOT NULL DEFAULT 'viewer',
    password_hash   VARCHAR(500),       -- bcrypt hash; NULL if Windows Auth only
    is_active       BOOLEAN NOT NULL DEFAULT TRUE,
    last_login_at   TIMESTAMPTZ,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by      VARCHAR(255)
);

-- ============================================================
-- DB User permissions
-- Run as superuser during setup
-- ============================================================
-- CREATE ROLE servercat_app WITH LOGIN PASSWORD 'changeme';
-- GRANT CONNECT ON DATABASE servercat TO servercat_app;
-- GRANT USAGE ON SCHEMA public TO servercat_app;
-- GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO servercat_app;
-- GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO servercat_app;
--
-- Revoke destructive rights on audit log:
-- REVOKE UPDATE, DELETE ON operational_logs FROM servercat_app;

-- ============================================================
-- Triggers: auto-update updated_at
-- ============================================================
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_servers_updated_at
    BEFORE UPDATE ON servers
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER trg_credential_references_updated_at
    BEFORE UPDATE ON credential_references
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
```

---

## Key Index Strategy

| Query Pattern | Index |
|---------------|-------|
| List servers by environment/criticality/site | Composite B-tree on filtered columns |
| Server tags filter | GIN on `servers.tags` |
| Latest poll for server | B-tree on `polls(server_id, started_at DESC)` |
| Poll result by server + date range | B-tree on `poll_results(server_id, collected_at DESC)` |
| Disks with free % < threshold | GIN on `poll_results.disk_volumes` + computed at query time |
| Services by status | GIN on `poll_results.services` |
| Audit log by actor/event | B-tree on `operational_logs(actor)`, `operational_logs(event_type)` |

---

## Retention Policy (v1 — manual, v2 — automated)

- `poll_results`: Retain 365 days by default (configurable)
- `polls`: Retain 730 days (header records are small)
- `operational_logs`: Retain forever (append-only, compliance)
- Cleanup job: Hangfire recurring job runs weekly, deletes rows older than retention window

---

## Entity Relationships

```
servers 1──────────* polls
servers 1──────────1 poll_results (via polls.id)
servers *──────────* server_groups (via server_group_memberships)
servers 1──────────* notes
servers 1──────────* operational_logs
servers *──────────1 credential_references
```
