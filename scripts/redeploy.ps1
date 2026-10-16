<#
  Developer loop: build -> close Revit -> install -> start Revit -> wait until the MCP server answers.
  Usage: powershell -ExecutionPolicy Bypass -File scripts\redeploy.ps1 [-NoStart]
#>
param([switch]$NoStart, [int]$Port = 8765)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }
& $dotnet build (Join-Path $root "Arian.RevitMcp.sln") -c Release -nologo -v q 2>&1 | Where-Object { $_ -match "error|Build succeeded" } | Sort-Object -Unique
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$rv = Get-Process Revit -ErrorAction SilentlyContinue
if ($rv) {
    Write-Host "Closing Revit..."
    $rv | ForEach-Object { try { $_.CloseMainWindow() | Out-Null } catch {} }
    Start-Sleep -Seconds 4
    Get-Process Revit -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
}

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "install.ps1") -NoBuild | Out-Null
Write-Host "Installed."

function Click-AlwaysLoad {
    # Revit asks once per new unsigned add-in build: "The publisher of this add-in could not be verified". Choose "Always Load".
    # The click is posted as a window message to the button itself, so the physical mouse / other windows are never touched.
    try {
        Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
        Add-Type -ErrorAction SilentlyContinue @"
using System; using System.Runtime.InteropServices;
public static class ArianWin { [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l); }
"@
        $p = Get-Process Revit -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $p) { return $false }
        $root = [System.Windows.Automation.AutomationElement]::RootElement
        $c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
        $main = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $c)
        if (-not $main) { return $false }
        $q = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "Always Load")
        $hit = $main.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $q)
        if (-not $hit) { return $false }
        $h = $hit.Current.NativeWindowHandle
        if ($h -eq 0) { return $false }
        $lp = [IntPtr](5 + (5 -shl 16))
        [ArianWin]::PostMessage([IntPtr]$h, 0x0201, [IntPtr]1, $lp) | Out-Null
        Start-Sleep -Milliseconds 80
        [ArianWin]::PostMessage([IntPtr]$h, 0x0202, [IntPtr]0, $lp) | Out-Null
        return $true
    } catch { return $false }
}

if (-not $NoStart) {
    Start-Process "C:\Program Files\Autodesk\Revit 2027\Revit.exe"
    Write-Host "Revit starting..."
    $deadline = (Get-Date).AddSeconds(240)
    while ((Get-Date) -lt $deadline) {
        try {
            $r = Invoke-WebRequest "http://127.0.0.1:$Port/health" -UseBasicParsing -TimeoutSec 3
            if ($r.StatusCode -eq 200) { Write-Host "MCP server UP: $($r.Content)"; exit 0 }
        } catch {}
        if (Click-AlwaysLoad) { Write-Host "(clicked 'Always Load' on the add-in security prompt)" }
        Start-Sleep -Seconds 3
    }
    Write-Warning "Server did not come up within 240 s"
    exit 1
}
