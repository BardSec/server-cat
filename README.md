# ServerCat — Server Catalog & Polling

On-premises agentless server inventory and health monitoring for Windows-heavy environments.

## Quick Start

### Prerequisites
- Windows Server 2019+ (app server)
- .NET 8 Runtime
- PostgreSQL 16
- Node.js 20 LTS (build only)

### 1. Database setup
```powershell
# Create database and user
psql -U postgres -c "CREATE DATABASE servercat;"
psql -U postgres -c "CREATE ROLE servercat_app WITH LOGIN PASSWORD 'your-password';"
psql -U postgres -d servercat -f scripts/seed.sql
```

### 2. Build backend
```bash
cd backend
dotnet restore
dotnet build
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
# API: http://localhost:5000
# Swagger: http://localhost:5000/swagger
# Hangfire: http://localhost:5000/hangfire
```

### 5. Production install (Windows)
```powershell
# Run as Administrator
.\deploy\windows\Install-ServerCat.ps1 -DbPassword "your-db-password"
# Then deploy binaries and start the service
Start-Service ServerCat
```

## Project Structure

```
server-cat/
├── docs/           # PRD, threat model, architecture, DB schema, API design
├── backend/        # .NET 8 ASP.NET Core solution
│   ├── ServerCat.Core/          # Domain entities, interfaces, DTOs
│   ├── ServerCat.Infrastructure/ # EF Core, collectors, security, jobs
│   └── ServerCat.Api/           # ASP.NET Core Web API + Program.cs
├── frontend/       # React 18 + TypeScript + Tailwind CSS
├── deploy/         # Installation and configuration scripts
└── scripts/        # SQL schema, seed data, backup scripts
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
| 443  | TCP | Inbound  | HTTPS web UI + API |
| 5986 | TCP | Outbound | WinRM HTTPS to targets |
| 5985 | TCP | Outbound | WinRM HTTP to targets (fallback) |
| 161  | UDP | Outbound | SNMP to targets |
| 5432 | TCP | App→DB   | PostgreSQL |
