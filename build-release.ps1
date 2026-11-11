#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Bouwt RadioBridge voor Windows x64 en Linux x64, zipped de output,
    en uploadt beide naar de GitHub release v0.9.

.GEBRUIK
    .\build-release.ps1

    Vereisten:
      - .NET 10 SDK  (dotnet in PATH)
      - gh CLI       (winget install GitHub.cli  of  https://cli.github.com)
      - Ingelogd bij gh  (gh auth login)
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$version    = "v0.9"
$repoRoot   = $PSScriptRoot
$distDir    = Join-Path $repoRoot "dist"
$projectDir = Join-Path $repoRoot "RadioBridge"

Write-Host ""
Write-Host "=== RadioBridge build-release.ps1 ===" -ForegroundColor Cyan
Write-Host "Versie  : $version"
Write-Host "Root    : $repoRoot"
Write-Host ""

# ── Stap 1: opruimen ──────────────────────────────────────────────────────────
Write-Host "→ dist/ opruimen..." -ForegroundColor Yellow
if (Test-Path $distDir) { Remove-Item $distDir -Recurse -Force }
New-Item -ItemType Directory -Path $distDir | Out-Null

# ── Stap 2: publish Windows x64 ───────────────────────────────────────────────
Write-Host "→ Publish Windows x64..." -ForegroundColor Yellow
$winOut = Join-Path $distDir "win-x64"
dotnet publish $projectDir `
    -r win-x64 `
    --self-contained `
    -c Release `
    -o $winOut `
    /p:PublishSingleFile=false `
    --nologo
if ($LASTEXITCODE -ne 0) { Write-Error "Windows publish mislukt!"; exit 1 }
Write-Host "  ✅ Windows build klaar: $winOut" -ForegroundColor Green

# ── Stap 3: publish Linux x64 ─────────────────────────────────────────────────
Write-Host "→ Publish Linux x64..." -ForegroundColor Yellow
$linOut = Join-Path $distDir "linux-x64"
dotnet publish $projectDir `
    -r linux-x64 `
    --self-contained `
    -c Release `
    -o $linOut `
    /p:PublishSingleFile=false `
    --nologo
if ($LASTEXITCODE -ne 0) { Write-Error "Linux publish mislukt!"; exit 1 }
Write-Host "  ✅ Linux build klaar: $linOut" -ForegroundColor Green

# ── Stap 4: zippen ────────────────────────────────────────────────────────────
Write-Host "→ Zippen..." -ForegroundColor Yellow

$winZip = Join-Path $repoRoot "RadioBridge-$version-win-x64.zip"
$linZip = Join-Path $repoRoot "RadioBridge-$version-linux-x64.zip"

if (Test-Path $winZip) { Remove-Item $winZip -Force }
if (Test-Path $linZip) { Remove-Item $linZip -Force }

Compress-Archive -Path "$winOut\*" -DestinationPath $winZip
Write-Host "  ✅ $winZip" -ForegroundColor Green

# Linux zip: gebruik 7-Zip als beschikbaar, anders PowerShell (geen execute-bits)
$sz = Get-Command "7z" -ErrorAction SilentlyContinue
if ($sz) {
    & 7z a -tzip $linZip "$linOut\*" | Out-Null
    Write-Host "  ✅ $linZip  (via 7-Zip)" -ForegroundColor Green
} else {
    Compress-Archive -Path "$linOut\*" -DestinationPath $linZip
    Write-Host "  ✅ $linZip  (via PowerShell — execute-bits mogelijk verloren; zie opmerking)" -ForegroundColor Yellow
    Write-Host "     TIP: installeer 7-Zip voor correcte Linux-permissies in de zip." -ForegroundColor DarkYellow
}

# ── Stap 5: upload naar GitHub release ───────────────────────────────────────
Write-Host ""
Write-Host "→ Uploaden naar GitHub release $version..." -ForegroundColor Yellow

# Controleer gh CLI
if (-not (Get-Command "gh" -ErrorAction SilentlyContinue)) {
    Write-Host "⚠  gh CLI niet gevonden. Installeer via:" -ForegroundColor Red
    Write-Host "   winget install GitHub.cli" -ForegroundColor Red
    Write-Host ""
    Write-Host "Zips zijn aangemaakt — upload handmatig:" -ForegroundColor Yellow
    Write-Host "  $winZip"
    Write-Host "  $linZip"
    exit 0
}

# Verwijder bestaande assets met dezelfde naam (overschrijft)
foreach ($asset in @("RadioBridge-$version-win-x64.zip", "RadioBridge-$version-linux-x64.zip")) {
    Write-Host "  Verwijder eventueel bestaand asset: $asset"
    gh release delete-asset $version $asset --yes 2>$null | Out-Null
}

# Upload
gh release upload $version $winZip $linZip --clobber
if ($LASTEXITCODE -ne 0) { Write-Error "Upload mislukt! Controleer: gh auth status"; exit 1 }

Write-Host ""
Write-Host "=== Klaar! ===" -ForegroundColor Green
Write-Host "Release : https://github.com/pe5jw/RadioBridge/releases/tag/$version"
Write-Host "Windows : RadioBridge-$version-win-x64.zip"
Write-Host "Linux   : RadioBridge-$version-linux-x64.zip"
Write-Host ""
