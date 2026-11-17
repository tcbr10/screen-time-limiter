#Requires -RunAsAdministrator
<#
  Removes Screen Limiter from this PC.
  From the download package: double-click Uninstall.cmd.
  From source: run from an elevated PowerShell   .\scripts\uninstall.ps1
  Add -RemoveData to also delete the saved limits and usage history.
  -Ask shows a Hebrew yes/no question first, and whether to delete the saved settings (Uninstall.cmd uses it).
#>
param([switch]$RemoveData, [switch]$Ask)
$ErrorActionPreference = 'Stop'

$installDir  = Join-Path $env:ProgramFiles 'ScreenLimiter'
$dataDir     = Join-Path $env:ProgramData 'ScreenLimiter'
$serviceName = 'ScreenLimiter'

if ($Ask) {
    Add-Type -AssemblyName System.Windows.Forms
    $rtl = [System.Windows.Forms.MessageBoxOptions]::RtlReading -bor [System.Windows.Forms.MessageBoxOptions]::RightAlign
    $sure = [System.Windows.Forms.MessageBox]::Show("להסיר את מגביל זמן מסך מהמחשב?`n`nאחרי ההסרה לא יהיו מגבלות זמן לאף משתמש.",
        'הסרת מגביל זמן מסך', 'YesNo', 'Question', 'Button2', $rtl)
    if ($sure -ne 'Yes') { exit 0 }
    $wipe = [System.Windows.Forms.MessageBox]::Show("למחוק גם את ההגדרות השמורות (המגבלות וקוד ההורה)?`n`nאם תבחרו `"לא`", הן יחזרו אם תתקינו שוב.",
        'הסרת מגביל זמן מסך', 'YesNo', 'Question', 'Button2', $rtl)
    $RemoveData = $wipe -eq 'Yes'
}

if (Get-Service $serviceName -ErrorAction SilentlyContinue) {
    Stop-Service $serviceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $serviceName | Out-Null
}
foreach ($mode in 'Minimal', 'Network') {
    Remove-Item "HKLM:\SYSTEM\CurrentControlSet\Control\SafeBoot\$mode\$serviceName" -Recurse -ErrorAction SilentlyContinue
}
Get-Process 'ScreenLimiter.Agent', 'ScreenLimiter.Admin' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

Remove-Item (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Screen Limiter.lnk') -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'Screen Limiter.lnk') -ErrorAction SilentlyContinue
if (Test-Path $installDir) { Remove-Item $installDir -Recurse -Force }

if ($RemoveData -and (Test-Path $dataDir)) {
    Remove-Item $dataDir -Recurse -Force
    Write-Host 'Removed the program and its saved data.'
} else {
    Write-Host "Removed the program. Saved limits are still in $dataDir (use -RemoveData to delete them)."
}

if ($Ask) {
    [System.Windows.Forms.MessageBox]::Show('מגביל זמן מסך הוסר מהמחשב.', 'הסרת מגביל זמן מסך', 'OK', 'Information', 'Button1', $rtl) | Out-Null
}
