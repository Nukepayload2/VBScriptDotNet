' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' L3 tests for the DI runner (test-plan T-RUN-1..10) plus the file-not-found channel and the exception hierarchy.
' No network, no process, no file written; concurrency and cancellation are driven by
' TaskCompletionSource/ManualResetEventSlim, never by Thread.Sleep timing assertions.

Imports System
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Logging
Imports Nukepayload2.VBScriptDotNet.Hosting
Imports Nukepayload2.VBScriptDotNet.Scripting
Imports Xunit

Public Class HostingRunnerTests

    ''' <summary>T-RUN-1: Print(1) is captured into Output, the run succeeds, there are no diagnostics.</summary>
    <Fact>
    Public Async Function T_RUN_1_PrintOutputIsCapturedWithExitCodeZero() As Task
        Using fixture As New RunnerFixture()
            Dim result = Await fixture.Runner.RunCodeAsync("Print(1)")

            Assert.Equal(0, result.ExitCode)
            Assert.Equal("1" & Environment.NewLine, result.Output)
            Assert.True(result.Diagnostics.IsEmpty)
            Assert.Null(result.Exception)
            ' No Return expression: the reported value is Nothing, not the int session's boxed 0.
            Assert.Null(result.ReturnValue)
        End Using
    End Function

    ''' <summary>T-RUN-2: the Return value becomes the exit code and the return value.</summary>
    <Fact>
    Public Async Function T_RUN_2_ReturnValueBecomesExitCode() As Task
        Using fixture As New RunnerFixture()
            Dim result = Await fixture.Runner.RunCodeAsync("Return 42")

            Assert.Equal(42, result.ExitCode)
            Assert.Equal(42, CInt(result.ReturnValue))
            Assert.Null(result.Exception)
            Assert.True(result.Diagnostics.IsEmpty)
        End Using
    End Function

    ''' <summary>
    ''' The return-value contract: "no Return" gives <c>Nothing</c>, "Return 0" gives a boxed 0; the exit code
    ''' stays vbi's (0 in both cases). The static facade shares the same mapping.
    ''' </summary>
    <Fact>
    Public Async Function ReturnValueDistinguishesNoReturnFromReturnZero() As Task
        Using fixture As New RunnerFixture()
            Dim withoutReturn = Await fixture.Runner.RunCodeAsync("Dim x = 1")
            Assert.Equal(0, withoutReturn.ExitCode)
            Assert.Null(withoutReturn.ReturnValue)

            Dim returnsZero = Await fixture.Runner.RunCodeAsync("Return 0")
            Assert.Equal(0, returnsZero.ExitCode)
            Assert.NotNull(returnsZero.ReturnValue)
            Assert.Equal(0, CInt(returnsZero.ReturnValue))
        End Using

        ' Same mapping through the static facade (both entries share VBScriptHostInternals.MapRunOutcome).
        Dim staticWithoutReturn = Await VBScript.RunAsync("Dim x = 1")
        Assert.Equal(0, staticWithoutReturn.ExitCode)
        Assert.Null(staticWithoutReturn.ReturnValue)

        Dim staticReturnsZero = Await VBScript.RunAsync("Return 0")
        Assert.Equal(0, staticReturnsZero.ExitCode)
        Assert.NotNull(staticReturnsZero.ReturnValue)
        Assert.Equal(0, CInt(staticReturnsZero.ReturnValue))
    End Function

    ''' <summary>T-RUN-3: a compile error is data: exit code 1, BC diagnostics, no exception.</summary>
    <Fact>
    Public Async Function T_RUN_3_CompileErrorIsDataWithExitCodeOne() As Task
        Using fixture As New RunnerFixture()
            Dim result = Await fixture.Runner.RunCodeAsync("Dim x =")

            Assert.Equal(1, result.ExitCode)
            Assert.NotEmpty(result.Diagnostics)
            Assert.Contains(result.Diagnostics, Function(diagnostic) diagnostic.Id.StartsWith("BC", StringComparison.Ordinal))
            Assert.Contains(result.Diagnostics, Function(diagnostic) diagnostic.Severity = VBScriptDiagnosticSeverity.Error)
            Assert.Null(result.Exception)
        End Using
    End Function

    ''' <summary>T-RUN-4: a run-time exception is data: Exception set, exit code = HResult, no throw.</summary>
    <Fact>
    Public Async Function T_RUN_4_RuntimeExceptionBecomesExceptionAndHResultExitCode() As Task
        Using fixture As New RunnerFixture()
            Dim result = Await fixture.Runner.RunCodeAsync("Throw New System.InvalidOperationException(""boom"")")

            Assert.NotNull(result.Exception)
            Assert.Equal("boom", result.Exception.Message)
            Assert.Equal(result.Exception.HResult, result.ExitCode)
            Assert.True(result.Diagnostics.IsEmpty)
        End Using
    End Function

    ''' <summary>T-RUN-5: CompileCodeAsync reports warnings, does not execute, and returns no return value.</summary>
    <Fact>
    Public Async Function T_RUN_5_CompileCodeReportsWarningsWithoutExecuting() As Task
        Using fixture As New RunnerFixture()
            Dim code =
                "Sub S()" & Environment.NewLine &
                "    Dim unusedVar As Integer" & Environment.NewLine &
                "End Sub" & Environment.NewLine &
                "Print(""RAN"")"

            Dim result = Await fixture.Runner.CompileCodeAsync(code)

            Assert.Equal(0, result.ExitCode)
            Assert.Contains(result.Diagnostics, Function(diagnostic) diagnostic.Id = "BC42024" AndAlso diagnostic.Severity = VBScriptDiagnosticSeverity.Warning)
            Assert.Equal(String.Empty, result.Output)
            Assert.Null(result.ReturnValue)
            Assert.Null(result.Exception)
        End Using
    End Function

    ''' <summary>T-RUN-6: EvaluateAsync(Of Integer)("1+1") returns 2.</summary>
    <Fact>
    Public Async Function T_RUN_6_EvaluateAsyncReturnsTheTypedValue() As Task
        Using fixture As New RunnerFixture()
            Dim value = Await fixture.Runner.EvaluateAsync(Of Integer)("1+1")

            Assert.Equal(2, value)
        End Using
    End Function

    ''' <summary>T-RUN-7: a stopped host must not run scripts; the runner throws VBScriptHostException.</summary>
    <Fact>
    Public Async Function T_RUN_7_StoppedHostThrowsHostException() As Task
        Dim builder = Host.CreateApplicationBuilder()
        builder.Logging.ClearProviders()
        builder.AddVBScripting()

        Using host = builder.Build()
            Await host.StartAsync()
            Await host.StopAsync()

            Dim runner = host.Services.GetRequiredService(Of IVBScriptRunner)()
            Dim failure = Await Assert.ThrowsAsync(Of VBScriptHostException)(
                Async Function() As Task
                    Await runner.RunCodeAsync("Dim x = 1")
                End Function)

            Assert.Contains("stopping", failure.Message)
            Assert.IsAssignableFrom(Of VBScriptException)(failure)
        End Using
    End Function

    ''' <summary>T-RUN-8: a second concurrent call on one runner throws exactly one VBScriptBusyException.</summary>
    <Fact>
    Public Async Function T_RUN_8_ConcurrentUseOfOneRunnerThrowsBusyExactlyOnce() As Task
        Dim entered As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        Dim gate As New ManualResetEventSlim(False)
        Dim globals As New BlockingGlobals(NullScriptOutput.Instance, Sub() entered.TrySetResult(True), gate)

        Using fixture As New RunnerFixture(Sub(options) options.Globals = globals)
            Dim runner = fixture.Runner

            Dim first = Task.Run(Of VBScriptResult)(Function() runner.RunCodeAsync("Print(""first"")"))
            Await entered.Task

            ' The first call is still inside Print, so the guard is held.
            Await Assert.ThrowsAsync(Of VBScriptBusyException)(
                Async Function() As Task
                    Await runner.RunCodeAsync("Return 1")
                End Function)

            gate.Set()
            Dim firstResult = Await first

            Assert.Equal(0, firstResult.ExitCode)
            Assert.True(firstResult.Diagnostics.IsEmpty)
            Assert.Null(firstResult.Exception)
            ' BlockingGlobals was created by the test with NullScriptOutput, so the host owns its output: the
            ' runner's buffer stays empty and Print text is not double-captured.
            Assert.Equal(String.Empty, firstResult.Output)
        End Using
    End Function

    ''' <summary>T-RUN-9: cancellation while the script runs surfaces as OperationCanceledException.</summary>
    <Fact>
    Public Async Function T_RUN_9_CancellationDuringExecutionThrowsOperationCanceled() As Task
        Dim entered As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        Dim gate As New ManualResetEventSlim(False)
        Dim globals As New BlockingGlobals(NullScriptOutput.Instance, Sub() entered.TrySetResult(True), gate)

        Using fixture As New RunnerFixture(Sub(options) options.Globals = globals)
            Dim runner = fixture.Runner
            Using cancellation As New CancellationTokenSource()
                Dim running = Task.Run(Of VBScriptResult)(Function() runner.RunCodeAsync("Print(""wait"")", cancellation.Token))
                Await entered.Task

                cancellation.Cancel()
                gate.Set()

                Await Assert.ThrowsAnyAsync(Of OperationCanceledException)(
                    Async Function() As Task
                        Await running
                    End Function)

                ' The guard is released on the cancellation path: the same runner works again.
                Dim after = Await runner.RunCodeAsync("Return 5")
                Assert.Equal(5, after.ExitCode)
            End Using
        End Using
    End Function

    ''' <summary>T-RUN-10: a custom VBScriptGlobals subclass Print override is hit by the script.</summary>
    <Fact>
    Public Async Function T_RUN_10_CustomGlobalsPrintOverrideIsHit() As Task
        RecordingGlobals.ClearPrinted()

        Using fixture As New RunnerFixture(Sub(options) options.GlobalsType = GetType(RecordingGlobals))
            Dim result = Await fixture.Runner.RunCodeAsync("Print(""x"")")

            Assert.Single(RecordingGlobals.Printed)
            Assert.Equal("x", RecordingGlobals.Printed(0))
            ' The runner created the globals with its per-call output, so the text is both captured and routed.
            ' vbi's formatter quotes strings, so a String value prints as "x".
            Assert.Equal("""x""" & Environment.NewLine, result.Output)
        End Using
    End Function

    ''' <summary>A missing script file is host misuse, not script data.</summary>
    <Fact>
    Public Async Function RunFileAsyncMissingFileThrowsScriptNotFoundException() As Task
        Using fixture As New RunnerFixture(Sub(options) options.WorkingDirectory = AppContext.BaseDirectory)
            Dim failure = Await Assert.ThrowsAsync(Of VBScriptScriptNotFoundException)(
                Async Function() As Task
                    Await fixture.Runner.RunFileAsync("definitely-missing.vbx")
                End Function)

            Assert.Contains("definitely-missing.vbx", failure.Message)
            Assert.IsAssignableFrom(Of VBScriptException)(failure)
        End Using
    End Function

    ''' <summary>
    ''' A script compile failure through EvaluateAsync is a script failure
    ''' (VBScriptScriptException : VBScriptException) and is NOT caught by Catch VBScriptHostException.
    ''' </summary>
    <Fact>
    Public Async Function ScriptCompileFailureIsNotAHostException() As Task
        Using fixture As New RunnerFixture()
            Dim failure = Await Assert.ThrowsAsync(Of VBScriptScriptException)(
                Async Function() As Task
                    Await fixture.Runner.EvaluateAsync(Of Integer)("Dim x =")
                End Function)

            Assert.NotEmpty(failure.Diagnostics)
            Assert.IsAssignableFrom(Of VBScriptException)(failure)
            Assert.IsNotAssignableFrom(Of VBScriptHostException)(failure)
        End Using
    End Function

End Class
