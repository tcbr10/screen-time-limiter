param([int]$Timeout = 60)
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$ErrorActionPreference = 'Stop'
$out = 'C:\dev\ui.out'; $done = 'C:\dev\ui.done'
Remove-Item $out, $done -EA 0
$action = New-ScheduledTaskAction -Execute 'wscript.exe' -Argument 'C:\dev\hidden.vbs'
$principal = New-ScheduledTaskPrincipal -UserId "$env:COMPUTERNAME\Docker" -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 30)
Register-ScheduledTask -TaskName 'SL-UI' -Action $action -Principal $principal -Settings $settings -Force | Out-Null
Start-ScheduledTask -TaskName 'SL-UI'
$sw = [Diagnostics.Stopwatch]::StartNew()
while (-not (Test-Path $done) -and $sw.Elapsed.TotalSeconds -lt $Timeout) { Start-Sleep -Milliseconds 300 }
if (-not (Test-Path $done)) { "TIMEOUT after $Timeout s"; Stop-ScheduledTask SL-UI -EA 0; Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | ? { $_.CommandLine -match "ui.ps1" } | % { Stop-Process -Id $_.ProcessId -Force -EA 0 } }
if (Test-Path $out) { Get-Content $out -Encoding utf8 }
