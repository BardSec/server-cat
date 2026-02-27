<#
.SYNOPSIS
    Installs ServerCat as a Windows Service.
.DESCRIPTION
    Full installation script for the ServerCat server catalog and polling application.
    Run as local Administrator on the target Windows Server.
.PARAMETER InstallDir
    Installation directory. Default: C:\ServerCat
.PARAMETER ServiceAccount
    Domain service account for the Windows Service. Default: .\LocalSystem (dev only)
.PARAMETER DbPassword
    PostgreSQL password for the servercat_app user.
.EXAMPLE
    .\Install-ServerCat.ps1 -ServiceAccount "CORP\svc-servercat-app" -DbPassword "SecurePass123!"
#>

[CmdletBinding()]
param(
    [string]$InstallDir      = "C:\ServerCat",
    [string]$ServiceName     = "ServerCat",
    [string]$ServiceAccount  = "LocalSystem",
    [string]$ServicePassword = "",
    [string]$DbHost          = "localhost",
    [string]$DbName          = "servercat",
    [string]$DbUser          = "servercat_app",
    [string]$DbPassword      = $(throw "-DbPassword is required"),
    [string]$JwtSecret       = [System.Guid]::NewGuid().ToString("N") + [System.Guid]::NewGuid().ToString("N"),
    [switch]$SkipPostgres,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

function Write-Step { param([string]$msg) Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Write-OK   { param([string]$msg) Write-Host "    OK: $msg" -ForegroundColor Green }
function Write-Warn { param([string]$msg) Write-Host "    WARN: $msg" -ForegroundColor Yellow }

# ── Require admin ─────────────────────────────────────────────────────────────
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "This script must be run as Administrator."
}

Write-Host @"
╔═══════════════════════════════════════════════════╗
║       ServerCat Installation Script v1.0          ║
║   Server Catalog & Polling — On-Premises          ║
╚═══════════════════════════════════════════════════╝
"@ -ForegroundColor Blue

# ── Step 1: Create directories ────────────────────────────────────────────────
Write-Step "Creating installation directories"

$VaultDir  = Join-Path $InstallDir "vault"
$LogDir    = Join-Path $InstallDir "logs"
$WwwDir    = Join-Path $InstallDir "wwwroot"

foreach ($dir in @($InstallDir, $VaultDir, $LogDir, $WwwDir)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}
Write-OK "Directories created at $InstallDir"

# ── Step 2: Set NTFS permissions ──────────────────────────────────────────────
Write-Step "Configuring NTFS permissions"

if ($ServiceAccount -ne "LocalSystem") {
    # Restrict install dir to service account + admins
    $acl = Get-Acl $InstallDir
    $acl.SetAccessRuleProtection($true, $false)

    $adminRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        "BUILTIN\Administrators", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
    $svcRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        $ServiceAccount, "ReadAndExecute", "ContainerInherit,ObjectInherit", "None", "Allow")

    $acl.AddAccessRule($adminRule)
    $acl.AddAccessRule($svcRule)
    Set-Acl $InstallDir $acl

    # Vault: service account gets Modify (needs to write vault.json)
    $vaultAcl = Get-Acl $VaultDir
    $vaultAcl.SetAccessRuleProtection($true, $false)
    $vaultSvcRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        $ServiceAccount, "Modify", "ContainerInherit,ObjectInherit", "None", "Allow")
    $vaultAcl.AddAccessRule($adminRule)
    $vaultAcl.AddAccessRule($vaultSvcRule)
    Set-Acl $VaultDir $vaultAcl

    Write-OK "NTFS permissions configured for $ServiceAccount"
} else {
    Write-Warn "Using LocalSystem — set proper service account before production deployment!"
}

# ── Step 3: Set environment variables ─────────────────────────────────────────
Write-Step "Setting machine-level environment variables"

$connStr = "Host=$DbHost;Database=$DbName;Username=$DbUser;Password=$DbPassword;SSL Mode=Prefer;"
[System.Environment]::SetEnvironmentVariable(
    "ConnectionStrings__DefaultConnection", $connStr, "Machine")
[System.Environment]::SetEnvironmentVariable(
    "Authentication__JwtSecret", $JwtSecret, "Machine")
[System.Environment]::SetEnvironmentVariable(
    "Vault__VaultFilePath", "$VaultDir\vault.json", "Machine")
[System.Environment]::SetEnvironmentVariable(
    "Vault__EntropyFilePath", "$VaultDir\vault.entropy", "Machine")

Write-OK "Environment variables configured (connection string, JWT secret, vault path)"
Write-Warn "JWT Secret: $JwtSecret — store this securely!"

# ── Step 4: Install Windows Service ──────────────────────────────────────────
Write-Step "Installing Windows Service"

$BinaryPath = Join-Path $InstallDir "ServerCat.Api.exe"
if (-not (Test-Path $BinaryPath)) {
    Write-Warn "Binary not found at $BinaryPath. Deploy the published output first."
    Write-Warn "Run on build machine: dotnet publish -c Release -o .\publish backend\ServerCat.sln"
    Write-Warn "Then copy .\publish\* to $InstallDir"
}

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    if ($Force) {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 2
        Write-OK "Existing service removed."
    } else {
        Write-Warn "Service '$ServiceName' already exists. Use -Force to reinstall."
    }
}

$scArgs = @(
    "create", $ServiceName,
    "binpath=", "`"$BinaryPath`"",
    "start=", "auto",
    "displayname=", "ServerCat - Server Catalog & Polling"
)

if ($ServiceAccount -ne "LocalSystem") {
    $scArgs += "obj=", $ServiceAccount
    if ($ServicePassword) { $scArgs += "password=", $ServicePassword }
}

sc.exe @scArgs | Out-Null
sc.exe description $ServiceName "Agentless Windows server inventory and health monitoring" | Out-Null
sc.exe failure $ServiceName reset=86400 actions=restart/60000/restart/120000/restart/300000 | Out-Null

Write-OK "Windows Service '$ServiceName' created"

# ── Step 5: Configure firewall ────────────────────────────────────────────────
Write-Step "Configuring Windows Firewall"

$fwRules = @(
    @{ Name="ServerCat HTTPS Inbound";       Dir="Inbound";  Port=443;  Proto="TCP"; Action="Allow" },
    @{ Name="ServerCat WinRM-HTTPS Outbound"; Dir="Outbound"; Port=5986; Proto="TCP"; Action="Allow" },
    @{ Name="ServerCat WinRM-HTTP Outbound";  Dir="Outbound"; Port=5985; Proto="TCP"; Action="Allow" },
    @{ Name="ServerCat SNMP Outbound";        Dir="Outbound"; Port=161;  Proto="UDP"; Action="Allow" }
)

foreach ($rule in $fwRules) {
    $existing = Get-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue
    if (-not $existing) {
        New-NetFirewallRule -DisplayName $rule.Name `
            -Direction $rule.Dir -Protocol $rule.Proto `
            -LocalPort $rule.Port -Action $rule.Action | Out-Null
        Write-OK "Firewall rule: $($rule.Name)"
    }
}

# ── Step 6: Schedule backup task ─────────────────────────────────────────────
Write-Step "Scheduling daily backup task"

$BackupScript = Join-Path $PSScriptRoot "..\..\..\scripts\Backup-ServerCat.ps1"
$BackupScript = [System.IO.Path]::GetFullPath($BackupScript)

if (Test-Path $BackupScript) {
    $action  = New-ScheduledTaskAction -Execute "powershell.exe" `
        -Argument "-NonInteractive -ExecutionPolicy Bypass -File `"$BackupScript`""
    $trigger = New-ScheduledTaskTrigger -Daily -At "02:00"
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable

    Register-ScheduledTask -TaskName "ServerCat Daily Backup" `
        -Action $action -Trigger $trigger -Settings $settings `
        -Description "Daily backup of ServerCat database and vault" `
        -RunLevel Highest -Force | Out-Null
    Write-OK "Backup task scheduled (daily at 02:00)"
} else {
    Write-Warn "Backup script not found at $BackupScript. Schedule manually."
}

# ── Summary ───────────────────────────────────────────────────────────────────
Write-Host @"

╔══════════════════════════════════════════════════════════════╗
║                  Installation Complete                        ║
╠══════════════════════════════════════════════════════════════╣
║  Install Dir:   $InstallDir
║  Service Name:  $ServiceName
║  Vault Dir:     $VaultDir
╠══════════════════════════════════════════════════════════════╣
║  NEXT STEPS:                                                  ║
║  1. Deploy published binaries to $InstallDir                  ║
║  2. Deploy frontend to $WwwDir                                ║
║  3. Run database migrations (app auto-runs on start)          ║
║  4. Run seed.sql if desired                                   ║
║  5. Start-Service $ServiceName                                ║
║  6. Configure reverse proxy (IIS ARR or Nginx)               ║
║  7. Verify: GET https://<host>/health                         ║
╠══════════════════════════════════════════════════════════════╣
║  SECURITY REMINDERS:                                          ║
║  - Change all default passwords before going live             ║
║  - Verify NTFS ACLs on vault directory                        ║
║  - Configure TLS certificate on reverse proxy                 ║
║  - Create CORP\svc-servercat-poll with minimal rights         ║
╚══════════════════════════════════════════════════════════════╝
"@ -ForegroundColor Green
