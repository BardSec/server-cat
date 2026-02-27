# Deployment Guide — On-Premises (Windows Server)

## Prerequisites

| Component | Version | Notes |
|-----------|---------|-------|
| Windows Server | 2019+ (2022 recommended) | App server host |
| .NET 8 Runtime | 8.0.x | ASP.NET Core + Windows Service |
| PostgreSQL | 16.x | Can be same host or separate server |
| Node.js | 20 LTS | Build-time only (CI/CD) |
| IIS (optional) | 10+ | If using IIS as reverse proxy |
| Nginx for Windows (optional) | 1.25+ | Alternative to IIS ARR |

## Service Accounts Required

```
CORP\svc-servercat-app     - Runs the API/Worker Windows Service
                             Rights: Log on as a service, local admin NOT required
                             DB access: servercat_app PostgreSQL role

CORP\svc-servercat-poll    - Used for WinRM/WMI polling of target servers
                             Rights on targets: Remote Management Users group
                             NOT a local administrator
```

**Assumption:** Active Directory domain exists. For workgroup environments, use local accounts with matching credentials on each target, or SNMPv3.

---

## Step 1: Install PostgreSQL

```powershell
# Download PostgreSQL 16 installer from postgresql.org (offline installer)
# Install silently
.\postgresql-16.x-windows-x64.exe --mode unattended --superpassword "ChangeMe123!" --servicename "postgresql-16" --serviceaccount "NT AUTHORITY\NetworkService"

# After install, create the database and user
& "C:\Program Files\PostgreSQL\16\bin\psql.exe" -U postgres -c "CREATE DATABASE servercat;"
& "C:\Program Files\PostgreSQL\16\bin\psql.exe" -U postgres -c "CREATE ROLE servercat_app WITH LOGIN PASSWORD 'AppPassword!456';"
& "C:\Program Files\PostgreSQL\16\bin\psql.exe" -U postgres -c "GRANT CONNECT ON DATABASE servercat TO servercat_app;"
& "C:\Program Files\PostgreSQL\16\bin\psql.exe" -U postgres -d servercat -c "GRANT USAGE ON SCHEMA public TO servercat_app;"
& "C:\Program Files\PostgreSQL\16\bin\psql.exe" -U postgres -d servercat -f "C:\servercat\scripts\schema.sql"
& "C:\Program Files\PostgreSQL\16\bin\psql.exe" -U postgres -d servercat -f "C:\servercat\scripts\seed.sql"
```

**PostgreSQL security hardening:**
```
# pg_hba.conf — allow only localhost and app server IP
host  servercat  servercat_app  127.0.0.1/32  scram-sha-256
host  servercat  servercat_app  10.0.1.0/24   scram-sha-256  # app server subnet
```

---

## Step 2: Install .NET 8 Runtime

```powershell
# Download the ASP.NET Core Runtime (not SDK) for production
# https://dotnet.microsoft.com/download/dotnet/8.0
.\dotnet-hosting-8.x.x-win.exe /quiet
```

---

## Step 3: Deploy Application Files

```powershell
$InstallDir = "C:\ServerCat"
New-Item -ItemType Directory -Path $InstallDir -Force

# Copy published output (from CI/CD or manual publish)
# dotnet publish -c Release -o .\publish (run on build machine)
Copy-Item .\publish\* $InstallDir -Recurse

# Set NTFS permissions: only svc-servercat-app can read/execute
icacls $InstallDir /inheritance:r
icacls $InstallDir /grant "CORP\svc-servercat-app:(OI)(CI)RX"
icacls $InstallDir /grant "BUILTIN\Administrators:(OI)(CI)F"

# Vault file directory — service account only
$VaultDir = "C:\ServerCat\vault"
New-Item -ItemType Directory -Path $VaultDir -Force
icacls $VaultDir /inheritance:r
icacls $VaultDir /grant "CORP\svc-servercat-app:(OI)(CI)M"
icacls $VaultDir /grant "BUILTIN\Administrators:(OI)(CI)F"
```

---

## Step 4: Configure appsettings.Production.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=servercat;Username=servercat_app;Password=AppPassword!456;SSL Mode=Prefer;"
  },
  "ServerCat": {
    "VaultFilePath": "C:\\ServerCat\\vault\\vault.json",
    "BaseUrl": "https://servercat.corp.local",
    "MaxConcurrentPolls": 10,
    "DefaultPollingIntervalMinutes": 60,
    "RetentionDays": 365
  },
  "Hangfire": {
    "DashboardEnabled": true,
    "MaxConcurrentJobs": 10
  },
  "Authentication": {
    "Mode": "Windows",
    "JwtSecret": "",
    "JwtIssuer": "https://servercat.corp.local"
  },
  "Serilog": {
    "MinimumLevel": "Information",
    "WriteTo": [
      { "Name": "File", "Args": { "path": "C:\\ServerCat\\logs\\servercat-.log", "rollingInterval": "Day", "retainedFileCountLimit": 30 } },
      { "Name": "EventLog", "Args": { "source": "ServerCat", "logName": "Application" } }
    ]
  }
}
```

**IMPORTANT:** Store the DB password and JWT secret using environment variables or DPAPI-encrypted config in production, not plaintext in the JSON file.

```powershell
# Set as environment variables on the machine (preferred)
[System.Environment]::SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "Host=localhost;...", "Machine")
[System.Environment]::SetEnvironmentVariable("ServerCat__JwtSecret", "your-256-bit-secret", "Machine")
```

---

## Step 5: Install as Windows Service

```powershell
# Create Windows Service using sc.exe
sc.exe create "ServerCat" `
    binpath="C:\ServerCat\ServerCat.Api.exe" `
    start=auto `
    obj="CORP\svc-servercat-app" `
    password="ServiceAccountPassword!" `
    displayname="ServerCat - Server Catalog & Polling"

sc.exe description "ServerCat" "Agentless server inventory and polling service"

# Set recovery actions: restart on failure
sc.exe failure "ServerCat" reset=86400 actions=restart/60000/restart/60000/restart/60000
```

Or using PowerShell:
```powershell
New-Service -Name "ServerCat" `
    -BinaryPathName "C:\ServerCat\ServerCat.Api.exe" `
    -DisplayName "ServerCat - Server Catalog & Polling" `
    -StartupType Automatic `
    -Credential (Get-Credential CORP\svc-servercat-app)

Start-Service "ServerCat"
```

---

## Step 6: Configure TLS / Reverse Proxy

### Option A: IIS with Application Request Routing (ARR)

```xml
<!-- C:\inetpub\wwwroot\servercat\web.config -->
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>
    <rewrite>
      <rules>
        <rule name="ReverseProxyToServerCat" stopProcessing="true">
          <match url="(.*)" />
          <action type="Rewrite" url="http://localhost:5000/{R:1}" />
        </rule>
      </rules>
    </rewrite>
    <security>
      <authentication>
        <windowsAuthentication enabled="true" />
        <anonymousAuthentication enabled="false" />
      </authentication>
    </security>
  </system.webServer>
</configuration>
```

Bind HTTPS cert to port 443 in IIS Manager using your internal CA certificate.

### Option B: Nginx for Windows

```nginx
# deploy/nginx/servercat.conf
server {
    listen 443 ssl http2;
    server_name servercat.corp.local;

    ssl_certificate      C:/certs/servercat.crt;
    ssl_certificate_key  C:/certs/servercat.key;
    ssl_protocols        TLSv1.2 TLSv1.3;
    ssl_ciphers          HIGH:!aNULL:!MD5;

    # Security headers
    add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;
    add_header X-Frame-Options DENY always;
    add_header X-Content-Type-Options nosniff always;
    add_header Content-Security-Policy "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline';" always;

    # Static SPA files
    location / {
        root   C:/ServerCat/wwwroot;
        try_files $uri $uri/ /index.html;
    }

    # API proxy
    location /api {
        proxy_pass         http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header   Upgrade $http_upgrade;
        proxy_set_header   Connection keep-alive;
        proxy_set_header   Host $host;
        proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
        proxy_cache_bypass $http_upgrade;
    }

    # Hangfire dashboard (admin only — also enforced at app level)
    location /hangfire {
        proxy_pass http://127.0.0.1:5000;
        allow 10.0.0.0/8;     # internal networks only
        deny all;
    }
}

server {
    listen 80;
    server_name servercat.corp.local;
    return 301 https://$server_name$request_uri;
}
```

---

## Step 7: Firewall Rules

```powershell
# Inbound: allow HTTPS to app server
New-NetFirewallRule -DisplayName "ServerCat HTTPS" -Direction Inbound -Protocol TCP -LocalPort 443 -Action Allow

# Outbound: allow WinRM HTTPS to target servers
New-NetFirewallRule -DisplayName "ServerCat WinRM-HTTPS Outbound" -Direction Outbound -Protocol TCP -RemotePort 5986 -Action Allow

# Outbound: allow WinRM HTTP (Kerberos only, fallback)
New-NetFirewallRule -DisplayName "ServerCat WinRM-HTTP Outbound" -Direction Outbound -Protocol TCP -RemotePort 5985 -Action Allow

# Outbound: allow SNMP
New-NetFirewallRule -DisplayName "ServerCat SNMP Outbound" -Direction Outbound -Protocol UDP -RemotePort 161 -Action Allow

# Outbound: PostgreSQL (if remote)
New-NetFirewallRule -DisplayName "ServerCat PostgreSQL" -Direction Outbound -Protocol TCP -RemotePort 5432 -Action Allow
```

---

## Step 8: Enable WinRM on Target Servers

Run this on each target Windows Server (via GPO or manually):

```powershell
# Enable WinRM
Enable-PSRemoting -Force

# Use HTTPS WinRM (recommended)
# Create a self-signed cert or use your internal CA
$cert = New-SelfSignedCertificate -DnsName $env:COMPUTERNAME -CertStoreLocation Cert:\LocalMachine\My
New-Item -Path WSMan:\Localhost\Listener -Transport HTTPS -Address * -CertificateThumbPrint $cert.Thumbprint -Force

# Add the polling service account to Remote Management Users
Add-LocalGroupMember -Group "Remote Management Users" -Member "CORP\svc-servercat-poll"

# Restrict WinRM access to app server IP only
New-NetFirewallRule -DisplayName "WinRM HTTPS from ServerCat" -Direction Inbound -Protocol TCP -LocalPort 5986 -RemoteAddress "10.0.1.100" -Action Allow
```

**JEA (Just Enough Administration) — Recommended:**
```powershell
# On each target: create a constrained JEA endpoint
# Only allow specific Get-* cmdlets needed by the collector
$sessionConfig = @{
    Name = 'ServerCatPolling'
    RunAsVirtualAccount = $false
    RunAsCredential = [PSCredential]::new('CORP\svc-servercat-poll', (ConvertTo-SecureString 'password' -AsPlainText -Force))
    VisibleCmdlets = @(
        'Get-CimInstance', 'Get-Service', 'Get-Disk', 'Get-Partition',
        'Get-Volume', 'Get-NetAdapter', 'Get-NetIPAddress', 'Get-NetRoute',
        'Get-DnsClientServerAddress', 'Get-HotFix', 'Get-Date', 'Get-ComputerInfo'
    )
    VisibleFunctions = @()
    VisibleExternalCommands = @()
}
Register-PSSessionConfiguration @sessionConfig
```

---

## Backup and Restore

### Backup

```powershell
# scripts/Backup-ServerCat.ps1
# Schedule via Task Scheduler — runs daily at 02:00

param(
    [string]$BackupDir = "C:\Backups\ServerCat",
    [int]$RetainDays = 30
)

$Date = Get-Date -Format "yyyy-MM-dd_HH-mm"
$BackupPath = Join-Path $BackupDir $Date

New-Item -ItemType Directory -Path $BackupPath -Force | Out-Null

# Database backup
$PgDump = "C:\Program Files\PostgreSQL\16\bin\pg_dump.exe"
$Env:PGPASSWORD = $Env:SERVERCAT_DB_PASSWORD  # set in environment
& $PgDump -h localhost -U servercat_app -d servercat -Fc -f "$BackupPath\servercat.dump"

# Vault file backup (already encrypted by DPAPI)
Copy-Item "C:\ServerCat\vault\vault.json" "$BackupPath\vault.json"

# Application config backup
Copy-Item "C:\ServerCat\appsettings.Production.json" "$BackupPath\appsettings.Production.json"

# Prune old backups
Get-ChildItem $BackupDir | Where-Object { $_.CreationTime -lt (Get-Date).AddDays(-$RetainDays) } | Remove-Item -Recurse -Force

Write-Host "Backup completed: $BackupPath"
```

### Restore

```powershell
# Restore database from dump
$PgRestore = "C:\Program Files\PostgreSQL\16\bin\pg_restore.exe"
$Env:PGPASSWORD = $Env:SERVERCAT_DB_PASSWORD
& $PgRestore -h localhost -U postgres -d servercat --clean --if-exists "$BackupPath\servercat.dump"

# Restore vault
Copy-Item "$BackupPath\vault.json" "C:\ServerCat\vault\vault.json" -Force

# Restart service
Restart-Service "ServerCat"
```

**WARNING:** The vault file is encrypted with DPAPI tied to the service account and machine key. If restoring to a different machine, you must re-encrypt all vault entries by running the admin script:
```
dotnet run --project ServerCat.Api -- --migrate-vault
```

---

## Health Check

```
GET https://servercat.corp.local/health
→ 200 OK { "status": "Healthy", "checks": { "database": "Healthy", "hangfire": "Healthy" } }
```

Monitor this endpoint with your existing monitoring tool (Zabbix, SCOM, SolarWinds, etc.).
