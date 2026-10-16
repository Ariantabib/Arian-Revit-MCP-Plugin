<#
  Diagnostic: reports whether Revit shows the unsigned-add-in prompt and whether the 'Arian' ribbon tab exists.
#>
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$p = Get-Process Revit -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Output "Revit is not running"; exit 0 }
$root = [System.Windows.Automation.AutomationElement]::RootElement
$c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$main = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $c)
if (-not $main) { Write-Output "Revit window not ready yet"; exit 0 }
$q = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "Always Load")
if ($main.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $q)) { Write-Output "SECURITY PROMPT PRESENT" } else { Write-Output "no security prompt" }
$a = $main.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "Arian")))
if ($a) { Write-Output "Arian tab present" } else { Write-Output "Arian tab NOT present" }
