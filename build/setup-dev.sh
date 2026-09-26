#!/usr/bin/env bash
#
# Checks, and optionally installs, what developing Basalt needs on macOS and
# Linux. The counterpart of build/setup-dev.ps1: keep the two in step.
#
# Prints one row per tool, present or missing. Without --check it also
# installs netcoredbg into ~/.basalt/debugger/<rid>, where the IDE and the
# debugger tests look for it. Running it twice changes nothing the second time.
#
# clang, Node.js and the JDK are only checked: they come from the system's
# package manager (Xcode command line tools, apt, brew), which is the user's
# to drive.
#
#   build/setup-dev.sh --check
#   build/setup-dev.sh
#
set -euo pipefail

netcoredbg_version="3.2.0-1092"
check_only=false
[[ "${1:-}" == "--check" ]] && check_only=true

case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*)
    # Git Bash reports itself as Linux-like, and would look for a Linux
    # netcoredbg where the IDE looks for netcoredbg.exe.
    echo "On Windows run build/setup-dev.ps1 instead." >&2
    exit 1
    ;;
  Darwin) os=osx ;;
  *)      os=linux ;;
esac

case "$(uname -m)" in
  arm64|aarch64) arch=arm64 ;;
  *)             arch=x64 ;;
esac

rid="$os-$arch"
debugger_folder="$HOME/.basalt/debugger/$rid"
netcoredbg="$debugger_folder/netcoredbg"

install_netcoredbg() {
  local asset
  case "$rid" in
    linux-x64)   asset="netcoredbg-linux-amd64.tar.gz" ;;
    linux-arm64) asset="netcoredbg-linux-arm64.tar.gz" ;;
    osx-arm64)   asset="netcoredbg-osx-arm64.zip" ;;
    *)
      echo "No netcoredbg build is published for $rid: build it from source" >&2
      echo "(https://github.com/Samsung/netcoredbg) and set BASALT_NETCOREDBG." >&2
      return 0
      ;;
  esac

  local url="https://github.com/Samsung/netcoredbg/releases/download/$netcoredbg_version/$asset"
  local work
  work="$(mktemp -d)"

  echo "Downloading netcoredbg $netcoredbg_version for $rid"
  curl -fsSL "$url" -o "$work/$asset"

  if [[ "$asset" == *.zip ]]; then
    unzip -q "$work/$asset" -d "$work"
  else
    tar -xzf "$work/$asset" -C "$work"
  fi

  mkdir -p "$debugger_folder"
  cp -R "$work/netcoredbg/." "$debugger_folder/"
  chmod +x "$netcoredbg"
  rm -rf "$work"
}

if [[ "$check_only" == false && ! -x "$netcoredbg" ]]; then
  install_netcoredbg
fi

first_line() { "$@" 2>&1 | head -1 || true; }

row() {
  local tool="$1" found="$2" needed_for="$3"
  local state="present"
  [[ -z "$found" ]] && state="MISSING"
  printf '%-14s %-8s %-45s %s\n' "$tool" "$state" "${found:0:45}" "$needed_for"
}

printf '%-14s %-8s %-45s %s\n' "Tool" "State" "Detail" "Needed for"

row ".NET SDK 10" "$(command -v dotnet >/dev/null && first_line dotnet --version)" "everything"
row "git" "$(command -v git >/dev/null && first_line git --version)" "everything"

found_debugger=""
if [[ -x "$netcoredbg" ]]; then found_debugger="$netcoredbg"; fi
if [[ -z "$found_debugger" && -n "${BASALT_NETCOREDBG:-}" ]]; then found_debugger="$BASALT_NETCOREDBG"; fi
row "netcoredbg" "$found_debugger" "debugging, debugger tests"

row "clang" "$(command -v clang >/dev/null && first_line clang --version)" "native QuickBASIC tests"
row "Node.js 18+" "$(command -v node >/dev/null && first_line node --version)" "VS Code extension (check only)"
row "JDK 21" "${JAVA_HOME:-}" "Rider plugin (check only)"
