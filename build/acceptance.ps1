<#
.SYNOPSIS
Runs the existing acceptance checks and writes a report with their evidence.

.DESCRIPTION
Runs test projects sequentially because they share build outputs. Requires an
already restored checkout; does not install browsers, debuggers or templates.
Skipped checks make the run incomplete (exit 2), failed checks exit 1. A green
report covers only the selected automated checks, not every F12 user journey.
Works with Windows PowerShell 5.1 and PowerShell 7.

.EXAMPLE
build/acceptance.ps1
build/acceptance.ps1 -Suite Web,Browser
build/acceptance.ps1 -List
#>
param(
    [ValidateSet("Core", "Web", "Browser")]
    [ValidateNotNullOrEmpty()]
    [string[]] $Suite = @("Core", "Web", "Browser"),
    [switch] $NoBuild,
    [string] $Filter,
    [switch] $List
)

$ErrorActionPreference = "Stop"
# PowerShell 7 can turn native nonzero exits into terminating errors. We record
# every suite's exit code ourselves so the remaining suites still get a result.
$PSNativeCommandUseErrorActionPreference = $false

$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "acceptance-results.ps1")
$checks = @(
    @{ Name = "Core"; Project = "tests/Basalt.Tests" },
    @{ Name = "Web"; Project = "tests/Basalt.Web.Tests" },
    @{ Name = "Browser"; Project = "tests/Basalt.E2E.Tests" }
) | Where-Object { $Suite -contains $_.Name }
if (@($checks).Count -eq 0) { throw "Select at least one acceptance suite." }

if ($List) {
    $checks | ForEach-Object { Write-Output ("{0}: {1}" -f $_.Name, $_.Project) }
    exit 0
}

$runId = Get-Date -Format "yyyyMMdd-HHmmss-fff"
$outputDirectory = Join-Path $root "artifacts/acceptance/$runId"
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$reportPath = Join-Path $outputDirectory "report.md"
$results = @()
$previousProcessorCount = $env:DOTNET_PROCESSOR_COUNT
$previousEvidenceDirectory = $env:BASALT_ACCEPTANCE_ARTIFACTS
$env:DOTNET_PROCESSOR_COUNT = "4"
$env:BASALT_ACCEPTANCE_ARTIFACTS = Join-Path $outputDirectory "screenshots"

function ConvertTo-ReportText([string] $Text) {
    return ($Text -replace '\|', '\|' -replace '[\r\n]+', ' ').Trim()
}

try {
    Push-Location $root
    try {
        $revision = (& git rev-parse HEAD | Out-String).Trim()
        $changes = @(& git status --porcelain --untracked-files=no)
        & git status --porcelain --untracked-files=all | Set-Content -LiteralPath (Join-Path $outputDirectory "source-status.txt") -Encoding UTF8
        & git diff --binary HEAD | Set-Content -LiteralPath (Join-Path $outputDirectory "source.patch") -Encoding UTF8
        $untrackedSources = @(& git ls-files --others --exclude-standard -- src tests build)
        foreach ($path in $untrackedSources) {
            $hash = Get-FileHash -LiteralPath (Join-Path $root $path) -Algorithm SHA256
            "$($hash.Hash)  $path" | Add-Content -LiteralPath (Join-Path $outputDirectory "untracked-source-sha256.txt") -Encoding UTF8
        }
        $sdk = (& dotnet --version | Out-String).Trim()
        $header = @(
            "# Automated acceptance report",
            "",
            "Started: $(Get-Date -Format o)",
            "Revision: $revision; tracked changes: $($changes.Count)",
            "Selected suites: $($checks.Name -join ', ')",
            "Test filter: $(ConvertTo-ReportText $Filter)",
            "Build disabled: $NoBuild. When disabled, results describe existing test binaries, which must be built from the intended source first.",
            "Source evidence: [status](source-status.txt), [tracked patch](source.patch); untracked source hashes are saved when present.",
            "Platform: $([Environment]::OSVersion); SDK: $sdk; PowerShell: $($PSVersionTable.PSVersion)",
            "",
            "Scope: existing automated checks only. This does not certify every F12 journey or Visual Studio parity.",
            "No dependencies, browsers, debugger tools or templates are installed by this runner.",
            "Browser checkpoints save screenshots in [screenshots](screenshots/). Assertions remain in the tests.",
            "",
            "| Suite | Outcome | Passed | Failed | Skipped | Total | Seconds | Evidence |",
            "| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |"
        )
        $header | Set-Content -LiteralPath $reportPath -Encoding UTF8

        foreach ($check in $checks) {
            $name = $check.Name
            $logPath = Join-Path $outputDirectory "$name.log"
            $trxPath = Join-Path $outputDirectory "$name.trx"
            $arguments = @(
                "test", $check.Project, "--no-restore", "-m:1", "-nr:false",
                "-p:UsedAvaloniaProducts=", "--results-directory", $outputDirectory,
                "--logger", "trx;LogFileName=$name.trx"
            )
            if ($NoBuild) { $arguments += "--no-build" }
            if (-not [string]::IsNullOrWhiteSpace($Filter)) { $arguments += @("--filter", $Filter) }
            $arguments | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputDirectory "$name-arguments.json") -Encoding UTF8
            Write-Host "Running $name; log: $logPath"
            $timer = [Diagnostics.Stopwatch]::StartNew()
            $exitCode = -1
            $failure = ""
            try {
                $previousErrorPreference = $ErrorActionPreference
                try {
                    # Windows PowerShell represents redirected native stderr as
                    # error records. Capture it without aborting a successful or
                    # failed native process before its exit code can be read.
                    $ErrorActionPreference = "Continue"
                    & dotnet @arguments *> $logPath
                    $exitCode = $LASTEXITCODE
                }
                finally {
                    $ErrorActionPreference = $previousErrorPreference
                }
            }
            catch {
                $failure = $_.Exception.Message
                $failure | Add-Content -LiteralPath $logPath
            }
            $timer.Stop()

            # In particular, -NoBuild must identify what was executed rather
            # than implying that the working-tree source was compiled.
            $binaryDirectory = Join-Path $root "$($check.Project)/bin/Debug/net10.0"
            if (Test-Path -LiteralPath $binaryDirectory) {
                Get-ChildItem -LiteralPath $binaryDirectory -Filter "Basalt*.dll" -File |
                    ForEach-Object {
                        $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
                        "$($hash.Hash)  $($_.Name)"
                    } | Set-Content -LiteralPath (Join-Path $outputDirectory "$name-binary-sha256.txt") -Encoding UTF8
            }

            $passed = 0
            $failed = 0
            $skipped = 0
            $total = 0
            $outcome = "Failed"
            $details = @()
            if (Test-Path -LiteralPath $trxPath) {
                try {
                    [xml] $trx = Get-Content -LiteralPath $trxPath -Raw
                    $summary = Get-AcceptanceResult $trx $exitCode
                    $total = $summary.Total
                    $passed = $summary.Passed
                    $failed = $summary.Failed
                    $skipped = $summary.Skipped
                    $outcome = $summary.Outcome
                    foreach ($test in $trx.TestRun.Results.UnitTestResult) {
                        if ($test.outcome -eq "Passed") { continue }
                        $reason = [string] $test.Output.ErrorInfo.Message
                        if ([string]::IsNullOrWhiteSpace($reason)) { $reason = [string] $test.Output.StdOut }
                        if ([string]::IsNullOrWhiteSpace($reason)) { $reason = "See TRX and console log for details." }
                        $details += "- $(ConvertTo-ReportText $test.testName): $($test.outcome). $(ConvertTo-ReportText $reason)"
                    }
                }
                catch {
                    $failure = "Could not read test evidence: $($_.Exception.Message)"
                }
            }
            else {
                $failure = "No TRX produced. $failure"
            }
            if ($total -eq 0 -and [string]::IsNullOrWhiteSpace($failure)) {
                $failure = "No tests executed."
            }
            $seconds = [Math]::Round($timer.Elapsed.TotalSeconds, 1)
            "| $name | $outcome | $passed | $failed | $skipped | $total | $seconds | [$name.log]($name.log), [$name.trx]($name.trx) |" |
                Add-Content -LiteralPath $reportPath -Encoding UTF8
            $results += @{ Name = $name; Outcome = $outcome; Details = $details; Failure = $failure; ExitCode = $exitCode }
            Write-Host "$name`: $outcome ($passed passed, $failed failed, $skipped skipped)."
        }

        foreach ($result in $results) {
            if ($result.Outcome -eq "Passed") { continue }
            @("", "## $($result.Name): $($result.Outcome)", "", "Process exit code: $($result.ExitCode)", "") |
                Add-Content -LiteralPath $reportPath -Encoding UTF8
            if ($result.Failure) { ConvertTo-ReportText $result.Failure | Add-Content -LiteralPath $reportPath -Encoding UTF8 }
            $result.Details | Add-Content -LiteralPath $reportPath -Encoding UTF8
        }
        @("", "Finished: $(Get-Date -Format o)") | Add-Content -LiteralPath $reportPath -Encoding UTF8
        Write-Host "Report: $reportPath"
    }
    finally {
        Pop-Location
    }
}
finally {
    $env:DOTNET_PROCESSOR_COUNT = $previousProcessorCount
    $env:BASALT_ACCEPTANCE_ARTIFACTS = $previousEvidenceDirectory
}

if (@($results | Where-Object { $_.Outcome -eq "Failed" }).Count -gt 0) { exit 1 }
if (@($results | Where-Object { $_.Outcome -eq "Incomplete" }).Count -gt 0) { exit 2 }
exit 0
