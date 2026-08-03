param(
    [string]$InstallDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\win-x64')
)

$ErrorActionPreference = 'Stop'
$installDirectory = [IO.Path]::GetFullPath($InstallDirectory)
$launcherPath = Join-Path $installDirectory 'codex-watch.exe'
$wrapperPath = Join-Path $installDirectory 'codex.cmd'

if (-not (Test-Path -LiteralPath $launcherPath)) {
    throw "Missing $launcherPath. Run scripts\publish.ps1 first."
}
if (-not (Test-Path -LiteralPath $wrapperPath)) {
    throw "Missing $wrapperPath. Run scripts\publish.ps1 first."
}

$originalCodex = [Environment]::GetEnvironmentVariable('CODEX_CLI_PATH', 'User')
if ([string]::IsNullOrWhiteSpace($originalCodex) -or -not (Test-Path -LiteralPath $originalCodex)) {
    $originalCodex = Get-Command codex -All -ErrorAction Stop |
        Where-Object { $_.Source -and -not $_.Source.StartsWith($installDirectory, [StringComparison]::OrdinalIgnoreCase) } |
        Select-Object -First 1 -ExpandProperty Source
}
if ([string]::IsNullOrWhiteSpace($originalCodex)) {
    throw 'The original Codex CLI was not found. Install Codex CLI first.'
}

$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if ($null -eq $userPath) { $userPath = '' }
$pathParts = @($userPath -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if (-not ($pathParts | Where-Object { [IO.Path]::GetFullPath($_) -eq $installDirectory })) {
    $userPath = if ($userPath) { "$installDirectory;$userPath" } else { $installDirectory }
    [Environment]::SetEnvironmentVariable('Path', $userPath, 'User')
}

[Environment]::SetEnvironmentVariable('CODEX_CLI_PATH', $originalCodex, 'User')
Write-Host 'Installed. The codex command will start an independent status light in new PowerShell/CMD sessions.'
Write-Host "Original Codex: $originalCodex"
Write-Host 'Close and reopen the terminal before use.'
