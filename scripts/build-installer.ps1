<#
.SYNOPSIS
    Builds the ServerCat MSI and bootstrapper EXE.
.DESCRIPTION
    1. Publishes the .NET backend (self-contained = false)
    2. Builds the React frontend
    3. Harvests file components with `wix harvest`
    4. Builds the MSI with WiX v4
    5. Builds the bootstrapper EXE (ServerCat-Setup.exe)
.NOTES
    Prerequisites:
      - .NET 8 SDK
      - Node.js 20
      - WiX Toolset v4: dotnet tool install -g wix
        wix extension add WixToolset.UI.wixext/4.0.5
        wix extension add WixToolset.Util.wixext/4.0.5
        wix extension add WixToolset.Firewall.wixext/4.0.5
        wix extension add WixToolset.Bal.wixext/4.0.5
        wix extension add WixToolset.Netfx.wixext/4.0.5
#>

param(
    [string]$Version    = "1.0.0",
    [string]$OutDir     = ".\dist",
    [switch]$SkipDotnet,
    [switch]$SkipFrontend,
    [switch]$SkipBundle
)

$ErrorActionPreference = "Stop"
$Root       = Split-Path $PSScriptRoot -Parent
$PublishDir = Join-Path $Root "publish"
$StaticDir  = Join-Path $Root "frontend-dist"
$InstallerDir = Join-Path $Root "installer"

function Write-Step { param([string]$msg) Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Assert-Command { param([string]$cmd) if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) { throw "Required command not found: $cmd" } }

# Verify tools
Assert-Command "dotnet"
Assert-Command "node"
Assert-Command "wix"

New-Item -ItemType Directory -Path $OutDir, $PublishDir, $StaticDir -Force | Out-Null

# ── 1. Publish .NET backend ───────────────────────────────────────
if (-not $SkipDotnet) {
    Write-Step "Publishing .NET backend (Release)"
    Remove-Item $PublishDir\* -Recurse -Force -ErrorAction SilentlyContinue
    dotnet publish "$Root\backend\ServerCat.Api\ServerCat.Api.csproj" `
        -c Release `
        --self-contained false `
        -o $PublishDir `
        /p:Version=$Version
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
    Write-Host "    Published to: $PublishDir"
}

# ── 2. Build React frontend ───────────────────────────────────────
if (-not $SkipFrontend) {
    Write-Step "Building React frontend"
    Push-Location "$Root\frontend"
    npm ci --prefer-offline
    npx vite build --outDir $StaticDir --emptyOutDir
    if ($LASTEXITCODE -ne 0) { Pop-Location; throw "npm build failed" }
    Pop-Location
    Write-Host "    Static files at: $StaticDir"
}

# ── 3. Harvest file components ────────────────────────────────────
Write-Step "Harvesting file components with wix harvest"

$HarvestBinaries = Join-Path $InstallerDir "GeneratedBinaries.wxs"
$HarvestStatic   = Join-Path $InstallerDir "GeneratedStatic.wxs"

# Harvest published backend binaries
wix harvest dir $PublishDir `
    -cg AppBinariesHarvested `
    -dr INSTALLDIR `
    -var "var.PublishDir" `
    -srd `
    -o $HarvestBinaries
if ($LASTEXITCODE -ne 0) { throw "wix harvest (binaries) failed" }

# Harvest frontend static files
wix harvest dir $StaticDir `
    -cg AppStaticHarvested `
    -dr WwwDir `
    -var "var.StaticDir" `
    -srd `
    -o $HarvestStatic
if ($LASTEXITCODE -ne 0) { throw "wix harvest (static) failed" }

Write-Host "    Harvested: $HarvestBinaries"
Write-Host "    Harvested: $HarvestStatic"

# ── 4. Build MSI ─────────────────────────────────────────────────
Write-Step "Building MSI"

$MsiOut = Join-Path $OutDir "ServerCat.msi"

wix build `
    "$InstallerDir\Package.wxs" `
    "$InstallerDir\UI\ConfigDlg.wxs" `
    $HarvestBinaries `
    $HarvestStatic `
    -ext WixToolset.UI.wixext `
    -ext WixToolset.Util.wixext `
    -ext WixToolset.Firewall.wixext `
    -d "PublishDir=$PublishDir\" `
    -d "StaticDir=$StaticDir\" `
    -d "Version=$Version" `
    -arch x64 `
    -o $MsiOut

if ($LASTEXITCODE -ne 0) { throw "MSI build failed" }
Write-Host "    MSI: $MsiOut ($('{0:N1}' -f ((Get-Item $MsiOut).Length / 1MB)) MB)"

# ── 5. Build bootstrapper EXE ─────────────────────────────────────
if (-not $SkipBundle) {
    Write-Step "Building bootstrapper (ServerCat-Setup.exe)"

    $ExeOut = Join-Path $OutDir "ServerCat-Setup.exe"

    wix build `
        "$InstallerDir\Bundle.wxs" `
        -ext WixToolset.Bal.wixext `
        -ext WixToolset.Netfx.wixext `
        -ext WixToolset.Util.wixext `
        -d "Version=$Version" `
        -o $ExeOut

    if ($LASTEXITCODE -ne 0) { throw "Bundle build failed" }
    Write-Host "    EXE: $ExeOut ($('{0:N1}' -f ((Get-Item $ExeOut).Length / 1MB)) MB)"
}

# ── Summary ───────────────────────────────────────────────────────
Write-Host @"

Build complete:
  MSI:  $MsiOut
  EXE:  $(Join-Path $OutDir 'ServerCat-Setup.exe')

Distribute ServerCat-Setup.exe to end-users.
Distribute ServerCat.msi for silent deployments:
  msiexec /i ServerCat.msi /qn DB_PASSWORD="Secret" JWT_SECRET="..." /l*v install.log
"@ -ForegroundColor Green
