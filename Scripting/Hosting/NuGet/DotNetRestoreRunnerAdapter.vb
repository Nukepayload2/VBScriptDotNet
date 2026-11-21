' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' Default <see cref="INuGetRestoreRunner"/>: forwards the request to the internal restore executor (by
    ''' default <c>DotNetRestoreRunner</c>, the process-spawning executor vbi uses) and maps the raw outcome to the
    ''' host-owned DTO. It performs no diagnostic interpretation and no caching.
    ''' </summary>
    Friend NotInheritable Class DotNetRestoreRunnerAdapter
        Implements INuGetRestoreRunner

        Private ReadOnly _runner As IRestoreRunner

        Friend Sub New()
            Me.New(New DotNetRestoreRunner())
        End Sub

        ''' <summary>
        ''' Test seam: accept the internal executor so the public-to-internal request mapping can be asserted
        ''' with a fake instead of a process. Production resolves the parameterless overload above.
        ''' </summary>
        Friend Sub New(runner As IRestoreRunner)
            _runner = If(runner, New DotNetRestoreRunner())
        End Sub

        Public Function GetInstalledSdkVersionsAsync(cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of String)) Implements INuGetRestoreRunner.GetInstalledSdkVersionsAsync
            Return _runner.GetInstalledSdkVersionsAsync(cancellationToken)
        End Function

        Public Async Function RestoreAsync(request As NuGetRestoreRequest, cancellationToken As CancellationToken) As Task(Of NuGetRestoreOutcome) Implements INuGetRestoreRunner.RestoreAsync
            If request Is Nothing Then
                Throw New ArgumentNullException(NameOf(request))
            End If

            Dim restoreRequest As New RestoreRequest(
                request.CacheDirectory,
                request.ProjectXml,
                request.GlobalJson,
                MapMirrorFiles(request.MirrorFiles),
                projectRelativePath:=If(String.IsNullOrEmpty(request.ProjectRelativePath), Nothing, request.ProjectRelativePath),
                packageNames:=MakeSingleItem(request.PackageName),
                packageVersions:=MakeSingleItem(request.PackageVersion),
                sourceFingerprint:=request.SourceFingerprint)

            Dim outcome = Await _runner.RestoreAsync(restoreRequest, cancellationToken).ConfigureAwait(False)
            Return New NuGetRestoreOutcome(
                outcome.ExitCode,
                outcome.StandardError,
                outcome.NuGetCacheWritten,
                outcome.AssetsJsonText,
                ImmutableArray(Of VBScriptDiagnostic).Empty,
                outcome.ConfigFailurePath,
                outcome.ConfigFailureReason)
        End Function

        Private Shared Function MapMirrorFiles(files As ImmutableArray(Of NuGetRestoreConfigFile)) As ImmutableArray(Of SourceConfigFile)
            If files.IsDefaultOrEmpty Then
                Return ImmutableArray(Of SourceConfigFile).Empty
            End If

            Dim builder = ImmutableArray.CreateBuilder(Of SourceConfigFile)(files.Length)
            For Each file In files
                Dim content = If(file.Content.IsDefaultOrEmpty, Array.Empty(Of Byte)(), file.Content.ToArray())
                builder.Add(New SourceConfigFile(file.RelativePath, content))
            Next

            Return builder.ToImmutable()
        End Function

        Private Shared Function MakeSingleItem(value As String) As ImmutableArray(Of String)
            If String.IsNullOrEmpty(value) Then
                Return ImmutableArray(Of String).Empty
            End If

            Return ImmutableArray.Create(value)
        End Function

    End Class

End Namespace
