' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Generic
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' The DI-backed <see cref="IVBScriptRunner"/>. It is a thin adapter: options and request construction, the
    ''' per-call assembly loader, the re-entry guard, cancellation and the result mapping. The execution semantics
    ''' live in <c>ScriptRunnerCore</c>/<c>ScriptSessionCore</c>, exactly as they do for vbi, so there is only one
    ''' script execution path in the product.
    ''' </summary>
    Friend NotInheritable Class VBScriptRunner
        Implements IVBScriptRunner

        Private ReadOnly _options As IOptions(Of VBScriptOptions)
        Private ReadOnly _output As IScriptOutput
        Private ReadOnly _lifetime As IHostApplicationLifetime
        Private ReadOnly _logger As ILogger
        Private ReadOnly _restoreService As INuGetRestoreService
        Private ReadOnly _packageResolver As INuGetPackageResolver
        Private _busy As Integer

        Public Sub New(options As IOptions(Of VBScriptOptions), output As IScriptOutput, lifetime As IHostApplicationLifetime, logger As ILogger(Of VBScriptRunner), restoreService As INuGetRestoreService, packageResolver As INuGetPackageResolver)
            If options Is Nothing Then
                Throw New ArgumentNullException(NameOf(options))
            End If

            If output Is Nothing Then
                Throw New ArgumentNullException(NameOf(output))
            End If

            If lifetime Is Nothing Then
                Throw New ArgumentNullException(NameOf(lifetime))
            End If

            If logger Is Nothing Then
                Throw New ArgumentNullException(NameOf(logger))
            End If

            If restoreService Is Nothing Then
                Throw New ArgumentNullException(NameOf(restoreService))
            End If

            If packageResolver Is Nothing Then
                Throw New ArgumentNullException(NameOf(packageResolver))
            End If

            ValidateNuGetRestoreConfiguration(restoreService, packageResolver)

            _options = options
            _output = output
            _lifetime = lifetime
            _logger = logger
            _restoreService = restoreService
            _packageResolver = packageResolver

            WarnWhenDesktopReferencesAreIgnored()
        End Sub

        Public Function RunFileAsync(path As String, Optional arguments As IReadOnlyList(Of String) = Nothing, Optional cancellationToken As CancellationToken = Nothing) As Task(Of VBScriptResult) Implements IVBScriptRunner.RunFileAsync
            Return RunFileCoreAsync(path, arguments, compileOnly:=False, cancellationToken:=cancellationToken)
        End Function

        Public Function RunCodeAsync(code As String, Optional cancellationToken As CancellationToken = Nothing) As Task(Of VBScriptResult) Implements IVBScriptRunner.RunCodeAsync
            Return RunCodeCoreAsync(code, compileOnly:=False, cancellationToken:=cancellationToken)
        End Function

        Public Function CompileFileAsync(path As String, Optional cancellationToken As CancellationToken = Nothing) As Task(Of VBScriptResult) Implements IVBScriptRunner.CompileFileAsync
            Return RunFileCoreAsync(path, arguments:=Nothing, compileOnly:=True, cancellationToken:=cancellationToken)
        End Function

        Public Function CompileCodeAsync(code As String, Optional cancellationToken As CancellationToken = Nothing) As Task(Of VBScriptResult) Implements IVBScriptRunner.CompileCodeAsync
            Return RunCodeCoreAsync(code, compileOnly:=True, cancellationToken:=cancellationToken)
        End Function

        Public Function EvaluateAsync(Of T)(code As String, Optional cancellationToken As CancellationToken = Nothing) As Task(Of T) Implements IVBScriptRunner.EvaluateAsync
            Return EvaluateCoreAsync(Of T)(code, cancellationToken)
        End Function

        Private Async Function RunFileCoreAsync(path As String, arguments As IReadOnlyList(Of String), compileOnly As Boolean, cancellationToken As CancellationToken) As Task(Of VBScriptResult)
            Dim options = CreateEffectiveOptions(arguments)
            Dim fullPath = ResolveScriptPath(options, path)
            Dim source = ReadSourceText(fullPath, options.FileEncoding)
            Return Await ExecuteAsync(options, source, fullPath, compileOnly, cancellationToken).ConfigureAwait(False)
        End Function

        Private Async Function RunCodeCoreAsync(code As String, compileOnly As Boolean, cancellationToken As CancellationToken) As Task(Of VBScriptResult)
            Dim options = CreateEffectiveOptions(arguments:=Nothing)
            Dim source = VBScriptHostInternals.CreateSourceText(code, options)
            Return Await ExecuteAsync(options, source, filePath:=Nothing, compileOnly:=compileOnly, cancellationToken:=cancellationToken).ConfigureAwait(False)
        End Function

        Private Async Function EvaluateCoreAsync(Of T)(code As String, cancellationToken As CancellationToken) As Task(Of T)
            ThrowIfHostStopping()
            EnterExclusive()

            Dim linked As CancellationTokenSource = Nothing
            Dim loader As InteractiveAssemblyLoader = Nothing
            Try
                linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.ApplicationStopping)
                loader = New InteractiveAssemblyLoader()

                Dim options = CreateEffectiveOptions(arguments:=Nothing)
                Dim capture As New CapturingScriptOutput()
                Dim globals = VBScriptHostInternals.ResolveGlobals(options, globals:=Nothing, defaultOutput:=New TeeScriptOutput(capture, _output))
                Dim source = VBScriptHostInternals.CreateSourceText(code, options)

                Dim value = Await VBScriptHostInternals.EvaluateAsync(Of T)(
                    options,
                    source,
                    globals,
                    loader,
                    linked.Token,
                    restoreCoordinator:=CreateRestoreCoordinator(options, loader),
                    packageResolver:=CreatePackageResolver()).ConfigureAwait(False)
                linked.Token.ThrowIfCancellationRequested()
                Return value
            Finally
                If loader IsNot Nothing Then
                    loader.Dispose()
                End If

                If linked IsNot Nothing Then
                    linked.Dispose()
                End If

                ExitExclusive()
            End Try
        End Function

        ''' <summary>
        ''' One file-script/snippet execution: the guard, the per-call loader, the linked token, the request and the
        ''' result mapping. <c>ScriptRunnerCore</c> owns compile/run/exit-code semantics; this method owns the host's
        ''' lifetime rules.
        ''' </summary>
        Private Async Function ExecuteAsync(options As VBScriptOptions, source As SourceText, filePath As String, compileOnly As Boolean, cancellationToken As CancellationToken) As Task(Of VBScriptResult)
            ThrowIfHostStopping()
            EnterExclusive()

            Dim linked As CancellationTokenSource = Nothing
            Dim loader As InteractiveAssemblyLoader = Nothing
            Try
                linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.ApplicationStopping)
                loader = New InteractiveAssemblyLoader()

                Dim capture As New CapturingScriptOutput()
                Dim globals = VBScriptHostInternals.ResolveGlobals(options, globals:=Nothing, defaultOutput:=New TeeScriptOutput(capture, _output))
                Dim request = VBScriptHostInternals.CreateRunRequest(
                    options,
                    source,
                    filePath,
                    globals,
                    loader,
                    compileOnly,
                    restoreCoordinator:=CreateRestoreCoordinator(options, loader),
                    packageResolver:=CreatePackageResolver())
                ' The session/core only needs to know whether the pre-scan blocks; printing is the host's job and
                ' this host merges the restore diagnostics into VBScriptResult.Diagnostics instead (no double count).
                request.RestoreDiagnosticsReporter = Function(diagnostics) VBScriptHostInternals.HasErrors(diagnostics)

                Dim outcome = Await ScriptRunnerCore.Instance.RunAsync(request, linked.Token).ConfigureAwait(False)

                ' Cancellation that lands while the script runs wins over the script's own result: the one-shot
                ' entries pin cancellation to OperationCanceledException.
                linked.Token.ThrowIfCancellationRequested()

                Return VBScriptHostInternals.MapRunOutcome(outcome, capture.OutText)
            Finally
                If loader IsNot Nothing Then
                    loader.Dispose()
                End If

                If linked IsNot Nothing Then
                    linked.Dispose()
                End If

                ExitExclusive()
            End Try
        End Function

        Private Function CreateEffectiveOptions(arguments As IReadOnlyList(Of String)) As VBScriptOptions
            Dim effective = VBScriptHostInternals.CreateEffectiveOptions(_options.Value)
            If arguments IsNot Nothing Then
                effective.ScriptArguments = arguments
            End If

            Return effective
        End Function

        ''' <summary>
        ''' Resolves the path against <see cref="VBScriptOptions.WorkingDirectory"/> (vbi uses the process current
        ''' directory when unset) and turns "missing file" into <see cref="VBScriptScriptNotFoundException"/> instead of
        ''' a raw <see cref="IOException"/>.
        ''' </summary>
        Private Shared Function ResolveScriptPath(options As VBScriptOptions, scriptPath As String) As String
            If String.IsNullOrWhiteSpace(scriptPath) Then
                Throw New VBScriptHostException(
                    "The script path is empty. Pass the path of a .vbx script file, absolute or relative to " &
                    "VBScriptOptions.WorkingDirectory.")
            End If

            Dim fullPath As String
            Try
                fullPath = If(
                    Path.IsPathRooted(scriptPath),
                    scriptPath,
                    Path.Combine(VBScriptHostInternals.ResolveWorkingDirectory(options), scriptPath))
            Catch ex As ArgumentException
                Throw New VBScriptScriptNotFoundException(
                    "The script path '" & scriptPath & "' is not a valid file path. Check the path, or set " &
                    "VBScriptOptions.WorkingDirectory to the directory it is relative to.", ex)
            End Try

            If Not File.Exists(fullPath) Then
                Throw New VBScriptScriptNotFoundException(
                    "The script file '" & fullPath & "' does not exist. Check the path, or set " &
                    "VBScriptOptions.WorkingDirectory to the directory it is relative to.")
            End If

            Return fullPath
        End Function

        Private Shared Function ReadSourceText(path As String, encoding As Encoding) As SourceText
            Using stream = File.OpenRead(path)
                Return SourceText.From(stream, encoding)
            End Using
        End Function

        ''' <summary>
        ''' A stopped host must not silently run scripts: the application is shutting down and anything the script
        ''' does would race the teardown.
        ''' </summary>
        Private Sub ThrowIfHostStopping()
            If Not _lifetime.ApplicationStopping.IsCancellationRequested Then
                Return
            End If

            Throw New VBScriptHostException(
                "The host is stopping (IHostApplicationLifetime.ApplicationStopping was signalled), so no script can " &
                "be started. Run the script while the application is running, and stop issuing runner calls before " &
                "the host begins its shutdown.")
        End Sub

        Private Sub EnterExclusive()
            If Interlocked.CompareExchange(_busy, 1, 0) = 0 Then
                Return
            End If

            Throw New VBScriptBusyException()
        End Sub

        Private Sub ExitExclusive()
            Interlocked.Exchange(_busy, 0)
        End Sub

        Private Function CreatePackageResolver() As ScriptNuGetPackageResolver
            Return New NuGetPackageResolverAdapter(_packageResolver)
        End Function

        Private Function CreateRestoreCoordinator(options As VBScriptOptions, loader As InteractiveAssemblyLoader) As IScriptRestoreCoordinator
            Return New NuGetRestoreCoordinatorAdapter(_restoreService, options, loader)
        End Function

        ''' <summary>
        ''' Startup consistency: a restore service and a package resolver are two halves of one contract — the
        ''' service writes the restored package state and the resolver reads it. Replacing only the service leaves
        ''' the default resolver reading a session the custom service never fills, which otherwise surfaces later as an
        ''' unexplained "assembly not found" compile error. Fail at runner construction instead, with the fix in the
        ''' message.
        ''' </summary>
        Private Shared Sub ValidateNuGetRestoreConfiguration(restoreService As INuGetRestoreService, packageResolver As INuGetPackageResolver)
            If TypeOf restoreService Is DotnetCliRestoreService Then
                Return
            End If

            If Not TypeOf packageResolver Is SessionNuGetPackageResolver Then
                Return
            End If

            Throw New VBScriptHostException(
                "The NuGet restore configuration is inconsistent: INuGetRestoreService is '" &
                restoreService.GetType().FullName & "', but INuGetPackageResolver is still the default " &
                "'" & GetType(SessionNuGetPackageResolver).FullName & "'. A custom restore service usually writes " &
                "its package state where the default resolver cannot see it. Call AddVBScriptNuGetRestore() to " &
                "register the three default NuGet services as a group before replacing them, or replace " &
                "INuGetPackageResolver with a resolver that reads the state your restore service writes.")
        End Sub

        ''' <summary>
        ''' <see cref="VBScriptOptions.UseDesktopReferences"/> only has a meaning on net472 (where the desktop
        ''' reference set exists). On net10 the option is ignored, and saying so once per runner is better than
        ''' letting the host wonder why <c>System.Windows.Forms</c> is missing.
        ''' </summary>
        Private Sub WarnWhenDesktopReferencesAreIgnored()
            If Not _options.Value.UseDesktopReferences OrElse VBScriptDefaults.SupportsDesktopReferences Then
                Return
            End If

            _logger.LogWarning(
                "VBScriptOptions.UseDesktopReferences is set, but this build targets {TargetFramework}, where the " &
                "desktop reference set (System.Drawing, System.Windows.Forms) is not added. The option is ignored; " &
                "add the assemblies explicitly with VBScriptOptions.References if the script needs them.",
                RuntimeInformation.FrameworkDescription)
        End Sub

    End Class

End Namespace
