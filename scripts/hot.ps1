<#
  Fast dev loop: rebuild ONLY the tools assembly and hot-swap it into the running Revit (no restart).
  Usage: powershell -ExecutionPolicy Bypass -File scripts\hot.ps1
  Requires the MCP server to be running in Revit. Changes to the main assembly (Core/Server/UI) still need scripts\redeploy.ps1.
#>
param([int]$Port = 8765)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }
$out = & $dotnet build (Join-Path $root "src\Arian.RevitMcp.Tools\Arian.RevitMcp.Tools.csproj") -c Release -nologo -v q 2>&1
$errs = $out | Where-Object { $_ -match ": error " } | Sort-Object -Unique
if ($errs) { $errs | ForEach-Object { Write-Host $_ }; throw "build failed" }
$dll = Join-Path $root "src\Arian.RevitMcp.Tools\bin\Release\net10.0-windows\Arian.RevitMcp.Tools.dll"
$body = @{ jsonrpc = "2.0"; id = 1; method = "tools/call"; params = @{ name = "reload_tools"; arguments = @{ path = $dll } } } | ConvertTo-Json -Depth 6
$r = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/mcp" -Method Post -ContentType "application/json" -Body $body
$r.result.content[0].text
