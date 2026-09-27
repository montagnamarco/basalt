param([string] $RecordedTrx)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "acceptance-results.ps1")

# xUnit's VSTest adapter can emit NotExecuted records while the aggregate
# notExecuted counter remains zero. These records carry the actual skip reason.
[xml] $skipped = @'
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    <UnitTestResult testName="A" outcome="Passed" />
    <UnitTestResult testName="B" outcome="NotExecuted"><Output><ErrorInfo><Message>clang missing</Message></ErrorInfo></Output></UnitTestResult>
  </Results>
  <ResultSummary><Counters total="2" executed="1" passed="1" failed="0" notExecuted="0" /></ResultSummary>
</TestRun>
'@
$result = Get-AcceptanceResult $skipped 0
if ($result.Outcome -ne "Incomplete" -or $result.Skipped -ne 1) {
    throw "An xUnit skipped result must be incomplete with one skip; got $($result.Outcome), $($result.Skipped)."
}

[xml] $passed = '<TestRun><Results><UnitTestResult outcome="Passed" /></Results><ResultSummary><Counters total="1" passed="1" failed="0" notExecuted="0" /></ResultSummary></TestRun>'
if ((Get-AcceptanceResult $passed 0).Outcome -ne "Passed") { throw "A passing report was rejected." }
if ((Get-AcceptanceResult $passed 7).Outcome -ne "Failed") { throw "A nonzero process exit passed." }
[xml] $failed = '<TestRun><Results><UnitTestResult outcome="Failed" /></Results><ResultSummary><Counters total="1" passed="0" failed="1" notExecuted="0" /></ResultSummary></TestRun>'
if ((Get-AcceptanceResult $failed 1).Outcome -ne "Failed") { throw "A failing test passed." }
[xml] $empty = '<TestRun><ResultSummary><Counters total="0" passed="0" failed="0" notExecuted="0" /></ResultSummary></TestRun>'
if ((Get-AcceptanceResult $empty 0).Outcome -ne "Failed") { throw "An empty report passed." }
[xml] $aborted = '<TestRun><Results><UnitTestResult outcome="Aborted" /></Results><ResultSummary><Counters total="1" passed="0" failed="0" notExecuted="0" aborted="1" /></ResultSummary></TestRun>'
if ((Get-AcceptanceResult $aborted 0).Outcome -ne "Failed") { throw "An aborted test was treated as a skip." }

if ($RecordedTrx) {
    [xml] $recorded = Get-Content -LiteralPath $RecordedTrx -Raw
    $result = Get-AcceptanceResult $recorded 0
    $actualSkipped = @($recorded.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -eq "NotExecuted" }).Count
    if ($actualSkipped -eq 0 -or $result.Skipped -ne $actualSkipped -or $result.Outcome -ne "Incomplete") {
        throw "Recorded xUnit report was not classified correctly."
    }
    Write-Output "Recorded report: $($result.Passed) passed, $($result.Skipped) skipped, $($result.Outcome)."
}
Write-Output "Acceptance result checks passed (skips, success, process failure, test failure, empty, aborted)."
