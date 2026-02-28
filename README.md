# ServerCat — Server Catalog & Polling

On-premises agentless server inventory and health monitoring for Windows-heavy environments.

---

## Deployment Options

Choose the method that fits your environment:

| Method | Best for | Prerequisites |
|--------|----------|---------------|
| **Docker Compose** | Quick eval, Linux/Mac hosts, dev | Docker 24+ |
| **MSI Installer** | Production Windows Server, no Docker | Windows Server 2019+, .NET 8 |
| **Manual** | Custom environments | .NET 8 SDK, Node.js 20, PostgreSQL 16 |

---

## Option 1 — Docker Compose (fastest start)

```bash
# 1. Clone and configure
cp .env.example .env
# Edit .env: set DB_PASSWORD and JWT_SECRET (required)

# 2. Start everything (PostgreSQL + App)
docker compose up -d

# 3. Open the UI
open http://localhost:8080
```

**First run** applies EF Core migrations automatically. Load sample data:
```bash
docker compose exec db psql -U servercat_app -d servercat -f /scripts/seed.sql
```

### Production with TLS (Nginx)

```bash
# Add your TLS cert
cp your.crt docker/nginx/certs/servercat.crt
cp your.key docker/nginx/certs/servercat.key

# Start with Nginx overlay
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d

# https://your-server  →  Web UI + API
```

Self-signed cert for quick eval:
```bash
openssl req -x509 -newkey rsa:4096 -keyout docker/nginx/certs/servercat.key \
  -out docker/nginx/certs/servercat.crt -days 365 -nodes -subj "/CN=servercat.local"
```

---

## Option 2 — MSI Installer (native Windows)

> **Result:** `ServerCat-Setup.exe` — a single double-clickable installer that bundles .NET 8 Runtime (downloads if missing) and presents a GUI wizard for database and service account configuration.

### Download and run (end-user)

```
ServerCat-Setup.exe
```

The wizard prompts for:
- Database host, name, user, password
- Windows service account (default: LocalSystem)
- Install directory

### Silent / enterprise deployment

```powershell
# Deploy via GPO, SCCM, Intune, etc.
ServerCat-Setup.exe /quiet /norestart DB_PASSWORD="YourPass" JWT_SECRET="..."

# Or the MSI alone (if prereqs already met):
msiexec /i ServerCat.msi /qn /l*v install.log ^
  DB_PASSWORD="YourPass" DB_HOST="sql01" JWT_SECRET="..." SERVICE_ACCOUNT="CORP\svc-servercat"
```

### Build the installer (developers)

Prerequisites: .NET 8 SDK, Node.js 20, [WiX Toolset v4](https://wixtoolset.org/)

```powershell
# Install WiX and required extensions
dotnet tool install -g wix
wix extension add WixToolset.UI.wixext/4.0.5
wix extension add WixToolset.Util.wixext/4.0.5
wix extension add WixToolset.Firewall.wixext/4.0.5
wix extension add WixToolset.Bal.wixext/4.0.5
wix extension add WixToolset.Netfx.wixext/4.0.5

# Build MSI + bootstrapper EXE
.\scripts\build-installer.ps1 -Version 1.0.0

# Output:
#   dist\ServerCat.msi          (MSI for enterprise deployment)
#   dist\ServerCat-Setup.exe    (bootstrapper for end-users)
```

---

## Option 3 — Manual build and run

### Prerequisites
- Windows Server 2019+ (app server)
- .NET 8 Runtime
- PostgreSQL 16
- Node.js 20 LTS (build only)

### 1. Database setup
```powershell
psql -U postgres -c "CREATE DATABASE servercat;"
psql -U postgres -c "CREATE ROLE servercat_app WITH LOGIN PASSWORD 'your-password';"
psql -U postgres -d servercat -f scripts/seed.sql
```

### 2. Build backend
```bash
cd backend
dotnet restore && dotnet build
dotnet ef database update --project ServerCat.Infrastructure --startup-project ServerCat.Api
```

### 3. Build frontend
```bash
cd frontend
npm install
npm run build   # outputs to backend/ServerCat.Api/wwwroot
```

### 4. Run (development)
```bash
cd backend/ServerCat.Api
dotnet run
# UI + API: http://localhost:5000
# Swagger:  http://localhost:5000/swagger
# Hangfire: http://localhost:5000/hangfire
```

### 5. Production Windows Service
```powershell
# Run as Administrator
.\deploy\windows\Install-ServerCat.ps1 -DbPassword "your-db-password"
Start-Service ServerCat
```

---

## Project Structure

```
server-cat/
├── Dockerfile                   # Multi-stage Docker build
├── docker-compose.yml           # Dev: PostgreSQL + App
├── docker-compose.prod.yml      # Prod overlay: adds Nginx TLS
├── .env.example                 # Configuration template
├── docker/
│   ├── nginx/                   # Nginx config + TLS cert placeholder
│   └── postgres/                # DB init SQL
├── installer/
│   ├── Package.wxs              # WiX v4 MSI package definition
│   ├── Bundle.wxs               # WiX bootstrapper (prereq + MSI)
│   └── UI/ConfigDlg.wxs        # Custom database config wizard dialog
├── docs/                        # PRD, threat model, architecture, API
├── backend/                     # .NET 8 ASP.NET Core solution
│   ├── ServerCat.Core/          # Domain entities, interfaces, DTOs
│   ├── ServerCat.Infrastructure/ # EF Core, collectors, security, jobs
│   └── ServerCat.Api/           # Controllers, Program.cs
├── frontend/                    # React 18 + TypeScript + Tailwind CSS
├── deploy/windows/              # PowerShell install + WinRM config scripts
└── scripts/
    ├── seed.sql                 # Sample data
    ├── build-installer.ps1      # Builds MSI + EXE
    └── Backup-ServerCat.ps1    # DB + vault backup
```

## Key Documents
- [PRD & User Stories](docs/PRD.md)
- [Threat Model](docs/THREAT_MODEL.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Database Schema](docs/DB_SCHEMA.md)
- [API Design](docs/API_DESIGN.md)
- [Deployment Guide](docs/DEPLOYMENT.md)
- [Roadmap](docs/ROADMAP.md)

## Technology Stack
- **Backend:** .NET 8 (ASP.NET Core) + Entity Framework Core + Npgsql
- **Database:** PostgreSQL 16 with JSONB for flexible inventory data
- **Background Jobs:** Hangfire with PostgreSQL storage
- **Credentials:** Windows DPAPI + local vault file (swappable via ICredentialStore)
- **Collectors:** WinRM/PowerShell (primary), WMI/CIM (stub), SNMP (stub)
- **Frontend:** React 18 + TypeScript + Vite + TanStack Query + Tailwind CSS

## Security Notes
- Credentials are **never** stored in plaintext — DPAPI encrypted in vault.json
- Polling service account requires only `Remote Management Users` group membership (not admin)
- JEA (Just Enough Administration) endpoint restricts allowed PowerShell cmdlets on targets
- All mutations are recorded in the append-only `operational_logs` table
- See [THREAT_MODEL.md](docs/THREAT_MODEL.md) for full security analysis

## Required Firewall Ports
| Port | Protocol | Direction | Purpose |
|------|----------|-----------|---------|
| 80/443 | TCP | Inbound | HTTP/HTTPS web UI + API |
| 5986 | TCP | Outbound | WinRM HTTPS to targets |
| 5985 | TCP | Outbound | WinRM HTTP to targets (fallback) |
| 161  | UDP | Outbound | SNMP to targets |
| 5432 | TCP | App→DB   | PostgreSQL |
