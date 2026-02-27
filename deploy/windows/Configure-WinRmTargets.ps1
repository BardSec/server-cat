<#
.SYNOPSIS
    Configures WinRM on a target Windows Server to accept connections from the ServerCat polling account.
.DESCRIPTION
    Run this script ON EACH TARGET SERVER (via GPO, PsExec, or manual execution).
    Creates a JEA constrained endpoint that limits the polling account to read-only Get-* cmdlets only.
.PARAMETER PollAccountName
    The domain polling account. Default: CORP\svc-servercat-poll
.PARAMETER AppServerIp
    IP address of the ServerCat app server (restricts WinRM firewall rule to this source).
#>

param(
    [string]$PollAccountName = "CORP\svc-servercat-poll",
    [string]$AppServerIp     = $(throw "-AppServerIp is required for firewall rule scoping")
)

$ErrorActionPreference = "Stop"

Write-Host "Configuring WinRM target for ServerCat polling..." -ForegroundColor Cyan
Write-Host "Poll account: $PollAccountName"
Write-Host "App server IP: $AppServerIp"

# ── 1. Enable WinRM ───────────────────────────────────────────────────────────
Write-Host "`n[1/5] Enabling WinRM..."
Enable-PSRemoting -Force -SkipNetworkProfileCheck | Out-Null
Write-Host "      WinRM enabled."

# ── 2. Create HTTPS WinRM listener ───────────────────────────────────────────
Write-Host "`n[2/5] Configuring HTTPS WinRM listener..."

# Check if HTTPS listener already exists
$httpsListener = Get-WSManInstance -ResourceURI winrm/config/Listener -SelectorSet @{Transport='HTTPS'} -ErrorAction SilentlyContinue
if (-not $httpsListener) {
    # Create a self-signed certificate for WinRM HTTPS
    # In production, use a cert signed by your internal CA
    $cert = New-SelfSignedCertificate `
        -DnsName $env:COMPUTERNAME, "$env:COMPUTERNAME.$env:USERDNSDOMAIN" `
        -CertStoreLocation Cert:\LocalMachine\My `
        -NotAfter (Get-Date).AddYears(2)

    New-Item -Path WSMan:\Localhost\Listener `
        -Transport HTTPS `
        -Address * `
        -CertificateThumbPrint $cert.Thumbprint `
        -Force | Out-Null

    Write-Host "      HTTPS listener created (cert thumbprint: $($cert.Thumbprint))"
    Write-Host "      WARN: Self-signed cert used. Replace with internal CA cert for production." -ForegroundColor Yellow
} else {
    Write-Host "      HTTPS listener already configured."
}

# ── 3. Add poll account to Remote Management Users ────────────────────────────
Write-Host "`n[3/5] Adding $PollAccountName to Remote Management Users..."
try {
    Add-LocalGroupMember -Group "Remote Management Users" -Member $PollAccountName -ErrorAction SilentlyContinue
    Write-Host "      Added to Remote Management Users."
} catch {
    Write-Host "      Already a member or error: $($_.Exception.Message)" -ForegroundColor Yellow
}

# ── 4. Create JEA constrained endpoint ───────────────────────────────────────
Write-Host "`n[4/5] Creating JEA constrained endpoint 'ServerCatPolling'..."

$jeaEndpointName = "ServerCatPolling"
$allowedCmdlets = @(
    # OS and hardware
    'Get-CimInstance',
    'Get-ComputerInfo',
    # Disk
    'Get-Disk', 'Get-Partition', 'Get-Volume',
    # Network
    'Get-NetAdapter', 'Get-NetIPAddress', 'Get-NetRoute',
    'Get-DnsClientServerAddress',
    # Services
    'Get-Service',
    # Patches
    'Get-HotFix',
    # Misc utilities
    'Get-Date', 'Get-Item',
    # Read-only path access for reboot check
    'Test-Path'
)

$allowedFunctions = @('Write-Output')
$allowedVariables = @('PSVersionTable', 'env:COMPUTERNAME')

# Create role capability file
$RoleCapDir  = "C:\Program Files\WindowsPowerShell\Modules\ServerCatJEA\RoleCapabilities"
New-Item -ItemType Directory -Path $RoleCapDir -Force | Out-Null

$roleCapFile = Join-Path $RoleCapDir "ServerCatPolling.psrc"
New-PSRoleCapabilityFile `
    -Path $roleCapFile `
    -VisibleCmdlets $allowedCmdlets `
    -VisibleFunctions $allowedFunctions `
    -VisibleVariables $allowedVariables `
    -Description "ServerCat read-only polling endpoint" | Out-Null

# Create session configuration file
$sessionConfigDir  = "C:\JEA"
New-Item -ItemType Directory -Path $sessionConfigDir -Force | Out-Null

$sessionConfigFile = Join-Path $sessionConfigDir "ServerCatPolling.pssc"

# Map the poll account to the role capability
$roleDef = @{
    $PollAccountName = @{ RoleCapabilities = 'ServerCatPolling' }
}

New-PSSessionConfigurationFile `
    -Path $sessionConfigFile `
    -SessionType RestrictedRemoteServer `
    -RunAsVirtualAccount `
    -RoleDefinitions $roleDef `
    -Description "ServerCat constrained polling endpoint" | Out-Null

# Register the endpoint
$existingEndpoint = Get-PSSessionConfiguration -Name $jeaEndpointName -ErrorAction SilentlyContinue
if ($existingEndpoint) {
    Unregister-PSSessionConfiguration -Name $jeaEndpointName -Force | Out-Null
}

Register-PSSessionConfiguration `
    -Name $jeaEndpointName `
    -Path $sessionConfigFile `
    -Force | Out-Null

Write-Host "      JEA endpoint '$jeaEndpointName' registered."
Write-Host "      Allowed cmdlets: $($allowedCmdlets -join ', ')"

# ── 5. Restrict WinRM firewall to app server IP ───────────────────────────────
Write-Host "`n[5/5] Configuring firewall to allow WinRM only from $AppServerIp..."

$ruleName = "WinRM HTTPS from ServerCat AppServer"
Remove-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue | Out-Null
New-NetFirewallRule `
    -DisplayName $ruleName `
    -Direction Inbound `
    -Protocol TCP `
    -LocalPort 5986 `
    -RemoteAddress $AppServerIp `
    -Action Allow | Out-Null

Write-Host "      Firewall rule created: port 5986 allowed from $AppServerIp only."

Write-Host @"

Configuration complete on $env:COMPUTERNAME.

To verify from the ServerCat app server, run:
    Test-WSMan -ComputerName $env:COMPUTERNAME -UseSSL -Authentication Kerberos

To connect with JEA endpoint:
    Enter-PSSession -ComputerName $env:COMPUTERNAME -UseSSL -ConfigurationName ServerCatPolling
"@ -ForegroundColor Green
