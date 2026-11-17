<#
  Builds the download package: a ready-to-run copy of the program plus Install.cmd / Uninstall.cmd,
  zipped as out\ScreenLimiter-<version>.zip. The program is self-contained, so the PC it is
  installed on needs nothing else (no .NET). Needs the .NET 10 SDK on the PC that builds it.
  Run:   .\scripts\package.ps1
#>
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$version = ([xml](Get-Content (Join-Path $repo 'Directory.Build.props'))).Project.PropertyGroup.Version
$name = "ScreenLimiter-$version"
$out = Join-Path $repo 'out'
$stage = Join-Path $out $name

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $stage 'scripts') -Force | Out-Null

foreach ($project in 'ScreenLimiter.Service', 'ScreenLimiter.Agent', 'ScreenLimiter.Admin') {
    dotnet publish (Join-Path $repo "src\$project") -c Release -r win-x64 --self-contained true `
        -p:DebugType=None -o (Join-Path $stage 'app') --nologo
    if ($LASTEXITCODE -ne 0) { throw "Building $project failed." }
}

Copy-Item (Join-Path $PSScriptRoot 'install.ps1'), (Join-Path $PSScriptRoot 'uninstall.ps1') (Join-Path $stage 'scripts')
Copy-Item (Join-Path $PSScriptRoot 'Install.cmd'), (Join-Path $PSScriptRoot 'Uninstall.cmd') $stage

$zip = Join-Path $out "$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $stage -DestinationPath $zip
"Package: $zip ({0:N0} MB)" -f ((Get-Item $zip).Length / 1MB)
