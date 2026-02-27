# Threat Model — Server Catalog & Polling

**Version:** 1.0
**Date:** 2026-02-27
**Methodology:** STRIDE per component

---

## 1. Assets & Trust Boundaries

```
┌─────────────────────────────────────────────────────────────────┐
│  CLIENT BROWSER (Untrusted)                                     │
└───────────────────┬─────────────────────────────────────────────┘
                    │ HTTPS (TLS 1.2+)
┌───────────────────▼─────────────────────────────────────────────┐
│  APP SERVER (Trusted Zone)                                       │
│  ┌─────────────┐  ┌──────────────┐  ┌──────────────────────┐   │
│  │ ASP.NET API │  │   Hangfire   │  │   DPAPI Vault File   │   │
│  │  (IIS/Svc)  │  │   Worker     │  │   (encrypted at rest)│   │
│  └──────┬──────┘  └──────┬───────┘  └──────────────────────┘   │
│         │                │                                       │
│  ┌──────▼────────────────▼──────┐                               │
│  │        PostgreSQL DB          │                               │
│  └──────────────────────────────┘                               │
└───────────────────┬─────────────────────────────────────────────┘
                    │ WinRM (5985/5986), WMI (135/dynamic), SNMP (161)
┌───────────────────▼─────────────────────────────────────────────┐
│  TARGET SERVERS (Managed — partial trust)                        │
│  Windows Server hosts                                            │
└─────────────────────────────────────────────────────────────────┘
```

---

## 2. STRIDE Analysis

### 2.1 Credential Handling

**Threat: S — Spoofing via credential theft**
- Credentials decrypted in memory during polling; a process dump reveals plaintext.
- *Mitigation:* Use SecureString/pinned memory where possible. Rotate credentials regularly. Disable memory dump on the app server (crash dump settings).

**Threat: T — Tampering with vault file**
- An attacker with local filesystem access modifies the DPAPI-encrypted vault file.
- *Mitigation:* DPAPI ties encryption to the service account SID + machine entropy; a tampered file causes decryption failure, not silent data exposure. Use NTFS ACLs so only the service account can read the vault file.

**Threat: I — Information disclosure of credentials via API**
- The `/api/credentials` endpoint accidentally returns secret material.
- *Mitigation:* The `CredentialReference` entity stores only a `SecretStoreKey` (opaque lookup key), never the actual secret. All response DTOs are explicitly mapped; no raw entity serialization. Add an integration test asserting the response body contains no field named `password`, `secret`, or `key_value`.

**Threat: R — Repudiation of credential changes**
- An admin deletes a credential reference with no audit trail.
- *Mitigation:* All CRUD operations on `credential_references` write a row to `operational_logs` with actor, timestamp, and changed fields (excluding secret material).

### 2.2 Web Application (API + UI)

**Threat: I — SQL Injection**
- All database access uses Entity Framework Core parameterized queries. Raw SQL is only used in explicit migration scripts.
- *Mitigation:* Use EF Core fluent API. If raw SQL is required in queries, use `FromSqlRaw` with `SqlParameter` objects only. Add SCA lint rule blocking `string.Format` in SQL contexts.

**Threat: I — Cross-Site Scripting (XSS)**
- The Notes field accepts markdown. A stored XSS attack via note content.
- *Mitigation:* Render markdown client-side with a sanitizer (DOMPurify). The API accepts and returns raw markdown text; sanitization is the client's responsibility. Never use `dangerouslySetInnerHTML` without DOMPurify.

**Threat: E — Broken Access Control / Privilege Escalation**
- A Viewer role user calls a mutation endpoint.
- *Mitigation:* Decorate all controllers with `[Authorize(Roles = "...")]`. Write policy-based authorization tests. Log all 403 responses to the operational log.

**Threat: E — CSRF**
- Forged requests from a malicious site.
- *Mitigation:* Use `SameSite=Strict` cookies. Require `Authorization: Bearer` header for all state-mutating API requests (prevents simple-form CSRF). Enable CORS policy limited to known frontend origins.

**Threat: D — Rate limit / DoS via poll trigger API**
- An attacker (or bug) triggers thousands of immediate polls.
- *Mitigation:* Rate-limit `POST /api/servers/{id}/poll` to 1 request per server per 60 seconds using a distributed rate limiter backed by the DB. Hangfire `DisableConcurrentExecution` prevents concurrent runs per server.

**Threat: I — Sensitive data in logs**
- Structured logging accidentally serializes objects containing decrypted credentials.
- *Mitigation:* Use `[SensitiveData]` custom attribute + Serilog destructuring policy to redact fields. Never log `CollectorContext` objects directly.

### 2.3 Polling / Lateral Movement Risk

**Threat: E — Lateral movement via compromised service account**
- The polling service account has domain admin rights and is used to pivot to other systems.
- *Mitigation (Principle of Least Privilege):*

| Collector | Required Rights | Recommended Implementation |
|-----------|-----------------|----------------------------|
| WinRM/PowerShell | Member of `Remote Management Users` group on target | Constrained PowerShell endpoint (JEA) restricts runnable commands |
| WMI/CIM | Member of `Distributed COM Users` + WMI namespace DACL grant on target | Use `root/CIMV2` namespace only; deny `root/default`, `root/subscription` |
| SNMP v2 | Community string access (read-only community) | Use unique per-device community strings; block write community |
| SNMP v3 | `authPriv` mode (auth + privacy) | Use SHA-256 + AES-128; store SNMPv3 credentials in vault |

  - Never use a domain admin account for polling. Create a dedicated `svc-servercat-poll` service account.
  - Apply JEA (Just Enough Administration) endpoint on Windows targets: publish a constrained session configuration that only allows specific `Get-*` cmdlets.

**Threat: T — Man-in-the-Middle on WinRM**
- An attacker intercepts WinRM traffic and replays or modifies responses.
- *Mitigation:* Use WinRM over HTTPS (port 5986) with certificate validation. In segmented networks where HTTPS WinRM is impractical, use Kerberos authentication (encrypted channel), never plain HTTP with Basic auth.

**Threat: S — SNMP Community String Brute-force**
- Public SNMP v2 community strings are guessable (`public`, `private`).
- *Mitigation:* Enforce non-default community strings. Prefer SNMPv3 `authPriv`. Block SNMP port 161 from all sources except the app server.

### 2.4 Database

**Threat: I — PostgreSQL connection string exposure**
- Connection string in appsettings.json is read by unauthorized users.
- *Mitigation:* Store connection string in environment variables or Windows DPAPI-encrypted config. Use `IConfiguration` with `EnvironmentVariables` provider. Rotate DB password on schedule. Use a least-privilege DB user (`servercat_app`) with only `SELECT/INSERT/UPDATE/DELETE` on app tables; no `CREATE`, `DROP`, or `SUPERUSER`.

**Threat: T — Database tampering / audit log manipulation**
- An attacker with DB access deletes rows from `operational_logs`.
- *Mitigation:* Grant `servercat_app` role only `INSERT` on `operational_logs` (no `UPDATE` or `DELETE`). Use `GRANT INSERT ON operational_logs TO servercat_app`. Admins cannot delete audit rows through the application layer.

---

## 3. RBAC Model

| Role | Permissions |
|------|-------------|
| **Viewer** | GET on all resources |
| **Engineer** | Viewer + add/edit servers, add notes, trigger polls |
| **Admin** | Engineer + manage credentials, manage users, delete servers, view audit logs |

Authentication: Windows Authentication (Kerberos/NTLM via IIS) **or** local JWT-based authentication (fallback for non-domain accounts). Configured per-deployment.

---

## 4. Audit Log Guarantees

Every state-mutating operation logs:
- `actor` — authenticated user identity (domain\username or JWT subject)
- `event_type` — structured constant (e.g., `server.created`, `poll.triggered`)
- `severity` — info / warning / error
- `summary` — human-readable description
- `details` — JSONB diff (old → new values, excluding secrets)
- `created_at` — UTC timestamp, set by database `DEFAULT NOW()`

Audit log rows are `INSERT`-only at the application layer (no UPDATE/DELETE granted to app DB user).

---

## 5. Network Exposure Summary

| Port | Protocol | Direction | Purpose |
|------|----------|-----------|---------|
| 443 | HTTPS | Inbound | UI + API traffic |
| 5432 | TCP | App→DB | PostgreSQL (local or same network segment) |
| 5986 | HTTPS | App→Target | WinRM HTTPS |
| 5985 | HTTP | App→Target | WinRM HTTP (fallback, Kerberos only) |
| 135+dynamic | TCP | App→Target | WMI/CIM DCOM (prefer WinRM) |
| 161 | UDP | App→Target | SNMP |

Minimize dynamic DCOM port range. Consider replacing WMI/DCOM with WinRM/CIM over WSMAN entirely.

---

## 6. Secrets Lifecycle

```
┌─────────────────────────────────────────────────────┐
│  Admin enters credential via /api/credentials POST   │
│  { name, auth_method, username_hint, secret_value }  │
└──────────────┬──────────────────────────────────────┘
               │
               ▼
┌──────────────────────────────────────────────────────┐
│  DpapiCredentialStore.Protect(secret_value)           │
│  → DPAPI encrypts using service account + machine key │
│  → Saves ciphertext to vault.json (NTFS ACL locked)   │
│  → Returns opaque SecretStoreKey (GUID)               │
└──────────────┬───────────────────────────────────────┘
               │ Only SecretStoreKey stored in DB
               ▼
┌──────────────────────────────────────────────────────┐
│  credential_references.secret_store_key = "vault:<guid>" │
│  (No plaintext, no hash, no reversible encoding)     │
└──────────────┬───────────────────────────────────────┘
               │ At poll time
               ▼
┌──────────────────────────────────────────────────────┐
│  DpapiCredentialStore.Unprotect(SecretStoreKey)       │
│  → Decrypts in memory → passed to collector           │
│  → Zeroed from memory after use                       │
└──────────────────────────────────────────────────────┘
```

**Enterprise vault integration:** The `ICredentialStore` interface allows swapping DPAPI for HashiCorp Vault, CyberArk, or Azure Key Vault by implementing the interface and changing DI registration. See `infrastructure/Security/`.
