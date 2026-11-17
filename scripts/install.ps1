#Requires -RunAsAdministrator
<#
  Builds Screen Limiter from source and installs it on this PC.
  Run from an elevated PowerShell:   .\scripts\install.ps1
  Needs the .NET 10 SDK (https://dotnet.microsoft.com/download).
  Safe to run again to upgrade: settings and usage are kept.
#>
$ErrorActionPreference = 'Stop'

$repo        = Split-Path -Parent $PSScriptRoot
$installDir  = Join-Path $env:ProgramFiles 'ScreenLimiter'
$serviceName = 'ScreenLimiter'
$serviceExe  = Join-Path $installDir 'ScreenLimiter.Service.exe'
$adminExe    = Join-Path $installDir 'ScreenLimiter.Admin.exe'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET 10 SDK was not found. Install it from https://dotnet.microsoft.com/download and run this again.'
}

Write-Host 'Stopping the running copy, if any...'
if (Get-Service $serviceName -ErrorAction SilentlyContinue) {
    Stop-Service $serviceName -Force -ErrorAction SilentlyContinue
}
Get-Process 'ScreenLimiter.Agent', 'ScreenLimiter.Admin' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

Write-Host "Building into $installDir ..."
foreach ($project in 'ScreenLimiter.Service', 'ScreenLimiter.Agent', 'ScreenLimiter.Admin') {
    dotnet publish (Join-Path $repo "src\$project") -c Release -o $installDir --nologo
    if ($LASTEXITCODE -ne 0) { throw "Building $project failed." }
}

Write-Host 'Registering the service...'
if (-not (Get-Service $serviceName -ErrorAction SilentlyContinue)) {
    New-Service -Name $serviceName -BinaryPathName "`"$serviceExe`"" -DisplayName 'Screen Limiter' `
        -Description 'Enforces screen time limits for local users.' -StartupType Automatic | Out-Null
}
# Restart automatically if the service ever crashes.
sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/5000/restart/5000 | Out-Null
# Run in Safe Mode too. Anyone can pick Safe Mode from the sign-in screen's restart options,
# and Windows starts only the services listed here.
foreach ($mode in 'Minimal', 'Network') {
    $key = "HKLM:\SYSTEM\CurrentControlSet\Control\SafeBoot\$mode\$serviceName"
    New-Item -Path $key -Force | Out-Null
    Set-ItemProperty -Path $key -Name '(default)' -Value 'Service'
}
Start-Service $serviceName

Write-Host 'Adding the Start menu shortcut...'
$shortcutPath = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Screen Limiter.lnk'
$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
$shortcut.TargetPath = $adminExe
$shortcut.WorkingDirectory = $installDir
$shortcut.Save()

Write-Host ''
Write-Host 'Installed. Open "Screen Limiter" from the Start menu to set limits.'
Write-Host 'Reminder: the accounts you limit must be standard users, not administrators.'
