' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' Test doubles for the hosting tests (test-plan T-DEF-4, T-DI-1/2/5/6, T-RUN-8/9/10). Everything is in-process:
' no network, no child process, no file written.

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Threading
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Logging
Imports Nukepayload2.VBScriptDotNet.Hosting

''' <summary>One record captured by <see cref="FakeLogger(Of TCategoryName)"/>.</summary>
Friend NotInheritable Class FakeLogRecord

    Friend Property Category As String
    Friend Property Level As LogLevel
    Friend Property Message As String
    Friend Property Exception As Exception

    Public Overrides Function ToString() As String
        Return Level.ToString() & " " & Category & ": " & Message
    End Function

End Class

''' <summary>Thread-safe sink shared by the fake loggers, so assertions can run after the calls completed.</summary>
Friend NotInheritable Class FakeLogSink

    Private ReadOnly _sync As New Object()
    Private ReadOnly _records As New List(Of FakeLogRecord)()

    Friend Sub Add(record As FakeLogRecord)
        SyncLock _sync
            _records.Add(record)
        End SyncLock
    End Sub

    Friend ReadOnly Property Records As IReadOnlyList(Of FakeLogRecord)
        Get
            SyncLock _sync
                Return _records.ToArray()
            End SyncLock
        End Get
    End Property

End Class

''' <summary>
''' Minimal stand-in for <c>Microsoft.Extensions.Diagnostics.Testing.FakeLogger(Of T)</c> (that package is not part of
''' the repository's restore closure): records level, category and formatted message so tests can assert that the
''' library logged exactly one warning.
''' </summary>
Friend NotInheritable Class FakeLogger(Of TCategoryName)
    Implements ILogger(Of TCategoryName)

    Private ReadOnly _sink As FakeLogSink
    Private ReadOnly _category As String

    Friend Sub New(sink As FakeLogSink)
        Me.New(sink, GetType(TCategoryName).FullName)
    End Sub

    Friend Sub New(sink As FakeLogSink, category As String)
        If sink Is Nothing Then
            Throw New ArgumentNullException(NameOf(sink))
        End If

        _sink = sink
        _category = If(category, String.Empty)
    End Sub

    Public Function BeginScope(Of TState)(state As TState) As IDisposable Implements ILogger.BeginScope
        Return Nothing
    End Function

    Public Function IsEnabled(logLevel As LogLevel) As Boolean Implements ILogger.IsEnabled
        Return True
    End Function

    Public Sub Log(Of TState)(logLevel As LogLevel, eventId As EventId, state As TState, exception As Exception, formatter As Func(Of TState, Exception, String)) Implements ILogger.Log
        _sink.Add(New FakeLogRecord With {
            .Category = _category,
            .Level = logLevel,
            .Message = formatter(state, exception),
            .Exception = exception
        })
    End Sub

End Class

''' <summary>
''' A host lifetime the test controls. The real <c>IHostApplicationLifetime</c> is not part of the facade's
''' Abstractions-only surface for a bare <see cref="IServiceCollection"/>, so runner-level tests register this one and
''' signal stopping explicitly.
''' </summary>
Friend NotInheritable Class FakeHostApplicationLifetime
    Implements IHostApplicationLifetime

    Private ReadOnly _stopping As New CancellationTokenSource()

    Public ReadOnly Property ApplicationStarted As CancellationToken Implements IHostApplicationLifetime.ApplicationStarted
        Get
            Return CancellationToken.None
        End Get
    End Property

    Public ReadOnly Property ApplicationStopping As CancellationToken Implements IHostApplicationLifetime.ApplicationStopping
        Get
            Return _stopping.Token
        End Get
    End Property

    Public ReadOnly Property ApplicationStopped As CancellationToken Implements IHostApplicationLifetime.ApplicationStopped
        Get
            Return CancellationToken.None
        End Get
    End Property

    Public Sub StopApplication() Implements IHostApplicationLifetime.StopApplication
    End Sub

    ''' <summary>Signals <see cref="ApplicationStopping"/>, as the real host does when shutdown begins.</summary>
    Friend Sub SignalStopping()
        _stopping.Cancel()
    End Sub

End Class

''' <summary>An <see cref="IScriptOutput"/> that remembers everything written to it.</summary>
Friend NotInheritable Class RecordingScriptOutput
    Implements IScriptOutput

    Private ReadOnly _out As New StringWriter(Globalization.CultureInfo.InvariantCulture)
    Private ReadOnly _error As New StringWriter(Globalization.CultureInfo.InvariantCulture)

    Public ReadOnly Property Out As TextWriter Implements IScriptOutput.Out
        Get
            Return _out
        End Get
    End Property

    Public ReadOnly Property [Error] As TextWriter Implements IScriptOutput.Error
        Get
            Return _error
        End Get
    End Property

    Friend ReadOnly Property OutText As String
        Get
            Return _out.ToString()
        End Get
    End Property

    Friend ReadOnly Property ErrorText As String
        Get
            Return _error.ToString()
        End Get
    End Property

End Class

''' <summary>
''' A <see cref="VBScriptGlobals"/> subclass whose <c>Print</c> override records the values it saw. The runner
''' instantiated it through <see cref="VBScriptOptions.GlobalsType"/>, so its <see cref="IScriptOutput"/> constructor
''' gets the per-call output the runner created (test-plan T-RUN-10).
''' <para>
''' Public on purpose: the script compiles into its own submission assembly, so a <c>Friend</c> globals type would be
''' rejected with <c>TypeAccessException</c> when the generated code touches it.
''' </para>
''' </summary>
Public NotInheritable Class RecordingGlobals
    Inherits VBScriptGlobals

    Private Shared ReadOnly _sync As New Object()
    Private Shared ReadOnly _printed As New List(Of Object)()

    Public Sub New(output As IScriptOutput)
        MyBase.New(output)
    End Sub

    Public Overrides Sub Print(value As Object)
        SyncLock _sync
            _printed.Add(value)
        End SyncLock

        MyBase.Print(value)
    End Sub

    Friend Shared ReadOnly Property Printed As IReadOnlyList(Of Object)
        Get
            SyncLock _sync
                Return _printed.ToArray()
            End SyncLock
        End Get
    End Property

    Friend Shared Sub ClearPrinted()
        SyncLock _sync
            _printed.Clear()
        End SyncLock
    End Sub

End Class

''' <summary>
''' Globals whose <c>Print</c> blocks until the test releases it. Used to hold a runner inside a call so the
''' re-entry guard and mid-run cancellation can be exercised without <see cref="Thread.Sleep"/> timing assertions.
''' Public for the same reason as <see cref="RecordingGlobals"/>: the submission assembly must be able to see it.
''' </summary>
Public NotInheritable Class BlockingGlobals
    Inherits VBScriptGlobals

    Private ReadOnly _onEntered As Action
    Private ReadOnly _gate As ManualResetEventSlim

    Public Sub New(output As IScriptOutput, onEntered As Action, gate As ManualResetEventSlim)
        MyBase.New(output)
        _onEntered = onEntered
        _gate = gate
    End Sub

    Public Overrides Sub Print(value As Object)
        _onEntered.Invoke()

        ' Bounded so a broken test fails instead of hanging the run.
        _gate.Wait(TimeSpan.FromSeconds(30))

        MyBase.Print(value)
    End Sub

End Class

''' <summary>
''' One container per test: the facade's DI registration plus the pieces a bare <see cref="IServiceCollection"/> does
''' not provide (logging and a host lifetime). The output is registered before <c>AddVBScripting</c>, which is the
''' T-DI-1 shape (user registration wins) and lets tests observe routing.
''' </summary>
Friend NotInheritable Class RunnerFixture
    Implements IDisposable

    Friend ReadOnly Provider As ServiceProvider
    Friend ReadOnly Lifetime As FakeHostApplicationLifetime
    Friend ReadOnly Output As RecordingScriptOutput

    Friend Sub New(Optional configure As Action(Of VBScriptOptions) = Nothing, Optional scriptOutput As IScriptOutput = Nothing, Optional afterRegistration As Action(Of IServiceCollection) = Nothing)
        Lifetime = New FakeHostApplicationLifetime()
        Me.Output = New RecordingScriptOutput()

        Dim effectiveOutput As IScriptOutput = scriptOutput
        If effectiveOutput Is Nothing Then
            effectiveOutput = Me.Output
        End If

        Dim services As New ServiceCollection()
        services.AddLogging()
        services.AddSingleton(Of IHostApplicationLifetime)(Lifetime)
        services.AddSingleton(Of IScriptOutput)(effectiveOutput)
        services.AddVBScripting(configure)

        If afterRegistration IsNot Nothing Then
            afterRegistration(services)
        End If

        Provider = services.BuildServiceProvider()
    End Sub

    Friend ReadOnly Property Runner As IVBScriptRunner
        Get
            Return Provider.GetRequiredService(Of IVBScriptRunner)()
        End Get
    End Property

    Public Sub Dispose() Implements IDisposable.Dispose
        Provider.Dispose()
    End Sub

End Class
