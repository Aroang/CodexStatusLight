#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source_dir="${1:-$repo_root/artifacts/wsl-x64}"
install_dir="${2:-$HOME/.local/bin}"

if [[ ! -x "$source_dir/codex-watch" || ! -f "$source_dir/CodexMonitor.exe" ]]; then
    echo "Missing WSL artifacts. Run: bash scripts/publish-wsl.sh" >&2
    exit 1
fi

mkdir -p "$install_dir"
cp "$source_dir/codex-watch" "$install_dir/codex-watch"
cp "$source_dir/CodexMonitor.exe" "$install_dir/CodexMonitor.exe"
chmod +x "$install_dir/codex-watch"

echo "Installed codex-watch to $install_dir"
echo "Ensure that directory is on PATH, then run: codex-watch"
