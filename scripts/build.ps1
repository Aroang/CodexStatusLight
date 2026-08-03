$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $repoRoot '.tools\dotnet\dotnet.exe'
$env:DOTNET_CLI_HOME = Join-Path $repoRoot '.dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:APPDATA = Join-Path $repoRoot '.dotnet-home\AppData\Roaming'
$env:NUGET_PACKAGES = Join-Path $repoRoot '.nuget\packages'

if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = 'dotnet'
}

& $dotnet restore (Join-Path $repoRoot 'CodexMonitor.sln') --configfile (Join-Path $repoRoot 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $dotnet build (Join-Path $repoRoot 'CodexMonitor.sln') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $dotnet run --project (Join-Path $repoRoot 'tests\CodexMonitor.SelfTest\CodexMonitor.SelfTest.csproj') -c Release --no-build
exit $LASTEXITCODE
