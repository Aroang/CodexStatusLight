#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
linux_runtime="${1:-linux-x64}"
windows_runtime="${2:-win-x64}"
windows_publish_script="$(wslpath -w "$repo_root/scripts/publish.ps1")"
output="$repo_root/artifacts/wsl-${linux_runtime#linux-}"
linux_build_platform="wsl-$linux_runtime"

if [[ -z "${WSL_DISTRO_NAME:-}" || ! -e /proc/sys/fs/binfmt_misc/WSLInterop ]]; then
    echo "publish-wsl.sh must run inside WSL with Windows interop enabled." >&2
    exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
    echo ".NET 8 SDK is required inside WSL." >&2
    exit 1
fi

find_windows_powershell() {
    if command -v powershell.exe >/dev/null 2>&1; then
        command -v powershell.exe
        return
    fi

    local candidate
    for candidate in \
        /mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe \
        "/mnt/c/Program Files/PowerShell/7/pwsh.exe"; do
        if [[ -f "$candidate" ]]; then
            printf '%s\n' "$candidate"
            return
        fi
    done

    return 1
}

if ! windows_powershell="$(find_windows_powershell)"; then
    echo "Windows PowerShell was not found." >&2
    echo "Enable Windows drive mounting/interop, or add powershell.exe to WSL PATH." >&2
    exit 1
fi

"$windows_powershell" -NoLogo -NoProfile -ExecutionPolicy Bypass \
    -File "$windows_publish_script" -Runtime "$windows_runtime"

mkdir -p "$output"
dotnet restore "$repo_root/src/CodexMonitor.Launcher/CodexMonitor.Launcher.csproj" \
    -r "$linux_runtime" \
    -p:CodexBuildPlatform="$linux_build_platform" \
    --configfile "$repo_root/NuGet.Config"
dotnet publish "$repo_root/src/CodexMonitor.Launcher/CodexMonitor.Launcher.csproj" \
    -c Release -r "$linux_runtime" --self-contained true \
    -p:CodexBuildPlatform="$linux_build_platform" \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:DebugType=None -p:DebugSymbols=false --no-restore -o "$output"

cp "$repo_root/artifacts/$windows_runtime/CodexMonitor.exe" "$output/CodexMonitor.exe"
echo "Published WSL launcher and Windows status light to $output"
