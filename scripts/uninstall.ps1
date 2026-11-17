#Requires -RunAsAdministrator
<#
  Removes Screen Limiter from this PC.
  Run from an elevated PowerShell:   .\scripts\uninstall.ps1
  Add -RemoveData to also delete the saved limits and usage history.
#>
param([switch]$RemoveData)
$ErrorActionPreference = 'Stop'

$installDir  = Join-Path $env:ProgramFiles 'ScreenLimiter'
$dataDir     = Join-Path $env:ProgramData 'ScreenLimiter'
$serviceName = 'ScreenLimiter'

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
if (Test-Path $installDir) { Remove-Item $installDir -Recurse -Force }

if ($RemoveData -and (Test-Path $dataDir)) {
    Remove-Item $dataDir -Recurse -Force
    Write-Host 'Removed the program and its saved data.'
} else {
    Write-Host "Removed the program. Saved limits are still in $dataDir (use -RemoveData to delete them)."
}
