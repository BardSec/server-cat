<#
.SYNOPSIS
    Daily backup of ServerCat database and vault file.
.DESCRIPTION
    Backs up the PostgreSQL database using pg_dump and copies the vault file.
    Prunes backups older than RetainDays.
.NOTES
    Schedule via Task Scheduler or the Install-ServerCat.ps1 script.
    Requires SERVERCAT_DB_PASSWORD environment variable to be set.
#>

param(
    [string]$BackupDir    = "C:\Backups\ServerCat",
    [string]$DbHost       = "localhost",
    [string]$DbName       = "servercat",
    [string]$DbUser       = "servercat_app",
    [string]$VaultFile    = "C:\ServerCat\vault\vault.json",
    [string]$ConfigFile   = "C:\ServerCat\appsettings.Production.json",
    [string]$PgBinDir     = "C:\Program Files\PostgreSQL\16\bin",
    [int]$RetainDays      = 30
)

$ErrorActionPreference = "Stop"

$Date       = Get-Date -Format "yyyy-MM-dd_HH-mm"
$BackupPath = Join-Path $BackupDir $Date

New-Item -ItemType Directory -Path $BackupPath -Force | Out-Null

# Get DB password from environment variable (never hardcode)
$DbPassword = $env:SERVERCAT_DB_PASSWORD
if (-not $DbPassword) {
    # Try machine-level env var
    $DbPassword = [System.Environment]::GetEnvironmentVariable("SERVERCAT_DB_PASSWORD", "Machine")
}
if (-not $DbPassword) {
    throw "SERVERCAT_DB_PASSWORD environment variable not set. Cannot run backup."
}

Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Starting ServerCat backup to $BackupPath"

# ── Database dump ─────────────────────────────────────────────────────────────
$PgDump    = Join-Path $PgBinDir "pg_dump.exe"
$DumpFile  = Join-Path $BackupPath "servercat.dump"

if (-not (Test-Path $PgDump)) {
    throw "pg_dump not found at $PgDump. Check PgBinDir parameter."
}

$env:PGPASSWORD = $DbPassword
try {
    & $PgDump -h $DbHost -U $DbUser -d $DbName -Fc -f $DumpFile
    if ($LASTEXITCODE -ne 0) { throw "pg_dump exited with code $LASTEXITCODE" }
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Database dump: OK ($((Get-Item $DumpFile).Length / 1MB) MB)"
} finally {
    $env:PGPASSWORD = $null
    Remove-Variable DbPassword -Scope Local -ErrorAction SilentlyContinue
}

# ── Vault file backup ─────────────────────────────────────────────────────────
# The vault file is already DPAPI-encrypted, so copying it is safe.
# NOTE: The entropy file must also be backed up (separately, ideally offline).
if (Test-Path $VaultFile) {
    Copy-Item $VaultFile (Join-Path $BackupPath "vault.json")
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Vault file backup: OK"
} else {
    Write-Warning "Vault file not found at $VaultFile"
}

$EntropyFile = "C:\ServerCat\vault\vault.entropy"
if (Test-Path $EntropyFile) {
    Copy-Item $EntropyFile (Join-Path $BackupPath "vault.entropy")
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Vault entropy backup: OK"
}

# ── Config backup ─────────────────────────────────────────────────────────────
if (Test-Path $ConfigFile) {
    Copy-Item $ConfigFile (Join-Path $BackupPath "appsettings.Production.json")
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Config backup: OK"
}

# ── Prune old backups ─────────────────────────────────────────────────────────
$CutoffDate = (Get-Date).AddDays(-$RetainDays)
$Pruned = Get-ChildItem $BackupDir -Directory |
    Where-Object { $_.CreationTime -lt $CutoffDate }

foreach ($old in $Pruned) {
    Remove-Item $old.FullName -Recurse -Force
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Pruned old backup: $($old.Name)"
}

Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Backup complete: $BackupPath" -ForegroundColor Green
