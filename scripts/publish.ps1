param(
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $repoRoot '.tools\dotnet\dotnet.exe'
$output = Join-Path $repoRoot "artifacts\$Runtime"
$env:DOTNET_CLI_HOME = Join-Path $repoRoot '.dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:APPDATA = Join-Path $repoRoot '.dotnet-home\AppData\Roaming'
$env:NUGET_PACKAGES = Join-Path $repoRoot '.nuget\packages'

if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = 'dotnet'
}

New-Item -ItemType Directory -Path $output -Force | Out-Null

$common = @(
    '-c', 'Release',
    '-r', $Runtime,
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-o', $output
)

$appProject = Join-Path $repoRoot 'src\CodexMonitor.App\CodexMonitor.App.csproj'
$launcherProject = Join-Path $repoRoot 'src\CodexMonitor.Launcher\CodexMonitor.Launcher.csproj'

& $dotnet restore $appProject -r $Runtime --configfile (Join-Path $repoRoot 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet restore $launcherProject -r $Runtime --configfile (Join-Path $repoRoot 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $dotnet publish $appProject @common --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet publish $launcherProject @common --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Published CodexMonitor.exe and codex-watch.exe to $output"
