Add-Type -AssemblyName System.Windows.Forms, System.Drawing, UIAutomationClient, UIAutomationTypes
New-Item -ItemType Directory C:\dev\shots -Force | Out-Null
function Shot([string]$name, $rect = $null) {
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    if ($rect) { $b = New-Object System.Drawing.Rectangle $rect[0], $rect[1], $rect[2], $rect[3] }
    $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($b.X, $b.Y, 0, 0, $b.Size); $g.Dispose()
    $bmp.Save("C:\dev\shots\$name.png", [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    "shot: $name"
}
$UIA = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
function Win([string]$processName, [int]$wait = 10) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $wait) {
        foreach ($p in Get-Process $processName -EA 0) {
            foreach ($w in $UIA::RootElement.FindAll($TS::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
                if ($w.Current.ProcessId -eq $p.Id) { return $w }
            }
        }
        Start-Sleep -Milliseconds 300
    }
}
function Find($root, [string]$name) {
    $c = New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, $name)
    $root.FindFirst($TS::Descendants, $c)
}
function FindId($root, [string]$id) {
    $c = New-Object System.Windows.Automation.PropertyCondition($UIA::AutomationIdProperty, $id)
    $root.FindFirst($TS::Descendants, $c)
}
function Click($el) {
    $p = $null
    if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$p)) { $p.Invoke(); return }
    if ($el.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$p)) { $p.Select(); return }
    if ($el.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$p)) { $p.Toggle(); return }
    throw "can't click $($el.Current.Name)"
}
function Dump($root) {
    foreach ($e in $root.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        $r = $e.Current.BoundingRectangle
        "{0,-14} id={1,-18} name={2} [{3},{4} {5}x{6}]" -f $e.Current.ControlType.ProgrammaticName.Replace('ControlType.',''), $e.Current.AutomationId, $e.Current.Name, [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height
    }
}
function FindBtn($root, [string]$name) {
    $c = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, $name)),
        (New-Object System.Windows.Automation.PropertyCondition($UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
    $root.FindFirst($TS::Descendants, $c)
}
function ChildWindow($root) {
    $root.FindFirst($TS::Children, (New-Object System.Windows.Automation.PropertyCondition($UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)))
}
function TypeInto($el, $text) { $el.SetFocus(); Start-Sleep -Milliseconds 200; [System.Windows.Forms.SendKeys]::SendWait($text) }
function Texts($root) { ($root.FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))) | % { $_.Current.Name } | ? { $_ }) -join ' | ' }
