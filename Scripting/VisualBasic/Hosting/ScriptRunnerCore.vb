' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.Text

Namespace Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

    ''' <summary>
    ''' Input of one file-script execution (<c>.vbx</c> or <c>/check</c>) or one session submission.
    ''' The host (vbi today, the hosting facade later) fills it; the core owns the execution semantics so
    ''' there is exactly one script execution path in the product.
    ''' </summary>
    Friend NotInheritable Class ScriptRunRequest

        ''' <summary>The script compiler that creates submissions (the VB compiler).</summary>
        Public Property Compiler As VisualBasicScriptCompiler

        ''' <summary>The source text to compile/run.</summary>
        Public Property Source As SourceText

        ''' <summary>Path of <see cref="Source"/>, used by <c>#Load</c> and diagnostics.</summary>
        Public Property FilePath As String

        ''' <summary>Options the submission is compiled with. Nothing is not supported.</summary>
        Public Property ScriptOptions As ScriptOptions

        ''' <summary>Globals instance handed to the script. Must be non-null.</summary>
        Public Property Globals As Object

        ''' <summary>Assembly loader for this execution (or this session). Owned by the caller.</summary>
        Public Property AssemblyLoader As InteractiveAssemblyLoader

        ''' <summary>Compile only (<c>/check</c>); do not run.</summary>
        Public Property CompileOnly As Boolean

        ''' <summary>
        ''' Base directory for <c>#Load</c> and relative references. vbi passes the compiler's
        ''' base directory; a host passes its working directory.
        ''' </summary>
        Public Property BaseDirectory As String

        ''' <summary>Optional NuGet pre-scan/restore seam; Nothing means "no restore".</summary>
        Public Property RestoreCoordinator As IScriptRestoreCoordinator

        ''' <summary>
        ''' Optional sink for pre-scan diagnostics. It must print them in the caller's own output channel and
        ''' return whether any of them is an error. The session calls it before compiling, so restore
        ''' diagnostics keep their original position in the console stream (this is what keeps vbi
        ''' byte-identical). When Nothing, the diagnostics stay in the submission outcome instead.
        ''' </summary>
        Public Property RestoreDiagnosticsReporter As Func(Of ImmutableArray(Of Diagnostic), Boolean)

    End Class

    ''' <summary>Result of one <see cref="ScriptRunnerCore.RunAsync"/> call.</summary>
    Friend NotInheritable Class ScriptRunOutcome

        ''' <summary>Process exit code: <c>Return</c> value, 1 for diagnostics, or an exception HRESULT.</summary>
        Public Property ExitCode As Integer

        ''' <summary>Diagnostics the caller still has to report (compile diagnostics or run-time compile errors).</summary>
        Public Property Diagnostics As ImmutableArray(Of Diagnostic)

        ''' <summary>
        ''' Diagnostics of the NuGet pre-scan. They were already reported through
        ''' <see cref="ScriptRunRequest.RestoreDiagnosticsReporter"/> when one was supplied.
        ''' </summary>
        Public Property RestoreDiagnostics As ImmutableArray(Of Diagnostic)

        ''' <summary>
        ''' The value the script reported (<c>Return</c>), or Nothing when it did not return a value. This is the
        ''' reported value, not the session's raw <see cref="ScriptSubmissionOutcome.ReturnValue"/>, so "no Return"
        ''' stays distinguishable from "Return 0".
        ''' </summary>
        Public Property ReturnValue As Object

        ''' <summary>The run-time exception, when the script threw. The caller formats it and uses ExitCode.</summary>
        Public Property Exception As Exception

    End Class

    ''' <summary>
    ''' Single implementation of the script execution pipeline: optional NuGet pre-scan, compile-only
    ''' (<c>/check</c>) or run, and the diagnostic/exception to exit-code mapping.
    ''' <para>
    ''' The one-shot shape (<see cref="RunAsync"/>) is literally "create a session, submit once, dispose";
    ''' the session shape (<see cref="CreateSession(Of TReturn)"/>) is the same submission code without the
    ''' dispose. The CLI (vbi) and the hosting facade are thin adapters over this type.
    ''' </para>
    ''' <para>
    ''' This engine deliberately lives in the VB scripting assembly, not in the shared
    ''' <c>Microsoft.CodeAnalysis.Scripting</c> assembly: that assembly is public scripting infrastructure
    ''' that other processes/consumers combine with Roslyn's own APIs, so this repository's product features
    ''' must not be carried there. The engine can sit here because VB scripting already owns the VB script
    ''' compiler and has the compiler's internals via InternalsVisibleTo; the shared scripting assembly stays
    ''' upstream-shaped.
    ''' </para>
    ''' </summary>
    Friend NotInheritable Class ScriptRunnerCore

        Public Shared ReadOnly Instance As New ScriptRunnerCore()

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Runs one file script or one <c>/check</c> compilation. There is no second pipeline here: this
        ''' creates a one-submission session of <see cref="Integer"/> (the return type vbi's file scripts have
        ''' always used), submits once, disposes it, and maps the submission outcome to an exit code.
        ''' </summary>
        Public Async Function RunAsync(request As ScriptRunRequest, cancellationToken As CancellationToken) As Task(Of ScriptRunOutcome)
            ValidateSessionRequest(request)

            Dim session As New ScriptSessionCore(Of Integer)(request)
            Try
                Dim submission = Await session.SubmitAsync(request.Source, cancellationToken).ConfigureAwait(False)
                Return MapSubmissionOutcome(request, submission)
            Finally
                session.Dispose()
            End Try
        End Function

        ''' <summary>
        ''' Creates a stateful session over the same submission implementation. The caller owns the
        ''' returned instance and the assembly loader it passed in. The default (<see cref="Object"/>)
        ''' shape is the one REPL-like hosts and the public session API use; vbi's one-shot path asks for
        ''' <see cref="Integer"/> because its file scripts have always been compiled with that return type.
        ''' </summary>
        Public Function CreateSession(request As ScriptRunRequest) As ScriptSessionCore(Of Object)
            Return CreateSession(Of Object)(request)
        End Function

        Public Function CreateSession(Of TReturn)(request As ScriptRunRequest) As ScriptSessionCore(Of TReturn)
            ValidateSessionRequest(request)
            Return New ScriptSessionCore(Of TReturn)(request)
        End Function

        ''' <summary>
        ''' Maps one submission to the one-shot outcome without reformatting anything: restore diagnostics
        ''' were already printed by the reporter, compile diagnostics are handed to the caller for
        ''' reporting, a run keeps its <c>Return</c> value (as the reported value, Nothing when the script did not
        ''' return one) or its exception HRESULT. Compile warnings from a successful run are not surfaced (the
        ''' historical <c>RunScriptAsync</c> never printed them); <c>/check</c> keeps reporting them because it
        ''' always did.
        ''' </summary>
        Private Shared Function MapSubmissionOutcome(request As ScriptRunRequest, submission As ScriptSubmissionOutcome) As ScriptRunOutcome
            If submission.RestoreBlocked Then
                ' The reporter printed them when the host supplied one; otherwise they are still data.
                Return New ScriptRunOutcome With {
                    .ExitCode = 1,
                    .Diagnostics = If(
                        request.RestoreDiagnosticsReporter IsNot Nothing,
                        ImmutableArray(Of Diagnostic).Empty,
                        submission.RestoreDiagnostics),
                    .RestoreDiagnostics = submission.RestoreDiagnostics
                }
            End If

            If submission.Diagnostics.HasAnyErrors() Then
                Return New ScriptRunOutcome With {
                    .ExitCode = 1,
                    .Diagnostics = submission.Diagnostics,
                    .RestoreDiagnostics = submission.RestoreDiagnostics
                }
            End If

            If request.CompileOnly Then
                ' /check: warnings and exit code 0 on success.
                Return New ScriptRunOutcome With {
                    .ExitCode = 0,
                    .Diagnostics = submission.Diagnostics,
                    .RestoreDiagnostics = submission.RestoreDiagnostics
                }
            End If

            If submission.Exception IsNot Nothing Then
                Return New ScriptRunOutcome With {
                    .ExitCode = submission.Exception.HResult,
                    .Exception = submission.Exception,
                    .RestoreDiagnostics = submission.RestoreDiagnostics
                }
            End If

            ' ExitCode keeps using the raw Return value (vbi's contract: 0 when absent, else the value).
            ' ReturnValue is the reported value, not the raw one: the int-returning one-shot session always
            ' boxes 0, so the raw value would make "no Return" indistinguishable from "Return 0".
            ' PrintedValue follows the HasReturnValue rule (Nothing when the script did not return a value).
            Return New ScriptRunOutcome With {
                .ExitCode = If(submission.ReturnValue Is Nothing, 0, CInt(submission.ReturnValue)),
                .ReturnValue = submission.PrintedValue,
                .RestoreDiagnostics = submission.RestoreDiagnostics
            }
        End Function

        Private Shared Sub ValidateSessionRequest(request As ScriptRunRequest)
            If request Is Nothing Then
                Throw New ArgumentNullException(NameOf(request))
            End If

            If request.Compiler Is Nothing Then
                Throw New ArgumentNullException(NameOf(request.Compiler))
            End If

            If request.ScriptOptions Is Nothing Then
                Throw New ArgumentNullException(NameOf(request.ScriptOptions))
            End If

            If request.Globals Is Nothing Then
                Throw New ArgumentNullException(NameOf(request.Globals))
            End If
        End Sub

    End Class

    ''' <summary>Result of one <c>ScriptSessionCore(Of TReturn).SubmitAsync</c> call.</summary>
    Friend NotInheritable Class ScriptSubmissionOutcome

        ''' <summary>Diagnostics for this submission (compile diagnostics, or the pre-scan diagnostics when it blocked).</summary>
        Public Property Diagnostics As ImmutableArray(Of Diagnostic)

        ''' <summary>Raw pre-scan diagnostics, whether or not they blocked the submission.</summary>
        Public Property RestoreDiagnostics As ImmutableArray(Of Diagnostic)

        ''' <summary>True when the pre-scan found errors, so compilation/execution never ran.</summary>
        Public Property RestoreBlocked As Boolean

        Public Property ReturnValue As Object

        ''' <summary>
        ''' The value vbi would have printed for this submission (<see cref="Script.HasReturnValue"/> rule),
        ''' or Nothing when the submission did not return a value / failed.
        ''' </summary>
        Public Property PrintedValue As Object

        Public Property Exception As Exception

        Public Property IsFaulted As Boolean

    End Class

    ''' <summary>
    ''' Stateful submission chain over the script core. <typeparamref name="TReturn"/> is the return type of
    ''' the <b>initial</b> submission only (the script chain's first <see cref="Script(Of TReturn)"/>);
    ''' continuations are the ordinary object-returning submissions, as in the REPL.
    ''' </summary>
    Friend NotInheritable Class ScriptSessionCore(Of TReturn)
        Implements IDisposable

        Private ReadOnly _request As ScriptRunRequest
        Private ReadOnly _initialOptions As ScriptOptions
        Private ReadOnly _globals As Object
        Private _options As ScriptOptions
        Private _state As ScriptState
        Private _busy As Integer
        Private _faulted As Boolean
        Private _disposed As Boolean

        Friend Sub New(request As ScriptRunRequest)
            If request.Compiler Is Nothing Then
                Throw New ArgumentNullException(NameOf(request.Compiler))
            End If

            If request.ScriptOptions Is Nothing Then
                Throw New ArgumentNullException(NameOf(request.ScriptOptions))
            End If

            If request.Globals Is Nothing Then
                Throw New ArgumentNullException(NameOf(request.Globals))
            End If

            _request = request
            _globals = request.Globals
            _initialOptions = request.ScriptOptions
            _options = request.ScriptOptions
        End Sub

        Public ReadOnly Property Variables As ImmutableArray(Of ScriptVariable)
            Get
                Return If(_state Is Nothing, ImmutableArray(Of ScriptVariable).Empty, _state.Variables)
            End Get
        End Property

        Public Function GetVariable(name As String) As ScriptVariable
            Return If(_state Is Nothing, Nothing, _state.GetVariable(name))
        End Function

        Public Function SubmitAsync(code As String, cancellationToken As CancellationToken) As Task(Of ScriptSubmissionOutcome)
            Return SubmitAsync(SourceText.From(If(code, String.Empty)), cancellationToken)
        End Function

        Public Async Function SubmitAsync(code As SourceText, cancellationToken As CancellationToken) As Task(Of ScriptSubmissionOutcome)
            If _disposed Then
                Throw New ObjectDisposedException("ScriptSessionCore")
            End If

            If _faulted Then
                Throw New InvalidOperationException("The session is faulted. Reset or dispose it before submitting again.")
            End If

            If Interlocked.CompareExchange(_busy, 1, 0) <> 0 Then
                Throw New InvalidOperationException("The session is already running a submission.")
            End If

            Try
                Dim restoreDiagnostics As ImmutableArray(Of Diagnostic) = ImmutableArray(Of Diagnostic).Empty
                If code IsNot Nothing AndAlso _request.RestoreCoordinator IsNot Nothing Then
                    restoreDiagnostics = Await _request.RestoreCoordinator.PrepareCompilationAsync(
                        code,
                        _options.FilePath,
                        _options,
                        cancellationToken).ConfigureAwait(False)

                    If Not restoreDiagnostics.IsEmpty Then
                        ' Reporting (or not) is the host's job; the session only decides whether to block.
                        Dim hasErrors = If(
                            _request.RestoreDiagnosticsReporter IsNot Nothing,
                            _request.RestoreDiagnosticsReporter(restoreDiagnostics),
                            restoreDiagnostics.HasAnyErrors())

                        If hasErrors Then
                            ' A failed pre-scan blocks this submission but does not kill the session.
                            Return New ScriptSubmissionOutcome With {
                                .Diagnostics = restoreDiagnostics,
                                .RestoreDiagnostics = restoreDiagnostics,
                                .RestoreBlocked = True
                            }
                        End If
                    End If
                End If

                Dim script As Script
                If _state Is Nothing Then
                    script = Script.CreateInitialScript(Of TReturn)(
                        _request.Compiler,
                        code,
                        _options,
                        _globals.GetType(),
                        assemblyLoaderOpt:=_request.AssemblyLoader)
                Else
                    script = _state.Script.ContinueWith(code.ToString(), _options)
                End If

                Dim diagnostics = script.Compile(cancellationToken)

                ' Compile errors do not kill the session and do not refresh its options.
                If diagnostics.HasAnyErrors() Then
                    Return New ScriptSubmissionOutcome With {
                        .Diagnostics = diagnostics,
                        .RestoreDiagnostics = restoreDiagnostics
                    }
                End If

                If _request.CompileOnly Then
                    ' /check: compile only; no state advance, no options refresh.
                    Return New ScriptSubmissionOutcome With {
                        .Diagnostics = diagnostics,
                        .RestoreDiagnostics = restoreDiagnostics
                    }
                End If

                Dim newState As ScriptState
                Try
                    If _state Is Nothing Then
                        newState = Await script.RunAsync(_globals, Function(e) True, cancellationToken).ConfigureAwait(False)
                    Else
                        newState = Await script.RunFromAsync(_state, Function(e) True, cancellationToken).ConfigureAwait(False)
                    End If
                Catch ex As OperationCanceledException
                    ' Cancellation leaves the session faulted: only Reset/Dispose are allowed afterwards.
                    _faulted = True
                    Throw
                End Try

                _state = newState
                Dim printedValue As Object = Nothing
                If newState.Exception Is Nothing AndAlso script.HasReturnValue() Then
                    printedValue = newState.ReturnValue
                End If
                RefreshOptions(newState)

                Return New ScriptSubmissionOutcome With {
                    .Diagnostics = diagnostics,
                    .RestoreDiagnostics = restoreDiagnostics,
                    .ReturnValue = newState.ReturnValue,
                    .PrintedValue = printedValue,
                    .Exception = newState.Exception
                }
            Finally
                Volatile.Write(_busy, 0)
            End Try
        End Function

        ''' <summary>
        ''' Applies the REPL's imports/references discipline to the next submission: the imports of
        ''' every previous submission are replayed into the options while references and imports that were
        ''' already applied are removed, and both resolvers are re-based on
        ''' <see cref="ScriptRunRequest.BaseDirectory"/> (never the process current directory).
        ''' <para>
        ''' The replay calls the compiler's existing <c>AddPreviousSubmissionImports</c> instead of adding a
        ''' seam to the shared scripting assembly: the import walk is a VB compiler concern, so it stays in the
        ''' VB compiler and the engine calls it directly.
        ''' </para>
        ''' </summary>
        Private Sub RefreshOptions(state As ScriptState)
            Dim importNames As New List(Of String)()

            VisualBasicScriptCompiler.AddPreviousSubmissionImports(
                state.Script,
                importNames,
                New HashSet(Of String)(StringComparer.OrdinalIgnoreCase))

            Dim options = _options.RemoveImportsAndReferences().WithImports(importNames.ToImmutableArray())

            Dim metadataResolver = TryCast(_options.MetadataResolver, RuntimeMetadataReferenceResolver)
            If metadataResolver IsNot Nothing AndAlso metadataResolver.PathResolver IsNot Nothing Then
                options = options.WithMetadataResolver(
                    metadataResolver.WithRelativePathResolver(
                        metadataResolver.PathResolver.WithBaseDirectory(_request.BaseDirectory)))
            End If

            Dim sourceResolver = TryCast(_options.SourceResolver, CommonCompiler.LoggingSourceFileResolver)
            If sourceResolver IsNot Nothing Then
                options = options.WithSourceResolver(sourceResolver.WithBaseDirectory(_request.BaseDirectory))
            End If

            _options = options
        End Sub

        ''' <summary>
        ''' Clears the submission chain and restores the initial options; globals stay the same instance.
        ''' The assembly loader is owned by the caller and is not replaced here.
        ''' </summary>
        Public Sub Reset()
            _state = Nothing
            _options = _initialOptions
            _faulted = False
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            _disposed = True
            _state = Nothing
        End Sub

    End Class

End Namespace
