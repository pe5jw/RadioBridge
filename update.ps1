#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Haalt RadioBridge voor Windows van GitHub en zet hem in dist\RadioBridge\.

.GEBRUIK
    .\update.ps1             # nieuwste stabiele versie
    .\update.ps1 -Dev        # nieuwste ontwikkelversie (automatische build van main)
    .\update.ps1 -Start      # en daarna RadioBridge starten
#>
param([switch]$Start, [switch]$Dev)

$ErrorActionPreference = "Stop"
$repo    = "pe5jw/RadioBridge"
$asset   = "RadioBridge-win-x64.zip"
$url     = if ($Dev) { "https://github.com/$repo/releases/download/dev/$asset" }
           else      { "https://github.com/$repo/releases/latest/download/$asset" }
$target  = Join-Path $PSScriptRoot "dist"
$zip     = Join-Path $env:TEMP $asset

Write-Host "→ RadioBridge afsluiten (indien actief)..." -ForegroundColor Yellow
Get-Process RadioBridge -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Write-Host "→ Downloaden ($(if ($Dev) { 'ontwikkelversie' } else { 'stabiel' })): $url" -ForegroundColor Yellow
Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing

Write-Host "→ Uitpakken naar $target\RadioBridge ..." -ForegroundColor Yellow
New-Item -ItemType Directory -Path $target -Force | Out-Null
try {
    Expand-Archive -Path $zip -DestinationPath $target -Force
} catch {
    Write-Host "⚠  Uitpakken mislukt: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "   Staat de map open in Total Commander / Verkenner, of draait RadioBridge nog?" -ForegroundColor Red
    exit 1
}
Remove-Item $zip -Force

$exe = Join-Path $target "RadioBridge\RadioBridge.exe"
$ver = (Get-Item $exe).VersionInfo.ProductVersion -replace '\+.*$', ''
Write-Host "✅ RadioBridge $ver → $exe" -ForegroundColor Green

if ($Start) { Start-Process $exe }
