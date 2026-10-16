<#
  Builds a release zip: dist\ArianRevitMcp-<version>.zip  (plugin binaries + bridge + Install/Uninstall + README + docs).
  Usage: powershell -ExecutionPolicy Bypass -File scripts\package.ps1 [-NoBuild] [-Sign -Pfx x.pfx -Password <secure>]
  If the MCP server is running, tool docs are regenerated and the knowledge base is cross-checked first.
#>
param([switch]$NoBuild, [switch]$Sign, [string]$Pfx, [securestring]$Password, [int]$Port = 8765)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }

if (-not $NoBuild) {
    & $dotnet build (Join-Path $root "Arian.RevitMcp.sln") -c Release -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "build failed" }
}
if ($Sign) { & (Join-Path $PSScriptRoot "sign.ps1") -Pfx $Pfx -Password $Password }

# --- documentation / knowledge checks (needs the live server) ---
try {
    $h = Invoke-RestMethod "http://127.0.0.1:$Port/health" -TimeoutSec 3
    Write-Host "Server up ($($h.tools) tools): regenerating docs and checking the knowledge base..."
    & node (Join-Path $PSScriptRoot "gen_tools_doc.mjs")
    $tmp = Join-Path $env:TEMP "arian-tools.txt"
    & node (Join-Path $PSScriptRoot "call.mjs") tools/list | Set-Content $tmp -Encoding UTF8
    & python (Join-Path $PSScriptRoot "check_knowledge.py") $tmp
    if ($LASTEXITCODE -ne 0) { throw "knowledge base mentions tools that do not exist (see above)" }
} catch { Write-Host "(server not running: skipped docs regeneration / knowledge check)" }

$ver = ([xml](Get-Content (Join-Path $root "Directory.Build.props"))).Project.PropertyGroup.Version
$out = Join-Path $root "dist"
$stage = Join-Path $out "stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $stage "plugin") | Out-Null
$bin = Join-Path $root "src\Arian.RevitMcp\bin\Release\net10.0-windows"
$tools = Join-Path $root "src\Arian.RevitMcp.Tools\bin\Release\net10.0-windows"
Copy-Item "$bin\*" (Join-Path $stage "plugin") -Recurse -Force
Copy-Item (Join-Path $tools "Arian.RevitMcp.Tools.dll") (Join-Path $stage "plugin") -Force
Copy-Item (Join-Path $tools "Arian.RevitMcp.Tools.deps.json") (Join-Path $stage "plugin") -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $PSScriptRoot "dist\*") $stage -Force
Copy-Item (Join-Path $root "README.md") $stage -Force
Copy-Item (Join-Path $root "LICENSE") $stage -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root "CHANGELOG.md") $stage -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root "docs") (Join-Path $stage "docs") -Recurse -Force

$zip = Join-Path $out "ArianRevitMcp-$ver.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $zip
Remove-Item $stage -Recurse -Force
$mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "Created $zip ($mb MB)"
