param([int]$ProcessId, [int]$Ticks = 3)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class W { [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, int d, UIntPtr e); }
'@
$p = Get-Process -Id $ProcessId
$root = [Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
$cond = New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty), $true
$els = $root.FindAll([Windows.Automation.TreeScope]::Descendants, $cond)
$main = $els[0]
$sp = $main.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
$rect = $main.Current.BoundingRectangle
$saved = [Windows.Forms.Cursor]::Position
[Windows.Forms.Cursor]::Position = New-Object Drawing.Point ([int]($rect.X + $rect.Width / 2)), ([int]($rect.Y + $rect.Height / 2))
$c0 = $sp.Current
Start-Sleep -Milliseconds 300
$dir = if ($c0.VerticalScrollPercent -gt 50) { 120 } else { -120 }
1..$Ticks | % { [W]::mouse_event(0x0800, 0, 0, $dir, [UIntPtr]::Zero) }
Start-Sleep -Milliseconds 900
$c1 = $sp.Current
1..$Ticks | % { [W]::mouse_event(0x0800, 0, 0, -$dir, [UIntPtr]::Zero) }
Start-Sleep -Milliseconds 900
$c2 = $sp.Current
[Windows.Forms.Cursor]::Position = $saved
$viewFrac = $c0.VerticalViewSize / 100
$deltaPct = [Math]::Abs($c1.VerticalScrollPercent - $c0.VerticalScrollPercent)
$viewsPerTick = ($deltaPct / 100) * ((1 - $viewFrac) / $viewFrac) / $Ticks
"viewSize={0:N2}% rectH={1:N0}px before={2:N3} after={3:N3} restored={4:N3}" -f $c0.VerticalViewSize, $rect.Height, $c0.VerticalScrollPercent, $c1.VerticalScrollPercent, $c2.VerticalScrollPercent
"viewsPerTick={0:N4}  => pxPerTick~{1:N0}  ticksPerFullPage~{2:N1}" -f $viewsPerTick, ($viewsPerTick * $rect.Height), (1 / $viewsPerTick)
