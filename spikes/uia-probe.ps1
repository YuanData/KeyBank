param([int[]]$ProcessIds)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$scrollAvail = New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty), $true
foreach ($id in $ProcessIds) {
    $p = Get-Process -Id $id
    $root = [Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $found = $root.FindAll([Windows.Automation.TreeScope]::Descendants, $scrollAvail)
    "{0} (pid {1}) scrollable elements={2} searchMs={3}" -f $p.Name, $id, $found.Count, $sw.ElapsedMilliseconds
    $n = 0
    foreach ($e in $found) {
        if ($n++ -ge 6) { break }
        $sp = $e.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
        $c = $sp.Current
        "  class={0} name='{1}' vSize={2:N1} vPct={3:N1} vScrollable={4}" -f $e.Current.ClassName, $e.Current.Name, $c.VerticalViewSize, $c.VerticalScrollPercent, $c.VerticallyScrollable
    }
}
