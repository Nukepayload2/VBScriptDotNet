' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' L3 session tests (test-plan §3.6, T-SES-1..16 and the session part of T-API-1). Every test is in-process:
' no network, no child process, no repository file written. Temp directories are used only by the #Load test.
' Concurrency and cancellation are driven by TaskCompletionSource and a CancellationTokenSource, never by sleeps.

Imports System
Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.DependencyInjection.Extensions
Imports Nukepayload2.VBScriptDotNet.Hosting
Imports Xunit

Public Class HostingSessionTests

    ''' <summary>
    ''' T-SES-1: an expression submission reports the value vbi would have printed, and the library does not echo it
    ''' to <see cref="IScriptOutput"/>.
    ''' </summary>
    <Fact>
    Public Async Function T_SES_1_PrintedValueIsReturnedAndScriptOutputIsNotWritten() As Task
        Using fixture As New RunnerFixture()
            Dim session = CreateSession(fixture)

            Dim result = Await session.SubmitAsync("1+1")

            Assert.False(HasError(result.Diagnostics))
            Assert.Equal(2, CInt(result.PrintedValue))
            Assert.False(result.IsFaulted)
            Assert.Null(result.Exception)

            ' PrintedValue is data; the session must not write it to the configured output.
            Assert.Equal(String.Empty, fixture.Output.OutText)
            Assert.Equal(String.Empty, fixture.Output.ErrorText)

            Await session.DisposeAsync()
        End Using
    End Function

    ''' <summary>T-SES-2: variables flow from one submission into the next.</summary>
    <Fact>
    Public Async Function T_SES_2_SubmissionsShareOneChain() As Task
        Using fixture As New RunnerFixture()
            Dim session = CreateSession(fixture)

            Dim first = Await session.SubmitAsync("Dim x = 1")
            Assert.False(HasError(first.Diagnostics))

            Dim second = Await session.SubmitAsync("x + 1")
            Assert.False(HasError(second.Diagnostics))
            Assert.Equal(2, CInt(second.PrintedValue))

            Dim x = session.GetVariable("x")
            Assert.NotNull(x)
            Assert.Equal(1, CInt(x.Value))

            Await session.DisposeAsync()
        End Using
    End Function

    ''' <summary>T-SES-3: redeclaring the same name keeps one de-duplicated variable whose value is the latest.</summary>
    <Fact>
    Public Async Function T_SES_3_RepeatedDeclarationIsDeduplicated() As Task
        Using fixture As New RunnerFixture()
            Dim session = CreateSession(fixture)

            Await session.SubmitAsync("Dim x = 1")
            Await session.SubmitAsync("Dim x = 2")

            Assert.Equal(1, CountVariablesNamed(session, "x"))
            Dim latest = session.GetVariable("x")
            Assert.NotNull(latest)
            Assert.Equal(2, CInt(latest.Value))

            Dim expression = Await session.SubmitAsync("x")
            Assert.Equal(2, CInt(expression.PrintedValue))

            Await session.DisposeAsync()
        End Using
    End Function

    ''' <summary>
    ''' T-SES-4: a compile error leaves the session alive and does not refresh its options, so an import
    ''' introduced by the failed submission is not visible afterwards; a later successful submission does refresh it.
    ''' </summary>
    <Fact>
    Public Async Function T_SES_4_CompileErrorKeepsTheSessionAndDoesNotRefreshOptions() As Task
        Using fixture As New RunnerFixture()
            Dim session = CreateSession(fixture)

            Dim broken = Await session.SubmitAsync("Imports System.Text.RegularExpressions" & vbCrLf & "Dim bad = )")
            Assert.True(HasError(broken.Diagnostics))

            ' The failed submission must not have applied its import.
            Dim afterFailure = Await session.SubmitAsync("Dim re = New Regex(""a"")")
            Assert.True(HasError(afterFailure.Diagnostics))

            ' The session is still usable, and a successful submission does refresh options.
            Dim imported = Await session.SubmitAsync("Imports System.Text.RegularExpressions")
            Assert.False(HasError(imported.Diagnostics))

            Dim afterSuccess = Await session.SubmitAsync("Dim re2 = New Regex(""a"")")
            Assert.False(HasError(afterSuccess.Diagnostics))

            Await session.DisposeAsync()
        End Using
    End Function

    ''' <summary>
    ''' T-SES-5: a run-time exception is reported in the result and the session continues; only cancellation
    ''' faults a session, so IsFaulted stays False here.
    ''' </summary>
    <Fact>
    Public Async Function T_SES_5_RuntimeExceptionLeavesTheSessionUsable() As Task
        Using fixture As New RunnerFixture()
            Dim session = CreateSession(fixture)

            Dim failed = Await session.SubmitAsync("Throw New InvalidOperationException(""boom"")")
            Assert.NotNull(failed.Exception)
            Assert.True(failed.Exception.Message.Contains("boom"), "the reported exception should be the script's own")
            Assert.False(failed.IsFaulted)

            Dim followUp = Await session.SubmitAsync("1+1")
            Assert.Equal(2, CInt(followUp.PrintedValue))
            Assert.False(followUp.IsFaulted)

            Await session.DisposeAsync()
        End Using
    End Function

    ''' <summary>T-SES-6: the three-state completeness classification of submission text.</summary>
    <Fact>
    Public Sub T_SES_6_IsCompleteSubmissionReportsTheThreeStates()
        Using fixture As New RunnerFixture()
            Dim session = CreateSession(fixture)

            Assert.Equal(VBScriptSubmissionState.Complete, session.IsCompleteSubmission("1+1"))
            Assert.Equal(VBScriptSubmissionState.Complete, session.IsCompleteSubmission("If True Then" & vbCrLf & "    Return 1" & vbCrLf & "End If"))
            Assert.Equal(VBScriptSubmissionState.Incomplete, session.IsCompleteSubmission("If True Then"))
            Assert.Equal(VBScriptSubmissionState.Incomplete, session.IsCompleteSubmission("Dim z ="))
            Assert.Equal(VBScriptSubmissionState.Invalid, session.IsCompleteSubmission("Dim x = )"))

            session.DisposeAsync().AsTask().GetAwaiter().GetResult()
        End Using
    End Sub

    ''' <summary>
    ''' T-SES-7: Reset clears the variables, restores the initial imports/references, keeps the globals instance and
    ''' creates a fresh assembly loader.
    ''' </summary>
    <Fact>
    Public Async Function T_SES_7_ResetRestoresTheInitialSnapshotAndKeepsGlobals() As Task
        Using fixture As New RunnerFixture()
            Dim globals As New VBScriptGlobals(fixture.Output)
            Dim options As New VBScriptOptions()
            options.Globals = globals
            options.[Imports].Add("System.Xml.Linq")

            Dim session = CreateSession(fixture, options)
            Dim loaderBefore = GetSessionLoader(session)

            Await session.SubmitAsync("Args.Add(""kept"")")
            Await session.SubmitAsync("Imports System.Text.RegularExpressions")
            Dim before = Await session.SubmitAsync("Dim before = 42")
            Assert.False(HasError(before.Diagnostics))
            Assert.NotNull(session.GetVariable("before"))

            session.Reset()

            Assert.Empty(session.Variables)
            Assert.Null(session.GetVariable("before"))
            Assert.NotSame(loaderBefore, GetSessionLoader(session))
            Assert.Same(globals, GetSessionGlobals(session))

            ' The import added by a submission is gone (the chain was cleared) ...
            Dim staleImport = Await session.SubmitAsync("Dim re = New Regex(""a"")")
            Assert.True(HasError(staleImport.Diagnostics))

            ' ... while the initial imports are restored and the same globals instance survived.
            Dim initialImport = Await session.SubmitAsync("Dim doc = New XDocument()")
            Assert.False(HasError(initialImport.Diagnostics))

            Dim kept = Await session.SubmitAsync("Return Args.Count")
            Assert.Equal(1, CInt(kept.PrintedValue))

            Await session.DisposeAsync()
        End Using
    End Function

    ''' <summary>
    ''' T-SES-8: cancelling a submission reports IsFaulted, the next submission throws the library's own exception,
    ''' and Reset makes the session usable again.
    ''' </summary>
    <Fact>
    Public Async Function T_SES_8_CancellationFaultsTheSessionUntilReset() As Task
        Using fixture As New RunnerFixture()
            Dim globals As New SessionGatedGlobals(fixture.Output)
            Dim options As New VBScriptOptions()
            options.Globals = globals

            Dim session = CreateSession(fixture, options)

            Using cancellation As New CancellationTokenSource()
                Dim pending = StartSubmission(session, "Print(1)", cancellation.Token)
                Await globals.Entered
                cancellation.Cancel()
                globals.Release()

                Dim cancelled = Await pending
                Assert.True(cancelled.IsFaulted)

                Await Assert.ThrowsAsync(Of VBScriptHostException)(
                    Async Function() As Task
                        Await session.SubmitAsync("1+1")
                    End Function)

                session.Reset()
            End Using

            Dim reused = Await session.SubmitAsync("1+1")
            Assert.False(reused.IsFaulted)
            Assert.Equal(2, CInt(reused.PrintedValue))

            Await session.DisposeAsync()
        End Using
    End Function

    ''' <summary>T-SES-9: DisposeAsync is idempotent and no submission is accepted afterwards.</summary>
    <Fact>
    Public Async Function T_SES_9_DisposeAsyncIsIdempotent() As Task
        Using fixture As New RunnerFixture()
            Dim session = CreateSession(fixture)

            Await session.DisposeAsync()
            Await session.DisposeAsync()

            Await Assert.ThrowsAsync(Of VBScriptHostException)(
                Async Function() As Task
                    Await session.SubmitAsync("1+1")
                End Function)
        End Using
    End Function

    ''' <summary>
    ''' T-SES-10: two sessions have independent loaders and variables and can run concurrently; a second concurrent
    ''' submission on the same session throws <see cref="VBScriptBusyException"/>.
    ''' </summary>
    <Fact>
    Public Async Function T_SES_10_SessionsAreIndependentAndAReentrantSubmissionIsRejected() As Task
        Using fixture As New RunnerFixture()
            Dim gateA As New SessionGatedGlobals(fixture.Output)
            Dim gateB As New SessionGatedGlobals(fixture.Output)
            Dim firstOptions As New VBScriptOptions()
            firstOptions.Globals = gateA
            Dim secondOptions As New VBScriptOptions()
            secondOptions.Globals = gateB

            Dim first = CreateSession(fixture, firstOptions)
            Dim second = CreateSession(fixture, secondOptions)

            Assert.NotSame(GetSessionLoader(first), GetSessionLoader(second))

            Dim taskA = StartSubmission(first, "Print(1)")
            Await gateA.Entered
            Dim taskB = StartSubmission(second, "Print(2)")
            Await gateB.Entered

            gateA.Release()
            gateB.Release()

            Assert.False((Await taskA).IsFaulted)
            Assert.False((Await taskB).IsFaulted)

            Await first.SubmitAsync("Dim onlyFirst = 1")
            Await second.SubmitAsync("Dim onlySecond = 2")
            Assert.NotNull(first.GetVariable("onlyFirst"))
            Assert.Null(first.GetVariable("onlySecond"))
            Assert.NotNull(second.GetVariable("onlySecond"))
            Assert.Null(second.GetVariable("onlyFirst"))

            ' Same session concurrently: the guard rejects the second call explicitly.
            Dim busyGate As New SessionGatedGlobals(fixture.Output)
            Dim busyOptions As New VBScriptOptions()
            busyOptions.Globals = busyGate
            Dim busy = CreateSession(fixture, busyOptions)

            Dim pending = StartSubmission(busy, "Print(3)")
            Await busyGate.Entered

            Await Assert.ThrowsAsync(Of VBScriptBusyException)(
                Async Function() As Task
                    Await busy.SubmitAsync("1+1")
                End Function)

            busyGate.Release()
            Assert.False((Await pending).IsFaulted)

            ' Sessions are not registered in the container.
            Assert.Null(fixture.Provider.GetService(Of IVBScriptSession)())

            Await busy.DisposeAsync()
            Await first.DisposeAsync()
            Await second.DisposeAsync()
        End Using
    End Function

    ''' <summary>
    ''' T-SES-11: #Load resolves relative to VBScriptOptions.WorkingDirectory, and the base directory is not reset
    ''' to the process current directory by the options refresh.
    ''' </summary>
    <Fact>
    Public Async Function T_SES_11_LoadUsesTheConfiguredWorkingDirectory() As Task
        Dim tempDirectory = CreateTempDirectory()
        Try
            File.WriteAllText(Path.Combine(tempDirectory, "session-lib.vbx"), "Dim loadedValue = 5")

            Using fixture As New RunnerFixture()
                Dim options As New VBScriptOptions()
                options.WorkingDirectory = tempDirectory

                Dim session = CreateSession(fixture, options)

                Dim result = Await session.SubmitAsync("#Load ""session-lib.vbx""" & vbCrLf & "loadedValue + 1")
                Assert.False(HasError(result.Diagnostics))
                Assert.Equal(6, CInt(result.PrintedValue))

                ' The host's option object is untouched, and the session's base directory stayed on it.
                Assert.Equal(tempDirectory, options.WorkingDirectory)
                Assert.Equal(tempDirectory, GetSessionBaseDirectory(session))

                ' Reset rebuilds the core from the initial options snapshot. If it reset the base directory
                ' to the process current directory, the second #Load would no longer find session-lib.vbx.
                session.Reset()
                Assert.Equal(tempDirectory, GetSessionBaseDirectory(session))
                Assert.NotEqual(Path.GetFullPath(Environment.CurrentDirectory), GetSessionBaseDirectory(session))

                Dim afterReset = Await session.SubmitAsync("#Load ""session-lib.vbx""" & vbCrLf & "loadedValue + 1")
                Assert.False(HasError(afterReset.Diagnostics))
                Assert.Equal(6, CInt(afterReset.PrintedValue))
                Assert.Equal(tempDirectory, GetSessionBaseDirectory(session))

                Await session.DisposeAsync()
            End Using
        Finally
            Try
                IO.Directory.Delete(tempDirectory, recursive:=True)
            Catch ex As IOException
                ' Cleanup failure must not turn a passing session test into a failure.
            End Try
        End Try
    End Function

    ''' <summary>
    ''' T-SES-12: the restore seam runs once per submission, and the imports replay does not accumulate duplicates
    ''' across submissions.
    ''' </summary>
    <Fact>
    Public Async Function T_SES_12_RestoreRunsPerSubmissionAndImportsDoNotAccumulate() As Task
        Dim restore As New FakeNuGetRestoreService()
        Using fixture As New RunnerFixture(
                afterRegistration:=Sub(services)
                                          services.Replace(ServiceDescriptor.Singleton(Of INuGetRestoreService)(restore))
                                      End Sub)
            Dim session = CreateSession(fixture)

            Await session.SubmitAsync("Dim a = 1")
            Await session.SubmitAsync("Dim b = 2")
            Dim imported = Await session.SubmitAsync("Imports System.Text")
            Assert.False(HasError(imported.Diagnostics))

            Assert.Equal(3, restore.Calls)

            Dim importsAfterFirst = GetSessionOptionImports(session)
            Assert.Equal(importsAfterFirst.Length, importsAfterFirst.Distinct(StringComparer.OrdinalIgnoreCase).Count())

            Await session.SubmitAsync("Imports System.Text")
            Dim importsAfterSecond = GetSessionOptionImports(session)
            Assert.Equal(importsAfterFirst.Length, importsAfterSecond.Length)
            Assert.Equal(importsAfterSecond.Length, importsAfterSecond.Distinct(StringComparer.OrdinalIgnoreCase).Count())

            Dim builder = Await session.SubmitAsync("Dim sb = New StringBuilder()")
            Assert.False(HasError(builder.Diagnostics))
            Assert.Equal(5, restore.Calls)

            Await session.DisposeAsync()
        End Using
    End Function

    ''' <summary>T-SES-13: imports declared by one submission are replayed into the next.</summary>
    <Fact>
    Public Async Function T_SES_13_ImportsFromAPreviousSubmissionAreReplayed() As Task
        Using fixture As New RunnerFixture()
            Dim session = CreateSession(fixture)

            Dim imported = Await session.SubmitAsync("Imports System.Text.RegularExpressions")
            Assert.False(HasError(imported.Diagnostics))

            Dim used = Await session.SubmitAsync("Dim re = New Regex(""a"")")
            Assert.False(HasError(used.Diagnostics))
            Assert.NotNull(session.GetVariable("re"))

            Await session.DisposeAsync()
        End Using
    End Function

    ''' <summary>
    ''' T-SES-14: variables are de-duplicated case-insensitively with the latest value, IsReadOnly is reported, and
    ''' globals never appear in Variables.
    ''' </summary>
    <Fact>
    Public Async Function T_SES_14_VariablesAreDeduplicatedCaseInsensitively() As Task
        Using fixture As New RunnerFixture()
            Dim session = CreateSession(fixture)

            Await session.SubmitAsync("Dim x = 1")
            Await session.SubmitAsync("Dim X = 2")
            Await session.SubmitAsync("Const C = 3")

            Assert.Equal(1, CountVariablesNamed(session, "x"))
            Dim latest = session.GetVariable("x")
            Assert.NotNull(latest)
            Assert.Equal(2, CInt(latest.Value))
            Assert.False(latest.IsReadOnly)

            Dim constant = session.GetVariable("c")
            Assert.NotNull(constant)
            Assert.True(constant.IsReadOnly)
            Assert.Equal(3, CInt(constant.Value))

            Assert.Null(session.GetVariable("Print"))
            Assert.Null(session.GetVariable("Args"))
            Assert.False(session.Variables.Any(Function(variable) String.Equals(variable.Name, "Args", StringComparison.OrdinalIgnoreCase)))

            Await session.DisposeAsync()
        End Using
    End Function

    ''' <summary>T-SES-15: submitting after the host signalled ApplicationStopping throws the library's own exception.</summary>
    <Fact>
    Public Async Function T_SES_15_SubmissionAfterApplicationStoppingThrows() As Task
        Using fixture As New RunnerFixture()
            Dim session = CreateSession(fixture)

            fixture.Lifetime.SignalStopping()

            Await Assert.ThrowsAsync(Of VBScriptHostException)(
                Async Function() As Task
                    Await session.SubmitAsync("1+1")
                End Function)

            Await session.DisposeAsync()
        End Using
    End Function

    ''' <summary>
    ''' T-SES-16: DisposeAsync while a submission is in flight cancels it, waits for it, and after it returns no
    ''' submission is accepted.
    ''' </summary>
    <Fact>
    Public Async Function T_SES_16_DisposeAsyncCancelsAnInFlightSubmission() As Task
        Using fixture As New RunnerFixture()
            Dim globals As New SessionGatedGlobals(fixture.Output)
            Dim options As New VBScriptOptions()
            options.Globals = globals

            Dim session = CreateSession(fixture, options)
            Dim pending = StartSubmission(session, "Print(1)")
            Await globals.Entered

            Dim disposing = session.DisposeAsync().AsTask()
            globals.Release()

            Dim cancelled = Await pending
            Assert.True(cancelled.IsFaulted)
            Await disposing

            Await Assert.ThrowsAsync(Of VBScriptHostException)(
                Async Function() As Task
                    Await session.SubmitAsync("1+1")
                End Function)
        End Using
    End Function

    ' ---------------------------------------------------------------------------------------------------------
    ' Helpers
    ' ---------------------------------------------------------------------------------------------------------

    Private Shared Function CreateSession(fixture As RunnerFixture, Optional options As VBScriptOptions = Nothing) As IVBScriptSession
        Dim factory = fixture.Provider.GetRequiredService(Of IVBScriptSessionFactory)()
        Return factory.CreateSession(If(options, New VBScriptOptions()))
    End Function

    Private Shared Function HasError(diagnostics As ImmutableArray(Of VBScriptDiagnostic)) As Boolean
        Return diagnostics.Any(Function(diagnostic) diagnostic.Severity = VBScriptDiagnosticSeverity.Error)
    End Function

    Private Shared Function CountVariablesNamed(session As IVBScriptSession, name As String) As Integer
        Return session.Variables.Count(Function(variable) String.Equals(variable.Name, name, StringComparison.OrdinalIgnoreCase))
    End Function

    ''' <summary>
    ''' A submission whose script blocks inside <c>Print</c> must not be started on the test thread: the async
    ''' pipeline runs the script synchronously up to its first incomplete await, so starting it here would block
    ''' the test before it can observe or release the gate.
    ''' </summary>
    Private Shared Function StartSubmission(session As IVBScriptSession, code As String, Optional cancellationToken As CancellationToken = Nothing) As Task(Of VBScriptSubmissionResult)
        Return Task.Run(
            Async Function() As Task(Of VBScriptSubmissionResult)
                Return Await session.SubmitAsync(code, cancellationToken)
            End Function)
    End Function

    ''' <summary>
    ''' The session's internals are Friend to this assembly, but the fields of the core belong to Roslyn's assembly;
    ''' reflection keeps the test from needing a new product seam just to observe the loader identity and the
    ''' refreshed imports.
    ''' </summary>
    Private Shared Function GetPrivateField(instance As Object, name As String) As Object
        Dim type = instance.GetType()
        While type IsNot Nothing
            Dim field = type.GetField(name, BindingFlags.NonPublic Or BindingFlags.Instance)
            If field IsNot Nothing Then
                Return field.GetValue(instance)
            End If

            type = type.BaseType
        End While

        Throw New InvalidOperationException("Field '" & name & "' was not found on " & instance.GetType().FullName & ".")
    End Function

    Private Shared Function GetSessionLoader(session As IVBScriptSession) As Object
        Return GetPrivateField(session, "_loader")
    End Function

    Private Shared Function GetSessionGlobals(session As IVBScriptSession) As Object
        Return GetPrivateField(session, "_globals")
    End Function

    Private Shared Function GetSessionCore(session As IVBScriptSession) As Object
        Return GetPrivateField(session, "_core")
    End Function

    ''' <summary>Reads the <c>ScriptOptions.Imports</c> the session will compile its next submission with.</summary>
    Private Shared Function GetSessionOptionImports(session As IVBScriptSession) As ImmutableArray(Of String)
        Dim core = GetSessionCore(session)
        Dim options = GetPrivateField(core, "_options")
        Dim propertyInfo = options.GetType().GetProperty("Imports", BindingFlags.Public Or BindingFlags.Instance)
        Return DirectCast(propertyInfo.GetValue(options), ImmutableArray(Of String))
    End Function

    Private Shared Function GetSessionBaseDirectory(session As IVBScriptSession) As String
        Dim core = GetSessionCore(session)
        Dim request = GetPrivateField(core, "_request")
        Dim propertyInfo = request.GetType().GetProperty("BaseDirectory", BindingFlags.Public Or BindingFlags.Instance)
        Return DirectCast(propertyInfo.GetValue(request), String)
    End Function

    Private Shared Function CreateTempDirectory() As String
        Dim tempDirectory = Path.Combine(Path.GetTempPath(), "vbscript-session-" & Guid.NewGuid().ToString("N"))
        IO.Directory.CreateDirectory(tempDirectory)
        Return tempDirectory
    End Function

End Class

''' <summary>
''' Globals whose <c>Print</c> signals that the script reached it and then waits for the test to release it. Used to
''' hold a submission in flight so re-entry and cancellation are observed without <see cref="Thread.Sleep"/>.
''' Public because the submission assembly must be able to see the globals type.
''' </summary>
Public NotInheritable Class SessionGatedGlobals
    Inherits VBScriptGlobals

    Private ReadOnly _entered As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
    Private ReadOnly _release As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)

    Public Sub New(output As IScriptOutput)
        MyBase.New(output)
    End Sub

    ''' <summary>Completes when the script called <c>Print</c>.</summary>
    Friend ReadOnly Property Entered As Task
        Get
            Return _entered.Task
        End Get
    End Property

    ''' <summary>Lets the blocked <c>Print</c> continue.</summary>
    Friend Sub Release()
        _release.TrySetResult(True)
    End Sub

    Public Overrides Sub Print(value As Object)
        _entered.TrySetResult(True)
        _release.Task.GetAwaiter().GetResult()
        MyBase.Print(value)
    End Sub

End Class
