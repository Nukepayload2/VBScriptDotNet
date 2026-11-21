' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting
Imports Microsoft.Extensions.Hosting

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' The stateful session over <c>ScriptSessionCore(Of Object)</c>. It owns only the host-side pieces the core
    ''' deliberately leaves to its caller: the per-session assembly loader, the re-entry guard, the per-submission
    ''' lifetime link, the faulted gate and the public result/variable mapping. Compilation, option refresh, imports
    ''' replay and the diagnostic/exception mapping stay in the core, exactly as they do for vbi and the one-shot
    ''' runner, so there is one execution path.
    ''' </summary>
    Friend NotInheritable Class VBScriptSession
        Implements IVBScriptSession

        Private ReadOnly _lifetime As IHostApplicationLifetime
        Private ReadOnly _restoreService As INuGetRestoreService
        Private ReadOnly _packageResolver As INuGetPackageResolver
        Private ReadOnly _initialOptions As VBScriptOptions
        Private ReadOnly _globals As Object
        Private ReadOnly _disposeCancellation As New CancellationTokenSource()
        Private ReadOnly _gate As New Object()

        Private _core As ScriptSessionCore(Of Object)
        Private _loader As InteractiveAssemblyLoader
        Private _busy As Integer
        Private _faulted As Boolean
        Private _disposed As Boolean
        Private _idle As TaskCompletionSource(Of Boolean)

        Friend Sub New(lifetime As IHostApplicationLifetime, output As IScriptOutput, restoreService As INuGetRestoreService, packageResolver As INuGetPackageResolver, options As VBScriptOptions)
            If lifetime Is Nothing Then
                Throw New ArgumentNullException(NameOf(lifetime))
            End If

            If output Is Nothing Then
                Throw New ArgumentNullException(NameOf(output))
            End If

            If restoreService Is Nothing Then
                Throw New ArgumentNullException(NameOf(restoreService))
            End If

            If packageResolver Is Nothing Then
                Throw New ArgumentNullException(NameOf(packageResolver))
            End If

            If options Is Nothing Then
                Throw New ArgumentNullException(NameOf(options))
            End If

            _lifetime = lifetime
            _restoreService = restoreService
            _packageResolver = packageResolver

            ' The session owns a copy with the vbi default set applied (the caller's object is never mutated), and
            ' resolves the globals instance once: the very same instance is reused across submissions and across
            ' Reset. ScriptArguments are appended to that instance here, once.
            _initialOptions = VBScriptHostInternals.CreateEffectiveOptions(options)
            _globals = VBScriptHostInternals.ResolveGlobals(_initialOptions, globals:=Nothing, defaultOutput:=output)

            _loader = New InteractiveAssemblyLoader()
            _core = CreateCore()
        End Sub

        ''' <summary>
        ''' Builds a core session over the initial options snapshot, the retained globals instance and the current
        ''' loader. Only the loader and the core instance change on Reset; the options snapshot and globals do not.
        ''' </summary>
        Private Function CreateCore() As ScriptSessionCore(Of Object)
            Dim request = VBScriptHostInternals.CreateRunRequest(
                _initialOptions,
                SourceText.From(String.Empty),
                filePath:=Nothing,
                globals:=_globals,
                assemblyLoader:=_loader,
                compileOnly:=False,
                restoreCoordinator:=New NuGetRestoreCoordinatorAdapter(_restoreService, _initialOptions, _loader),
                packageResolver:=New NuGetPackageResolverAdapter(_packageResolver))

            ' Restore diagnostics are data in the session result; the session never prints them to IScriptOutput.
            request.RestoreDiagnosticsReporter = Function(diagnostics) VBScriptHostInternals.HasErrors(diagnostics)

            Return ScriptRunnerCore.Instance.CreateSession(Of Object)(request)
        End Function

        Public Async Function SubmitAsync(code As String, Optional cancellationToken As CancellationToken = Nothing) As Task(Of VBScriptSubmissionResult) Implements IVBScriptSession.SubmitAsync
            ' The host stopping is checked per submission, not once when the session was created.
            ThrowIfHostStopping()
            EnterSubmission()

            Dim linked As CancellationTokenSource = Nothing
            Try
                linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.ApplicationStopping, _disposeCancellation.Token)

                Try
                    Dim outcome = Await _core.SubmitAsync(code, linked.Token).ConfigureAwait(False)

                    ' Cancellation that lands while the script runs wins over the script's own result, exactly as it
                    ' does for the one-shot runner. It faults the session instead of throwing.
                    linked.Token.ThrowIfCancellationRequested()

                    Return MapOutcome(outcome)
                Catch ex As OperationCanceledException
                    Fault()
                    Return New VBScriptSubmissionResult(ImmutableArray(Of VBScriptDiagnostic).Empty, Nothing, Nothing, Nothing, isFaulted:=True)
                End Try
            Finally
                If linked IsNot Nothing Then
                    linked.Dispose()
                End If

                ExitSubmission()
            End Try
        End Function

        ''' <summary>
        ''' The three-state classification algorithm. The compiler's own <c>IsCompleteSubmission</c> is the
        ''' discriminator between "needs more lines" and "invalid": it returns false for the continuation shapes
        ''' (missing <c>End If</c>/<c>End Region</c>, a trailing line continuation, an incomplete member such as
        ''' <c>Dim z =</c>, a missing last token) and true for plain syntax errors such as <c>Dim x = )</c>.
        ''' </summary>
        Public Function IsCompleteSubmission(code As String) As VBScriptSubmissionState Implements IVBScriptSession.IsCompleteSubmission
            Dim compiler = VisualBasicScriptCompiler.Instance
            Dim tree = compiler.ParseSubmissionForHost(SourceText.From(If(code, String.Empty)), parseOptions:=Nothing, CancellationToken.None)

            Dim hasDiagnostics = False
            For Each diagnostic In tree.GetDiagnostics()
                hasDiagnostics = True
                Exit For
            Next

            If Not compiler.IsCompleteSubmissionForHost(tree) Then
                Return VBScriptSubmissionState.Incomplete
            End If

            Return If(hasDiagnostics, VBScriptSubmissionState.Invalid, VBScriptSubmissionState.Complete)
        End Function

#If NET Then
        ' Both trim/AOT annotations can only land on the accessor: neither is a property-level attribute
        ' (annotating the property itself reports BC30662), and an accessor attribute must sit on the same
        ' logical line as Get, so the explicit line continuation attaches them there. net472 has neither
        ' attribute and takes the #Else branch below.
        Public ReadOnly Property Variables As ImmutableArray(Of VBScriptVariable) Implements IVBScriptSession.Variables
            <System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Reads script variables through reflection over dynamically compiled submissions.")> _
            <System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Reads script variables from dynamically compiled submissions, which is not available in AOT-only applications.")> _
            Get
                Return SnapshotVariables()
            End Get
        End Property
#Else
        Public ReadOnly Property Variables As ImmutableArray(Of VBScriptVariable) Implements IVBScriptSession.Variables
            Get
                Return SnapshotVariables()
            End Get
        End Property
#End If

        ''' <summary>
        ''' De-duplicates the core's variables by name with the last declaration winning, while keeping the
        ''' position of the first declaration stable. <c>ScriptState.Variables</c> already excludes the globals object
        ''' (slot #0).
        ''' </summary>
        Private Function SnapshotVariables() As ImmutableArray(Of VBScriptVariable)
            Dim core = _core
            If core Is Nothing Then
                Return ImmutableArray(Of VBScriptVariable).Empty
            End If

            Dim mapped As New List(Of VBScriptVariable)()
            Dim positions As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)

            For Each variable In core.Variables
                Dim snapshot As New VBScriptVariable(variable.Name, GetTypeName(variable.Type), variable.IsReadOnly, variable.Value)
                Dim index As Integer
                If positions.TryGetValue(variable.Name, index) Then
                    mapped(index) = snapshot
                Else
                    positions.Add(variable.Name, mapped.Count)
                    mapped.Add(snapshot)
                End If
            Next

            Return mapped.ToImmutableArray()
        End Function

        Public Function GetVariable(name As String) As VBScriptVariable Implements IVBScriptSession.GetVariable
            If name Is Nothing Then
                Throw New ArgumentNullException(NameOf(name))
            End If

            For Each variable In Variables
                If String.Equals(variable.Name, name, StringComparison.OrdinalIgnoreCase) Then
                    Return variable
                End If
            Next

            Return Nothing
        End Function

        ''' <summary>
        ''' Clears the chain and restores the initial snapshot. The globals instance is the same object, and a
        ''' fresh loader is created so the next submissions cannot resolve against assemblies loaded by the cleared
        ''' chain.
        ''' </summary>
        Public Sub Reset() Implements IVBScriptSession.Reset
            EnterExclusiveForReset()

            Try
                Dim previousCore = _core
                Dim previousLoader = _loader
                Dim nextLoader As New InteractiveAssemblyLoader()

                _loader = nextLoader
                Dim nextCore As ScriptSessionCore(Of Object)
                Try
                    nextCore = CreateCore()
                Catch
                    _loader = previousLoader
                    nextLoader.Dispose()
                    Throw
                End Try

                _core = nextCore
                previousCore.Dispose()
                previousLoader.Dispose()

                SyncLock _gate
                    _faulted = False
                End SyncLock
            Finally
                ExitSubmission()
            End Try
        End Sub

        ''' <summary>
        ''' Cancels the in-flight submission (through the linked token), waits until it has observed the cancellation,
        ''' then releases the loader and the core. Idempotent.
        ''' </summary>
        Public Function DisposeAsync() As ValueTask Implements IVBScriptSession.DisposeAsync, IAsyncDisposable.DisposeAsync
            Return New ValueTask(DisposeCoreAsync())
        End Function

        Private Async Function DisposeCoreAsync() As Task
            Dim idle As Task = Nothing

            SyncLock _gate
                If _disposed Then
                    Return
                End If

                _disposed = True
                If _busy <> 0 Then
                    _idle = New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
                    idle = _idle.Task
                End If
            End SyncLock

            _disposeCancellation.Cancel()

            If idle IsNot Nothing Then
                Await idle.ConfigureAwait(False)
            End If

            _core.Dispose()
            _loader.Dispose()
            _disposeCancellation.Dispose()
        End Function

        Private Sub EnterSubmission()
            SyncLock _gate
                If _disposed Then
                    Throw New VBScriptHostException(
                        "The VBScript session has been disposed, so it no longer accepts submissions. Create a new " &
                        "session with IVBScriptSessionFactory.CreateSession.")
                End If

                If _faulted Then
                    Throw New VBScriptHostException(
                        "The VBScript session is faulted because its last submission was cancelled. Cancellation leaves " &
                        "half-applied state behind, so only Reset() (which restores the initial options and creates a " &
                        "fresh assembly loader) or DisposeAsync() is allowed; the globals instance is kept by Reset.")
                End If

                If _busy <> 0 Then
                    Throw New VBScriptBusyException()
                End If

                _busy = 1
            End SyncLock
        End Sub

        Private Sub EnterExclusiveForReset()
            SyncLock _gate
                If _disposed Then
                    Throw New VBScriptHostException(
                        "The VBScript session has been disposed, so it cannot be reset. Create a new session with " &
                        "IVBScriptSessionFactory.CreateSession.")
                End If

                If _busy <> 0 Then
                    Throw New VBScriptBusyException()
                End If

                _busy = 1
            End SyncLock
        End Sub

        Private Sub ExitSubmission()
            Dim idle As TaskCompletionSource(Of Boolean) = Nothing

            SyncLock _gate
                _busy = 0
                idle = _idle
                _idle = Nothing
            End SyncLock

            If idle IsNot Nothing Then
                idle.TrySetResult(True)
            End If
        End Sub

        Private Sub Fault()
            SyncLock _gate
                _faulted = True
            End SyncLock
        End Sub

        Private Sub ThrowIfHostStopping()
            If Not _lifetime.ApplicationStopping.IsCancellationRequested Then
                Return
            End If

            Throw New VBScriptHostException(
                "The host is stopping (IHostApplicationLifetime.ApplicationStopping was signalled), so no submission " &
                "can be started. Stop issuing session submissions before the host begins its shutdown; an in-flight " &
                "submission is cancelled by the same token.")
        End Sub

        Private Shared Function MapOutcome(outcome As ScriptSubmissionOutcome) As VBScriptSubmissionResult
            Dim diagnostics = VBScriptHostInternals.MergeDiagnostics(
                VBScriptHostInternals.MapDiagnostics(outcome.Diagnostics),
                VBScriptHostInternals.MapDiagnostics(outcome.RestoreDiagnostics))

            ' ReturnValue is the raw script return (Nothing when there was none); PrintedValue is the line vbi would
            ' have printed (the HasReturnValue rule). A run-time exception keeps IsFaulted false: the session
            ' stays usable.
            Return New VBScriptSubmissionResult(
                diagnostics,
                outcome.ReturnValue,
                outcome.PrintedValue,
                outcome.Exception,
                isFaulted:=False)
        End Function

        Private Shared Function GetTypeName(type As Type) As String
            If type Is Nothing Then
                Return String.Empty
            End If

            Return If(type.FullName, type.Name)
        End Function

    End Class

End Namespace
