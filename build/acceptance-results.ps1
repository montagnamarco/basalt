# Shared TRX interpretation; kept separate so real and synthetic reports can
# exercise failure handling without rebuilding or rerunning the application.
function Get-AcceptanceResult {
    param([xml] $Trx, [int] $ExitCode)

    $counters = $Trx.TestRun.ResultSummary.Counters
    if ($null -eq $counters) { throw "TRX has no result counters." }
    $total = [int] $counters.total
    $passed = [int] $counters.passed
    $records = @($Trx.TestRun.Results.UnitTestResult)
    $failed = [Math]::Max([int] $counters.failed,
        @($records | Where-Object { $_.outcome -eq "Failed" }).Count)
    # xUnit can leave notExecuted at zero even when its individual results
    # contain skipped tests. Count those records instead of inventing success
    # or failure from the gap between total and passed.
    $skipped = [Math]::Max([int] $counters.notExecuted,
        @($records | Where-Object { $_.outcome -eq "NotExecuted" }).Count)
    $unexpected = @($records | Where-Object { $_.outcome -notin @("Passed", "NotExecuted") }).Count
    $consistent = $records.Count -eq $total -and $unexpected -eq 0
    $outcome = "Failed"
    if ($ExitCode -eq 0 -and $consistent -and $total -gt 0 -and $passed -eq $total -and
        $failed -eq 0 -and $skipped -eq 0) {
        $outcome = "Passed"
    }
    elseif ($ExitCode -eq 0 -and $consistent -and $failed -eq 0 -and $skipped -gt 0 -and
        ($passed + $skipped) -eq $total) {
        $outcome = "Incomplete"
    }
    return [pscustomobject] @{ Total = $total; Passed = $passed; Failed = $failed; Skipped = $skipped; Outcome = $outcome }
}
