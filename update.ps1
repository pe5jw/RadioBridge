#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Haalt de nieuwste Windows-build van RadioBridge van GitHub (gebouwd door GitHub Actions)
    en zet hem in dist\RadioBridge\.

.GEBRUIK
    .\update.ps1            # downloaden + uitpakken
    .\update.ps1 -Start     # en daarna RadioBridge starten
#>
param([switch]$Start)

$ErrorActionPreference = "Stop"
$version = "v0.9"
$url     = "https://github.com/pe5jw/RadioBridge/releases/download/$version/RadioBridge-$version-win-x64.zip"
$target  = Join-Path $PSScriptRoot "dist"
$zip     = Join-Path $env:TEMP "RadioBridge-$version-win-x64.zip"

Write-Host "→ RadioBridge afsluiten (indien actief)..." -ForegroundColor Yellow
Get-Process RadioBridge -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Write-Host "→ Downloaden: $url" -ForegroundColor Yellow
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
Write-Host "✅ Klaar: $exe  ($((Get-Item $exe).LastWriteTime))" -ForegroundColor Green

if ($Start) { Start-Process $exe }
