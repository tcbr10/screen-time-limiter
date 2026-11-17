#Requires -RunAsAdministrator
<#
  Installs Screen Limiter on this PC. Safe to run again to upgrade: settings and usage are kept.

  From the download package: double-click Install.cmd (it runs this script with administrator rights).
  The ready-built program is then taken from the package's "app" folder; nothing else is needed.

  From source: run from an elevated PowerShell   .\scripts\install.ps1
  It builds the program first and needs the .NET 10 SDK (https://dotnet.microsoft.com/download).

  -Quiet skips the closing message box (used by automated tests).
#>
param([switch]$Quiet)
$ErrorActionPreference = 'Stop'

$repo        = Split-Path -Parent $PSScriptRoot
$packageDir  = Join-Path $repo 'app'
$fromPackage = Test-Path (Join-Path $packageDir 'ScreenLimiter.Service.exe')
$installDir  = Join-Path $env:ProgramFiles 'ScreenLimiter'
$serviceName = 'ScreenLimiter'
$serviceExe  = Join-Path $installDir 'ScreenLimiter.Service.exe'
$adminExe    = Join-Path $installDir 'ScreenLimiter.Admin.exe'

if (-not $fromPackage -and -not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET 10 SDK was not found. Install it from https://dotnet.microsoft.com/download and run this again.'
}

Write-Host 'Stopping the running copy, if any...'
if (Get-Service $serviceName -ErrorAction SilentlyContinue) {
    Stop-Service $serviceName -Force -ErrorAction SilentlyContinue
}
Get-Process 'ScreenLimiter.Agent', 'ScreenLimiter.Admin' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

if ($fromPackage) {
    Write-Host "Copying the program to $installDir ..."
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
    Copy-Item -Path (Join-Path $packageDir '*') -Destination $installDir -Recurse -Force
} else {
    Write-Host "Building into $installDir ..."
    foreach ($project in 'ScreenLimiter.Service', 'ScreenLimiter.Agent', 'ScreenLimiter.Admin') {
        dotnet publish (Join-Path $repo "src\$project") -c Release -o $installDir --nologo
        if ($LASTEXITCODE -ne 0) { throw "Building $project failed." }
    }
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

Write-Host 'Adding the Start menu and desktop shortcuts...'
$shell = New-Object -ComObject WScript.Shell
foreach ($folder in (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs'), [Environment]::GetFolderPath('CommonDesktopDirectory')) {
    $shortcut = $shell.CreateShortcut((Join-Path $folder 'Screen Limiter.lnk'))
    $shortcut.TargetPath = $adminExe
    $shortcut.WorkingDirectory = $installDir
    $shortcut.Description = 'Screen Limiter - settings'
    $shortcut.Save()
}

Write-Host ''
Write-Host 'Installed. Open "Screen Limiter" from the Start menu to set limits.'
Write-Host 'Reminder: the accounts you limit must be standard users, not administrators.'

if (-not $Quiet) {
    # The console can't show Hebrew properly, so the closing note is a regular Windows message box.
    Add-Type -AssemblyName System.Windows.Forms
    $text = "מגביל זמן מסך הותקן בהצלחה.`n`nעכשיו ייפתח מסך ההגדרות. בפעם הבאה אפשר לפתוח אותו מסמל `"Screen Limiter`" בשולחן העבודה או בתפריט התחל."
    $rtl = [System.Windows.Forms.MessageBoxOptions]::RtlReading -bor [System.Windows.Forms.MessageBoxOptions]::RightAlign
    [System.Windows.Forms.MessageBox]::Show($text, 'מגביל זמן מסך', 'OK', 'Information', 'Button1', $rtl) | Out-Null
    Start-Process $adminExe
}
