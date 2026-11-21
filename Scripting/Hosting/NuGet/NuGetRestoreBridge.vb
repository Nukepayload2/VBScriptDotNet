' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Concurrent
Imports System.Collections.Immutable
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

' The descriptor constructed below is a host-local bridge for a diagnostic produced elsewhere, not an analyzer
' rule shipped in this assembly, so analyzer release tracking and message-shape analysis do not apply.
#Disable Warning RS2008, RS1032

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' Execution-path adapter that gives the VB script engine's internal <see cref="IScriptRestoreCoordinator"/>
    ''' seam a public <see cref="INuGetRestoreService"/>. It converts the source/options shape in both directions
    ''' and, when the service implements the Friend loader capability, hands it the loader the execution created.
    ''' </summary>
    Friend NotInheritable Class NuGetRestoreCoordinatorAdapter
        Implements IScriptRestoreCoordinator

        Private ReadOnly _service As INuGetRestoreService
        Private ReadOnly _options As VBScriptOptions
        Private ReadOnly _loader As InteractiveAssemblyLoader

        Friend Sub New(service As INuGetRestoreService, options As VBScriptOptions, loader As InteractiveAssemblyLoader)
            If service Is Nothing Then
                Throw New ArgumentNullException(NameOf(service))
            End If
            If options Is Nothing Then
                Throw New ArgumentNullException(NameOf(options))
            End If

            _service = service
            _options = options
            _loader = loader
        End Sub

        Public Async Function PrepareCompilationAsync(code As SourceText, filePath As String, options As ScriptOptions, cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of Diagnostic)) Implements IScriptRestoreCoordinator.PrepareCompilationAsync
            If code Is Nothing Then
                Return ImmutableArray(Of Diagnostic).Empty
            End If

            Dim source As New VBScriptSource(code.ToString(), filePath)
            Dim diagnostics As ImmutableArray(Of VBScriptDiagnostic)

            If TypeOf _service Is INuGetRestoreServiceWithLoader Then
                diagnostics = Await DirectCast(_service, INuGetRestoreServiceWithLoader).
                    PrepareCompilationAsync(source, _options, _loader, cancellationToken).ConfigureAwait(False)
            Else
                diagnostics = Await _service.PrepareCompilationAsync(source, _options, cancellationToken).ConfigureAwait(False)
            End If

            Return NuGetDiagnosticBridge.ToInternal(diagnostics, code, filePath, options)
        End Function

    End Class

    ''' <summary>
    ''' Internal restore-executor shape over the public <see cref="INuGetRestoreRunner"/>. The coordinator only knows
    ''' the internal <c>IRestoreRunner</c> seam; this adapter makes the public runner the real execution layer, so
    ''' replacing <see cref="INuGetRestoreRunner"/> in DI changes what the default restore service does.
    ''' </summary>
    Friend NotInheritable Class NuGetRestoreRunnerBridge
        Implements IRestoreRunner

        Private ReadOnly _runner As INuGetRestoreRunner

        Friend Sub New(runner As INuGetRestoreRunner)
            If runner Is Nothing Then
                Throw New ArgumentNullException(NameOf(runner))
            End If

            _runner = runner
        End Sub

        Public Function GetInstalledSdkVersionsAsync(cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of String)) Implements IRestoreRunner.GetInstalledSdkVersionsAsync
            Return _runner.GetInstalledSdkVersionsAsync(cancellationToken)
        End Function

        Public Async Function RestoreAsync(request As RestoreRequest, cancellationToken As CancellationToken) As Task(Of RestoreOutcome) Implements IRestoreRunner.RestoreAsync
            If request Is Nothing Then
                Throw New ArgumentNullException(NameOf(request))
            End If

            Dim publicRequest As New NuGetRestoreRequest(
                FirstOrEmpty(request.PackageNames),
                FirstOrEmpty(request.PackageVersions),
                request.CacheDirectory,
                request.ProjectXml,
                request.GlobalJson,
                request.ProjectRelativePath,
                MapMirrorFiles(request.MirrorFiles),
                request.SourceFingerprint)

            Dim outcome = Await _runner.RestoreAsync(publicRequest, cancellationToken).ConfigureAwait(False)
            Dim internalOutcome As New RestoreOutcome(
                outcome.ExitCode,
                outcome.StandardError,
                outcome.NuGetCacheWritten,
                outcome.AssetsJsonText,
                outcome.ConfigFailurePath,
                outcome.ConfigFailureReason)
            Return internalOutcome
        End Function

        Private Shared Function FirstOrEmpty(values As ImmutableArray(Of String)) As String
            If values.IsDefaultOrEmpty Then
                Return String.Empty
            End If

            Return values(0)
        End Function

        Private Shared Function MapMirrorFiles(files As ImmutableArray(Of SourceConfigFile)) As ImmutableArray(Of NuGetRestoreConfigFile)
            If files.IsDefaultOrEmpty Then
                Return ImmutableArray(Of NuGetRestoreConfigFile).Empty
            End If

            Dim builder = ImmutableArray.CreateBuilder(Of NuGetRestoreConfigFile)(files.Length)
            For Each file In files
                builder.Add(New NuGetRestoreConfigFile(file.MirrorRelativePath, ImmutableArray.CreateRange(Of Byte)(file.Content)))
            Next

            Return builder.ToImmutable()
        End Function

    End Class

    ''' <summary>
    ''' The shared resolver shape (<c>NuGetPackageResolver</c>) is abstract and must stay internal to the shared
    ''' scripting assembly; the VB script engine exposes a delegate-backed subclass
    ''' (<see cref="ScriptNuGetPackageResolver"/>), and this adapter forwards <c>#R "nuget:…"</c> resolution to
    ''' the public <see cref="INuGetPackageResolver"/> the host configured.
    ''' </summary>
    Friend NotInheritable Class NuGetPackageResolverAdapter
        Inherits ScriptNuGetPackageResolver

        Friend Sub New(resolver As INuGetPackageResolver)
            MyBase.New(CreateResolveDelegate(resolver))
        End Sub

        Private Shared Function CreateResolveDelegate(resolver As INuGetPackageResolver) As Func(Of String, String, ImmutableArray(Of String))
            If resolver Is Nothing Then
                Throw New ArgumentNullException(NameOf(resolver))
            End If

            Return Function(packageName, packageVersion) resolver.Resolve(packageName, packageVersion)
        End Function

    End Class

    ''' <summary>
    ''' Converts the host-owned <see cref="VBScriptDiagnostic"/> list back to the compiler's diagnostic shape. The
    ''' compile-only pipeline blocks on <c>DiagnosticSeverity.Error</c>, and the result mapping turns the
    ''' diagnostics back into host DTOs, so id, severity, message and anchor are preserved. A diagnostic anchored
    ''' in the submitted source is attached to a source location (not an external-file location) so the anchor
    ''' survives the round trip.
    ''' </summary>
    Friend NotInheritable Class NuGetDiagnosticBridge

        Private Shared ReadOnly s_descriptors As New ConcurrentDictionary(Of String, DiagnosticDescriptor)(StringComparer.Ordinal)

        Private Sub New()
        End Sub

        Friend Shared Function ToInternal(diagnostics As ImmutableArray(Of VBScriptDiagnostic), code As SourceText, filePath As String, options As ScriptOptions) As ImmutableArray(Of Diagnostic)
            If diagnostics.IsDefaultOrEmpty Then
                Return ImmutableArray(Of Diagnostic).Empty
            End If

            Dim tree As SyntaxTree = Nothing
            Dim builder = ImmutableArray.CreateBuilder(Of Diagnostic)(diagnostics.Length)

            For Each item In diagnostics
                If item Is Nothing Then
                    Continue For
                End If

                builder.Add(ToInternalDiagnostic(item, code, filePath, options, tree))
            Next

            Return builder.ToImmutable()
        End Function

        Private Shared Function ToInternalDiagnostic(item As VBScriptDiagnostic, code As SourceText, filePath As String, options As ScriptOptions, ByRef tree As SyntaxTree) As Diagnostic
            Dim descriptor = GetDescriptor(item)
            Dim location = CreateLocation(item, code, filePath, options, tree)
            Return Diagnostic.Create(descriptor, location, item.Message)
        End Function

        Private Shared Function CreateLocation(item As VBScriptDiagnostic, code As SourceText, filePath As String, options As ScriptOptions, ByRef tree As SyntaxTree) As Location
            If item.Line <= 0 Then
                Return Location.None
            End If

            Dim itemPath = item.FilePath
            Dim anchoredInSubmission = String.IsNullOrEmpty(itemPath) OrElse
                String.Equals(itemPath, filePath, StringComparison.OrdinalIgnoreCase)

            If anchoredInSubmission Then
                If tree Is Nothing Then
                    Dim parseOptions As VisualBasicParseOptions = Nothing
                    If options IsNot Nothing Then
                        parseOptions = TryCast(ScriptHostResolvers.GetParseOptions(options), VisualBasicParseOptions)
                    End If
                    If parseOptions Is Nothing Then
                        parseOptions = New VisualBasicParseOptions(kind:=SourceCodeKind.Script, languageVersion:=LanguageVersion.Latest)
                    End If

                    tree = VisualBasicSyntaxTree.ParseText(code, parseOptions, If(filePath, String.Empty))
                End If

                Dim lineIndex = Math.Max(0, Math.Min(item.Line - 1, code.Lines.Count - 1))
                Dim textLine = code.Lines(lineIndex)
                Dim column = Math.Max(0, Math.Min(Math.Max(item.Column, 1) - 1, textLine.Span.Length))
                Return Location.Create(tree, New TextSpan(textLine.Start + column, 0))
            End If

            Dim position As New LinePosition(Math.Max(item.Line, 1) - 1, Math.Max(item.Column, 1) - 1)
            Return Location.Create(itemPath, New TextSpan(0, 0), New LinePositionSpan(position, position))
        End Function

        Private Shared Function GetDescriptor(item As VBScriptDiagnostic) As DiagnosticDescriptor
            Dim id = If(String.IsNullOrEmpty(item.Id), "VBI0000", item.Id)
            Dim key = id & "|" & CInt(item.Severity).ToString(Globalization.CultureInfo.InvariantCulture)
            Return s_descriptors.GetOrAdd(key,
                Function(ignored) New DiagnosticDescriptor(
                    id,
                    title:=id,
                    messageFormat:="{0}",
                    category:="VBScriptingNuGet",
                    defaultSeverity:=ToCompilerSeverity(item.Severity),
                    isEnabledByDefault:=True))
        End Function

        Private Shared Function ToCompilerSeverity(severity As VBScriptDiagnosticSeverity) As DiagnosticSeverity
            Select Case severity
                Case VBScriptDiagnosticSeverity.Error
                    Return DiagnosticSeverity.Error
                Case VBScriptDiagnosticSeverity.Warning
                    Return DiagnosticSeverity.Warning
                Case VBScriptDiagnosticSeverity.Info
                    Return DiagnosticSeverity.Info
                Case Else
                    Return DiagnosticSeverity.Hidden
            End Select
        End Function

    End Class

End Namespace
