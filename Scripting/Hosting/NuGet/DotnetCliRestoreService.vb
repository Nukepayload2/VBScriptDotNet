' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' Default <see cref="INuGetRestoreService"/>: wraps the internal <c>NuGetRestoreCoordinator</c> (the same
    ''' pre-scan/restore implementation vbi uses) over one shared <see cref="NuGetPackageSession"/> and one restore
    ''' runner. Restores are serialized because the session cache is a singleton and the coordinator writes into it.
    ''' <para>
    ''' The public contract has no assembly loader, but a restore must push the restored runtime assets and native
    ''' probe roots into the loader the execution created. The default service therefore also implements the
    ''' Friend <see cref="INuGetRestoreServiceWithLoader"/> capability; the coordinator adapter uses it when
    ''' available and otherwise calls the public method.
    ''' </para>
    ''' </summary>
    Friend NotInheritable Class DotnetCliRestoreService
        Implements INuGetRestoreService
        Implements INuGetRestoreServiceWithLoader

        Private ReadOnly _options As IOptions(Of VBScriptOptions)
        Private ReadOnly _session As NuGetPackageSession
        Private ReadOnly _logger As ILogger(Of DotnetCliRestoreService)
        Private ReadOnly _runner As IRestoreRunner
        Private ReadOnly _gate As New SemaphoreSlim(1, 1)
        Private _firstTrigger As Integer

        Friend Sub New(options As IOptions(Of VBScriptOptions),
                       session As NuGetPackageSession,
                       logger As ILogger(Of DotnetCliRestoreService),
                       runner As INuGetRestoreRunner)
            If options Is Nothing Then
                Throw New ArgumentNullException(NameOf(options))
            End If
            If session Is Nothing Then
                Throw New ArgumentNullException(NameOf(session))
            End If
            If logger Is Nothing Then
                Throw New ArgumentNullException(NameOf(logger))
            End If
            If runner Is Nothing Then
                Throw New ArgumentNullException(NameOf(runner))
            End If

            _options = options
            _session = session
            _logger = logger
            ' The public runner is the execution layer: every restore the default service performs goes through it.
            _runner = New NuGetRestoreRunnerBridge(runner)
        End Sub

        Public Function PrepareCompilationAsync(source As VBScriptSource, options As VBScriptOptions, cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of VBScriptDiagnostic)) Implements INuGetRestoreService.PrepareCompilationAsync
            Return PrepareCoreAsync(source, options, loader:=Nothing, cancellationToken:=cancellationToken)
        End Function

        Friend Function PrepareCompilationAsync(source As VBScriptSource, options As VBScriptOptions, loader As InteractiveAssemblyLoader, cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of VBScriptDiagnostic)) Implements INuGetRestoreServiceWithLoader.PrepareCompilationAsync
            Return PrepareCoreAsync(source, options, loader, cancellationToken)
        End Function

        Private Async Function PrepareCoreAsync(source As VBScriptSource,
                                                options As VBScriptOptions,
                                                loader As InteractiveAssemblyLoader,
                                                cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of VBScriptDiagnostic))
            If source Is Nothing Then
                Return ImmutableArray(Of VBScriptDiagnostic).Empty
            End If

            Dim effectiveOptions = If(options, _options.Value)
            Dim context = effectiveOptions.RestoreContext
            Dim baseDirectory = VBScriptHostInternals.ResolveWorkingDirectory(effectiveOptions)
            context.UpdateSourceFingerprint(baseDirectory)

            Dim firstTrigger As Boolean
            If Interlocked.Exchange(_firstTrigger, 1) = 0 Then
                firstTrigger = True
            End If

            Await _gate.WaitAsync(cancellationToken).ConfigureAwait(False)
            Try
                ' One coordinator per execution: it carries the per-run cumulative package set and, when the
                ' execution supplied one, the per-run assembly loader. The session stays shared. The context
                ' fingerprint is the single source of truth: the coordinator must not derive a second one from the
                ' script's directory chain, or the cache key would diverge from the exposed context.
                Dim coordinator As New NuGetRestoreCoordinator(
                    _session,
                    runner:=_runner,
                    sourceFingerprint:=context.SourceFingerprint,
                    loader:=loader,
                    sdkVersionObserver:=AddressOf context.UpdateSdkVersion)
                Dim scriptOptions = VBScriptHostInternals.CreateScriptOptions(effectiveOptions, source.FilePath, baseDirectory)
                Dim sourceText = VBScriptHostInternals.CreateSourceText(source.Text, effectiveOptions)

                Dim diagnostics = Await coordinator.PrepareCompilationAsync(sourceText, source.FilePath, scriptOptions, cancellationToken).ConfigureAwait(False)
                Return VBScriptHostInternals.MapDiagnostics(diagnostics)
            Finally
                _gate.Release()

                ' The first-trigger entry is written after the call so a real restore's observed SDK version is
                ' part of the context it mirrors.
                If firstTrigger Then
                    LogFirstTrigger(context)
                End If
            End Try
        End Function

        ''' <summary>
        ''' On first use the default implementation writes one Information entry that mirrors
        ''' the restore context, so a host can diagnose restore-environment drift from its own log pipeline.
        ''' </summary>
        Private Sub LogFirstTrigger(context As NuGetRestoreContext)
            _logger.LogInformation(
                "NuGet restore is being used for the first time. Restore context: TargetFramework={TargetFramework}; " &
                "RuntimeIdentifier={RuntimeIdentifier}; PlatformVersion={PlatformVersion}; SdkVersion={SdkVersion}; " &
                "SourceFingerprint={SourceFingerprint}.",
                context.TargetFramework,
                context.RuntimeIdentifier,
                context.PlatformVersion,
                context.SdkVersion,
                context.SourceFingerprint)
        End Sub

    End Class

End Namespace
