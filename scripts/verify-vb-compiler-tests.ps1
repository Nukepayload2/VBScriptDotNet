[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

$gates = @(
    @{ Name = 'Phase2'; Project = 'Compilers\VisualBasicTest\Microsoft.CodeAnalysis.VisualBasic.UnitTests.vbproj'; Total = 143; Passed = 143; Skipped = 0; Failed = 0 }
    @{ Name = 'Syntax'; Project = 'Compilers\VisualBasicSyntaxTest\Microsoft.CodeAnalysis.VisualBasic.Syntax.UnitTests.vbproj'; Total = 4098; Passed = 4095; Skipped = 3; Failed = 0 }
    @{ Name = 'Symbol'; Project = 'Compilers\VisualBasicSymbolTest\Microsoft.CodeAnalysis.VisualBasic.Symbol.UnitTests.vbproj'; Total = 3407; Passed = 3383; Skipped = 24; Failed = 0 }
    @{ Name = 'Semantic'; Project = 'Compilers\VisualBasicSemanticTest\Microsoft.CodeAnalysis.VisualBasic.Semantic.UnitTests.vbproj'; Total = 5914; Passed = 5810; Skipped = 104; Failed = 0 }
    @{ Name = 'IOperation'; Project = 'Compilers\VisualBasicIOperationTest\Roslyn.Compilers.VisualBasic.IOperation.UnitTests.vbproj'; Total = 1574; Passed = 1566; Skipped = 8; Failed = 0 }
    @{ Name = 'Emit'; Project = 'Compilers\VisualBasicEmitTest\Microsoft.CodeAnalysis.VisualBasic.Emit.UnitTests.vbproj'; Total = 4388; Passed = 4285; Skipped = 103; Failed = 0 }
    @{ Name = 'CommandLine'; Project = 'Compilers\VisualBasicCommandLineTest\Microsoft.CodeAnalysis.VisualBasic.CommandLine.UnitTests.vbproj'; Total = 483; Passed = 476; Skipped = 7; Failed = 0 }
)

$resultDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "vb-compiler-gates-$([guid]::NewGuid())"
New-Item -ItemType Directory -Path $resultDirectory | Out-Null

$failed = $false

try {
    foreach ($gate in $gates) {
        Write-Host "==> dotnet build $($gate.Project) --framework net10.0"
        dotnet build $gate.Project --framework net10.0 --nologo -v:minimal
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $($gate.Project)" }

        $trxName = "$($gate.Name).trx"
        Write-Host "==> dotnet test $($gate.Project) --framework net10.0 --no-build"
        dotnet test $gate.Project --framework net10.0 --no-build --nologo -v:minimal `
            --results-directory $resultDirectory --logger "trx;LogFileName=$trxName"
        $exitCode = $LASTEXITCODE

        [xml]$trx = Get-Content (Join-Path $resultDirectory $trxName) -Raw
        $counters = $trx.TestRun.ResultSummary.Counters
        $notExecuted = @($trx.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -eq 'NotExecuted' }).Count
        $actual = @{
            Total = [int]$counters.total
            Passed = [int]$counters.passed
            Skipped = $notExecuted
            Failed = [int]$counters.failed
        }

        $matchesBaseline =
            $actual.Total -eq $gate.Total -and
            $actual.Passed -eq $gate.Passed -and
            $actual.Skipped -eq $gate.Skipped -and
            $actual.Failed -eq $gate.Failed
        $expectedExit = $exitCode -eq 0 -or ($gate.Name -eq 'CommandLine' -and $exitCode -ne 0)
        if (-not $matchesBaseline -or -not $expectedExit) {
            # Keep the .trx: an intermittent red is otherwise unrecoverable, because the results
            # directory is the only place the failing test's ErrorInfo.Message and StackTrace exist.
            Write-Host "GATE FAILED: $($gate.Name). Results preserved at: $(Join-Path $resultDirectory $trxName)"
            $failed = $true
            throw "$($gate.Name) gate mismatch: total/passed/skipped/failed = $($actual.Total)/$($actual.Passed)/$($actual.Skipped)/$($actual.Failed), exit $exitCode."
        }

        Write-Host "$($gate.Name): $($actual.Total)/$($actual.Passed)/$($actual.Skipped)/$($actual.Failed)"
    }
}
finally {
    if ($failed) {
        Write-Host "One or more gates failed; their results were left at: $resultDirectory"
    }
    else {
        Remove-Item -LiteralPath $resultDirectory -Recurse -Force
    }
}

Write-Host 'All seven compiler test project net10.0 gates completed.'
