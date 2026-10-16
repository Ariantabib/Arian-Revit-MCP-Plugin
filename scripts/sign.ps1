<#
  Authenticode-signs the plugin DLLs so Revit stops asking "publisher could not be verified" (when the certificate is trusted by the machine/organisation).
  Usage: powershell -File scripts\sign.ps1 -Pfx C:\certs\arian.pfx -Password (Read-Host -AsSecureString)  [-Folder <dir with the DLLs>]
  Needs signtool.exe (Windows SDK). A self-signed certificate does NOT remove the prompt unless you add it to Trusted Publishers yourself.
#>
param(
    [Parameter(Mandatory = $true)][string]$Pfx,
    [Parameter(Mandatory = $true)][securestring]$Password,
    [string]$Folder,
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $Folder) { $Folder = Join-Path $root "src\Arian.RevitMcp\bin\Release\net10.0-windows" }
$signtool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match "x64" } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $signtool) { throw "signtool.exe not found (install the Windows 10/11 SDK)." }
$plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password))
foreach ($name in "Arian.RevitMcp.dll", "Arian.RevitMcp.Tools.dll") {
    $f = Join-Path $Folder $name
    if (-not (Test-Path $f)) { Write-Warning "skip $f"; continue }
    & $signtool.FullName sign /f $Pfx /p $plain /fd SHA256 /tr $TimestampUrl /td SHA256 $f
    if ($LASTEXITCODE -ne 0) { throw "signing failed for $f" }
    Write-Host "signed $name"
}
