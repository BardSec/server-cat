# API Design — Server Catalog & Polling

**Base URL:** `https://<host>/api`
**Auth:** Bearer JWT (or Windows Negotiate on IIS)
**Content-Type:** `application/json`
**Error format:** `{ "type": "...", "title": "...", "status": 400, "detail": "...", "errors": {} }`

---

## Servers

### GET /api/servers
List servers with optional filters.

**Query params:**
| Param | Type | Description |
|-------|------|-------------|
| `environment` | string | production, staging, development, dr, lab |
| `criticality` | string | critical, high, medium, low |
| `site` | string | site name filter (partial match) |
| `status` | string | green, yellow, red, gray |
| `search` | string | Full-text search on hostname/display_name |
| `tags` | string[] | Tag filter (AND logic) |
| `page` | int | Page number (default 1) |
| `pageSize` | int | Page size (default 50, max 500) |
| `sortBy` | string | Field to sort (hostname, criticality, lastSeen) |
| `sortDir` | string | asc / desc |

**Response 200:**
```json
{
  "items": [
    {
      "id": "550e8400-e29b-41d4-a716-446655440000",
      "hostname": "APP-PROD-01",
      "displayName": "Primary App Server",
      "ipAddress": "10.0.1.50",
      "environment": "production",
      "criticality": "critical",
      "site": "DC-East",
      "owner": "Platform Engineering",
      "osName": "Windows Server 2019 Standard",
      "tags": ["web", "iis"],
      "pollingEnabled": true,
      "pollingIntervalMinutes": 60,
      "status": "green",
      "lastPollAt": "2026-02-27T10:05:00Z",
      "lastPollStatus": "success",
      "isActive": true,
      "createdAt": "2025-01-15T09:00:00Z"
    }
  ],
  "totalCount": 142,
  "page": 1,
  "pageSize": 50,
  "totalPages": 3
}
```

---

### POST /api/servers
Add a new server.

**Request:**
```json
{
  "hostname": "APP-PROD-02",
  "ipAddress": "10.0.1.51",
  "displayName": "Secondary App Server",
  "environment": "production",
  "criticality": "high",
  "site": "DC-East",
  "owner": "Platform Engineering",
  "osType": "windows",
  "collectorType": "winrm",
  "credentialRefId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "pollingEnabled": true,
  "pollingIntervalMinutes": 60,
  "tags": ["web", "iis"]
}
```

**Response 201:**
```json
{
  "id": "660e9511-f3ac-52e5-b827-557766551111",
  "hostname": "APP-PROD-02",
  ...
}
```

**Errors:**
- `400` — Validation failure (missing required fields, interval out of range)
- `409` — Hostname already exists

---

### GET /api/servers/{id}
Get full server detail.

**Response 200:**
```json
{
  "id": "550e8400-...",
  "hostname": "APP-PROD-01",
  "displayName": "Primary App Server",
  "ipAddress": "10.0.1.50",
  "environment": "production",
  "criticality": "critical",
  "site": "DC-East",
  "owner": "Platform Engineering",
  "osType": "windows",
  "collectorType": "winrm",
  "credentialRef": {
    "id": "a1b2c3d4-...",
    "name": "CORP\\svc-poll",
    "authMethod": "winrm_kerberos"
  },
  "pollingEnabled": true,
  "pollingIntervalMinutes": 60,
  "tags": ["web", "iis"],
  "groups": [{ "id": "...", "name": "IIS Servers", "color": "#3B82F6" }],
  "status": "green",
  "lastPollAt": "2026-02-27T10:05:00Z",
  "lastPollStatus": "success",
  "isActive": true,
  "createdAt": "2025-01-15T09:00:00Z",
  "updatedAt": "2026-02-20T14:30:00Z",
  "createdBy": "admin@corp.local"
}
```

---

### PUT /api/servers/{id}
Update server metadata.

**Request:** Same shape as POST (all fields optional in PUT).

**Response 200:** Updated server object.

---

### DELETE /api/servers/{id}
Soft-delete (sets `is_active=false`). Use `?hard=true` (Admin only) for permanent delete.

**Response 204**

---

### POST /api/servers/{id}/connectivity-test
Test reachability before polling. Returns results within 30s.

**Request:**
```json
{
  "collectorType": "winrm",
  "credentialRefId": "a1b2c3d4-..."
}
```

**Response 200:**
```json
{
  "success": true,
  "collectorType": "winrm",
  "durationMs": 1240,
  "checks": [
    { "name": "DNS Resolution", "passed": true, "detail": "Resolved to 10.0.1.50" },
    { "name": "TCP Port 5986", "passed": true, "detail": "Connected in 45ms" },
    { "name": "WinRM Authentication", "passed": true, "detail": "Kerberos authentication succeeded" },
    { "name": "CIM Query", "passed": true, "detail": "Win32_OperatingSystem returned 1 record" }
  ],
  "error": null
}
```

---

## Polls

### GET /api/servers/{serverId}/polls
List poll history for a server.

**Query params:** `page`, `pageSize`, `status`, `from`, `to`

**Response 200:**
```json
{
  "items": [
    {
      "id": "poll-uuid-1",
      "serverId": "550e8400-...",
      "startedAt": "2026-02-27T10:05:00Z",
      "completedAt": "2026-02-27T10:05:08Z",
      "status": "success",
      "collectorType": "winrm",
      "triggeredBy": "scheduler",
      "durationMs": 8240
    }
  ],
  "totalCount": 312
}
```

---

### GET /api/servers/{serverId}/polls/latest
Get the most recent poll result (full snapshot).

**Response 200:**
```json
{
  "poll": {
    "id": "poll-uuid-1",
    "startedAt": "2026-02-27T10:05:00Z",
    "status": "success",
    "durationMs": 8240
  },
  "result": {
    "hostnameResolved": "APP-PROD-01.corp.local",
    "domain": "CORP",
    "osName": "Windows Server 2019 Standard",
    "osVersion": "10.0.17763",
    "osBuild": "17763.6532",
    "lastBootTime": "2026-02-20T06:00:00Z",
    "uptimeSeconds": 604800,
    "cpuInfo": {
      "model": "Intel(R) Xeon(R) Gold 6248R CPU @ 3.00GHz",
      "sockets": 2,
      "coresPerSocket": 24,
      "logicalProcessors": 96
    },
    "memoryTotalMb": 65536,
    "memoryAvailableMb": 42100,
    "diskVolumes": [
      { "drive": "C:", "label": "System", "filesystem": "NTFS", "sizeGb": 127.9, "freeGb": 45.2, "freePct": 35.3 },
      { "drive": "D:", "label": "Data", "filesystem": "NTFS", "sizeGb": 1023.8, "freeGb": 98.5, "freePct": 9.6 }
    ],
    "networkAdapters": [
      {
        "name": "Ethernet0",
        "ipAddress": "10.0.1.50",
        "subnetMask": "255.255.255.0",
        "macAddress": "00:11:22:33:44:55",
        "defaultGateway": "10.0.1.1",
        "dnsServers": ["10.0.0.10", "10.0.0.11"]
      }
    ],
    "services": [
      { "name": "W3SVC", "displayName": "World Wide Web Publishing Service", "status": "Running", "startType": "Automatic" },
      { "name": "Spooler", "displayName": "Print Spooler", "status": "Stopped", "startType": "Automatic" }
    ],
    "lastUpdateInstalled": "2026-02-15",
    "pendingReboot": false
  }
}
```

---

### GET /api/polls/{pollId}
Get a specific poll and its result.

**Response 200:** Same shape as latest poll response.

---

### POST /api/servers/{serverId}/poll
Trigger an immediate poll. Rate-limited to 1/server/60s.

**Response 202:**
```json
{
  "pollId": "new-poll-uuid",
  "message": "Poll queued. Check /api/polls/{pollId} for status.",
  "queuedAt": "2026-02-27T10:10:00Z"
}
```

**Errors:**
- `429` — Poll already triggered within the last 60 seconds
- `400` — Server polling disabled

---

## Notes

### GET /api/servers/{serverId}/notes
**Query params:** `page`, `pageSize`

**Response 200:**
```json
{
  "items": [
    {
      "id": "note-uuid-1",
      "serverId": "550e8400-...",
      "content": "## Maintenance Window\nScheduled for 2026-03-01 02:00 UTC. IIS restart expected.",
      "isPinned": true,
      "createdAt": "2026-02-25T14:00:00Z",
      "createdBy": "jsmith@corp.local",
      "updatedAt": null,
      "updatedBy": null
    }
  ],
  "totalCount": 5
}
```

---

### POST /api/servers/{serverId}/notes
**Request:**
```json
{
  "content": "Disk D: is being expanded. Ticket #INC00234.",
  "isPinned": false
}
```

**Response 201:** Created note object.

---

### PUT /api/notes/{noteId}
Update content or pinned status.

**Request:**
```json
{ "content": "Updated text", "isPinned": true }
```

**Response 200:** Updated note object.

---

### DELETE /api/notes/{noteId}
**Response 204**

---

## Operational Logs

### GET /api/servers/{serverId}/logs
Server-scoped audit trail.

**Query params:** `page`, `pageSize`, `eventType`, `severity`, `from`, `to`

### GET /api/operational-logs
System-wide audit trail (Admin only).

**Response 200:**
```json
{
  "items": [
    {
      "id": "log-uuid",
      "serverId": "550e8400-...",
      "eventType": "poll.failed",
      "severity": "error",
      "summary": "Poll failed for APP-PROD-01: WinRM connection refused on port 5986",
      "details": { "error": "Connection refused", "port": 5986 },
      "actor": "system",
      "createdAt": "2026-02-27T09:00:00Z"
    }
  ]
}
```

---

## Credentials

### GET /api/credentials
List credential references (no secret values returned).

**Response 200:**
```json
{
  "items": [
    {
      "id": "a1b2c3d4-...",
      "name": "CORP Polling Account",
      "description": "Domain service account for WinRM polling",
      "authMethod": "winrm_kerberos",
      "usernameHint": "CORP\\svc-servercat-poll",
      "createdAt": "2025-01-10T08:00:00Z",
      "createdBy": "admin@corp.local"
    }
  ]
}
```

### POST /api/credentials
Create a new credential reference. Secret is written to vault, only key stored in DB.

**Request:**
```json
{
  "name": "CORP Polling Account",
  "description": "Domain service account for WinRM polling",
  "authMethod": "winrm_kerberos",
  "usernameHint": "CORP\\svc-servercat-poll",
  "secretValue": "P@ssw0rd123!"
}
```

**IMPORTANT:** `secretValue` is accepted over HTTPS, immediately encrypted by DPAPI, and never stored or logged. The response never includes `secretValue`.

**Response 201:** Credential reference (no secret).

---

## Groups

### GET /api/groups
### POST /api/groups  `{ "name": "...", "description": "...", "color": "#3B82F6" }`
### POST /api/groups/{groupId}/servers  `{ "serverIds": ["uuid1", "uuid2"] }`
### DELETE /api/groups/{groupId}/servers/{serverId}

---

## Dashboard

### GET /api/dashboard/summary
**Response 200:**
```json
{
  "totalServers": 142,
  "activeServers": 138,
  "byStatus": { "green": 110, "yellow": 18, "red": 8, "gray": 6 },
  "byCriticality": { "critical": 25, "high": 48, "medium": 55, "low": 14 },
  "byEnvironment": { "production": 80, "staging": 30, "development": 20, "dr": 8, "lab": 4 },
  "recentFailures": [
    { "serverId": "...", "hostname": "APP-PROD-07", "failedAt": "2026-02-27T08:30:00Z", "error": "WinRM timeout" }
  ],
  "pollsLast24h": 3240,
  "failedPollsLast24h": 12
}
```

---

## Error Responses

All errors follow RFC 7807 Problem Details:

```json
{
  "type": "https://servercat.local/errors/validation",
  "title": "Validation Error",
  "status": 400,
  "detail": "One or more fields failed validation",
  "errors": {
    "hostname": ["Hostname is required"],
    "pollingIntervalMinutes": ["Must be between 5 and 10080"]
  },
  "traceId": "00-abc123-def456-01"
}
```

| Status | Meaning |
|--------|---------|
| 400 | Validation error or bad request |
| 401 | Not authenticated |
| 403 | Authenticated but insufficient role |
| 404 | Resource not found |
| 409 | Conflict (e.g., duplicate hostname) |
| 429 | Rate limited |
| 500 | Unexpected server error (trace ID in response) |
