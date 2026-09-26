<#
.SYNOPSIS
Checks, and optionally installs, what developing Basalt needs on Windows.

.DESCRIPTION
Prints one row per tool: present or missing, and what it is needed for.
Without -Check it also installs what is missing and can be installed
without an administrator: netcoredbg into ~/.basalt/debugger/<rid>, where
the IDE and the debugger tests look for it, and LLVM through winget for the
native QuickBASIC tests. Running it twice changes nothing the second time.

Node.js, the JDK and the Visual Studio extension workload are only checked:
they serve the editor extensions, and installing an IDE workload is the
kind of change a developer makes knowingly, not as a side effect.

.EXAMPLE
build\setup-dev.ps1 -Check
build\setup-dev.ps1
#>
param(
    [switch] $Check
)

$ErrorActionPreference = "Stop"

$netcoredbgVersion = "3.2.0-1092"

function Test-Command([string] $Name) {
    return [bool] (Get-Command $Name -ErrorAction SilentlyContinue)
}

function Get-FirstLine([scriptblock] $Command) {
    try {
        $line = & $Command 2>&1 | Select-Object -First 1
        return "$line".Trim()
    }
    catch {
        return ""
    }
}

$architecture = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "arm64" } else { "x64" }
$rid = "win-$architecture"
$debuggerFolder = Join-Path $HOME ".basalt\debugger\$rid"
$netcoredbg = Join-Path $debuggerFolder "netcoredbg.exe"

function Install-Netcoredbg {
    # Only a 64-bit Intel build is published for Windows; on ARM64 it runs
    # under emulation, which is enough for the tests.
    $url = "https://github.com/Samsung/netcoredbg/releases/download/$netcoredbgVersion/netcoredbg-win64.zip"
    $zip = Join-Path ([IO.Path]::GetTempPath()) "netcoredbg-$netcoredbgVersion.zip"
    $unpacked = Join-Path ([IO.Path]::GetTempPath()) "netcoredbg-$netcoredbgVersion"

    Write-Host "Downloading netcoredbg $netcoredbgVersion"
    Invoke-WebRequest $url -OutFile $zip -UseBasicParsing
    Expand-Archive $zip -DestinationPath $unpacked -Force

    New-Item -ItemType Directory -Force $debuggerFolder | Out-Null
    Copy-Item (Join-Path $unpacked "netcoredbg\*") $debuggerFolder -Recurse -Force

    Remove-Item $zip, $unpacked -Recurse -Force -ErrorAction SilentlyContinue
}

function Install-Llvm {
    if (-not (Test-Command "winget")) {
        Write-Warning "winget is not available: install LLVM from https://github.com/llvm/llvm-project/releases"
        return
    }

    winget install --id LLVM.LLVM --exact --silent --accept-package-agreements --accept-source-agreements
    Write-Host "LLVM installed: open a new terminal so clang is on the PATH."
}

if (-not $Check) {
    if (-not (Test-Path $netcoredbg)) { Install-Netcoredbg }
    if (-not (Test-Command "clang")) { Install-Llvm }
}

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$extensionWorkload = if (Test-Path $vswhere) {
    & $vswhere -requires Microsoft.VisualStudio.Workload.VisualStudioExtension -property displayName | Select-Object -First 1
} else { "" }

$rows = @(
    [pscustomobject]@{
        Tool = ".NET SDK 10"; For = "everything"
        Found = Get-FirstLine { dotnet --version }
    },
    [pscustomobject]@{
        Tool = "git"; For = "everything"
        Found = Get-FirstLine { git --version }
    },
    [pscustomobject]@{
        Tool = "netcoredbg"; For = "debugging, debugger tests"
        Found = if (Test-Path $netcoredbg) { $netcoredbg } elseif ($env:BASALT_NETCOREDBG) { $env:BASALT_NETCOREDBG } else { "" }
    },
    [pscustomobject]@{
        Tool = "clang"; For = "native QuickBASIC tests (skipped without it)"
        Found = if (Test-Command "clang") { Get-FirstLine { clang --version } } else { "" }
    },
    [pscustomobject]@{
        Tool = "Node.js 18+"; For = "VS Code extension (check only)"
        Found = if (Test-Command "node") { Get-FirstLine { node --version } } else { "" }
    },
    [pscustomobject]@{
        Tool = "JDK 21"; For = "Rider plugin (check only)"
        Found = if ($env:JAVA_HOME) { $env:JAVA_HOME } else { "" }
    },
    [pscustomobject]@{
        Tool = "VS extension workload"; For = "Visual Studio extension (check only)"
        Found = "$extensionWorkload"
    }
)

$rows | ForEach-Object {
    [pscustomobject]@{
        Tool = $_.Tool
        State = if ($_.Found) { "present" } else { "MISSING" }
        Detail = $_.Found
        NeededFor = $_.For
    }
} | Format-Table -AutoSize
