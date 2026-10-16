<#
  Builds the Arian Revit MCP Plugin and installs it for Revit 2027 (per-user, no admin needed).
  Usage:  powershell -ExecutionPolicy Bypass -File scripts\install.ps1  [-NoBuild]
  Revit must be closed (the plugin DLL is locked while Revit runs).
#>
param([switch]$NoBuild, [string]$RevitYear = "2027")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$sln = Join-Path $root "Arian.RevitMcp.sln"

# locate a .NET 10 SDK
$dotnet = "dotnet"
$userDotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (Test-Path $userDotnet) { $dotnet = $userDotnet }

if (-not $NoBuild) {
    & $dotnet build $sln -c Release -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }
}

$out = Join-Path $root "src\Arian.RevitMcp\bin\Release\net10.0-windows"
$toolsOut = Join-Path $root "src\Arian.RevitMcp.Tools\bin\Release\net10.0-windows"
$addins = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitYear"
$dest = Join-Path $addins "ArianRevitMcp"

if (Get-Process Revit -ErrorAction SilentlyContinue) {
    Write-Warning "Revit is running - the DLL may be locked. Close Revit and re-run if copying fails."
}

New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item "$out\*" $dest -Recurse -Force
Copy-Item (Join-Path $toolsOut "Arian.RevitMcp.Tools.*") $dest -Force

$dll = Join-Path $dest "Arian.RevitMcp.dll"
$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>Arian Revit MCP Plugin</Name>
    <Assembly>$dll</Assembly>
    <AddInId>7B1C2E5A-4D3F-4A8B-9C61-A41A00C0DE01</AddInId>
    <FullClassName>Arian.RevitMcp.App</FullClassName>
    <VendorId>ARIAN</VendorId>
    <VendorDescription>Arian - AI agent bridge for Revit</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
$manifestPath = Join-Path $addins "ArianRevitMcp.addin"
Set-Content -Path $manifestPath -Value $manifest -Encoding UTF8

Write-Host "Installed to $dest"
Write-Host "Manifest:   $manifestPath"
Write-Host "Start Revit $RevitYear -> 'Arian' tab -> Start Server."
Write-Host "(First launch: Revit asks about an unsigned add-in - choose 'Always Load'.)"
