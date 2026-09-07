' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable
Imports System.Diagnostics
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks

Namespace Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

    ''' <summary>
    ''' A single restore request: where the temporary project lives and the byte-stable project content
    ''' (design §E). The runner writes the project once and runs <c>dotnet restore</c> against it.
    ''' </summary>
    Friend NotInheritable Class RestoreRequest

        Friend Sub New(cacheDirectory As String, projectXml As String, globalJson As String)
            If String.IsNullOrWhiteSpace(cacheDirectory) Then
                Throw New ArgumentException("Cache directory required.", NameOf(cacheDirectory))
            End If
            _cacheDirectory = cacheDirectory
            _projectXml = If(projectXml, String.Empty)
            _globalJson = If(globalJson, String.Empty)
        End Sub

        Private ReadOnly _cacheDirectory As String
        Private ReadOnly _projectXml As String
        Private ReadOnly _globalJson As String

        Friend ReadOnly Property CacheDirectory As String
            Get
                Return _cacheDirectory
            End Get
        End Property

        Friend ReadOnly Property ProjectFilePath As String
            Get
                Return Path.Combine(_cacheDirectory, "project.vbproj")
            End Get
        End Property

        Friend ReadOnly Property GlobalJsonPath As String
            Get
                Return Path.Combine(_cacheDirectory, "global.json")
            End Get
        End Property

        Friend ReadOnly Property ProjectXml As String
            Get
                Return _projectXml
            End Get
        End Property

        Friend ReadOnly Property GlobalJson As String
            Get
                Return _globalJson
            End Get
        End Property
    End Class

    ''' <summary>
    ''' Raw outcome of one restore run (design §E3): exit code, stderr, whether NuGet wrote its own
    ''' <c>obj/project.nuget.cache</c>, and the generated <c>obj/project.assets.json</c> text on success.
    ''' No interpretation is performed here; decision/translation are separate pure functions.
    ''' </summary>
    Friend NotInheritable Class RestoreOutcome

        Friend Sub New(exitCode As Integer, standardError As String, nuGetCacheWritten As Boolean, assetsJsonText As String)
            _exitCode = exitCode
            _standardError = If(standardError, String.Empty)
            _nuGetCacheWritten = nuGetCacheWritten
            _assetsJsonText = assetsJsonText
        End Sub

        Private ReadOnly _exitCode As Integer
        Private ReadOnly _standardError As String
        Private ReadOnly _nuGetCacheWritten As Boolean
        Private ReadOnly _assetsJsonText As String

        Friend ReadOnly Property ExitCode As Integer
            Get
                Return _exitCode
            End Get
        End Property

        Friend ReadOnly Property StandardError As String
            Get
                Return _standardError
            End Get
        End Property

        Friend ReadOnly Property NuGetCacheWritten As Boolean
            Get
                Return _nuGetCacheWritten
            End Get
        End Property

        Friend ReadOnly Property AssetsJsonText As String
            Get
                Return _assetsJsonText
            End Get
        End Property
    End Class

    ''' <summary>
    ''' Injectable restore runner seam (design §E3). Production is <see cref="DotNetRestoreRunner"/>;
    ''' tests inject a fake that returns fixed outcomes and assets text without spawning a process.
    ''' </summary>
    Friend Interface IRestoreRunner

        ''' <summary>
        ''' Installed .NET SDK versions (raw, unsorted). An empty result means no SDK is available and the
        ''' coordinator reports the SDK-missing diagnostic without attempting a restore.
        ''' </summary>
        Function GetInstalledSdkVersionsAsync(cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of String))

        ''' <summary>
        ''' Writes the temporary project under <paramref name="request"/>.CacheDirectory (once) and runs one
        ''' <c>dotnet restore</c>, returning the raw outcome.
        ''' </summary>
        Function RestoreAsync(request As RestoreRequest, cancellationToken As CancellationToken) As Task(Of RestoreOutcome)
    End Interface

    ''' <summary>
    ''' Pure SDK-version selection over the installed-SDK list (design §E3). The temporary project's
    ''' global.json pins the selected version with <c>rollForward: latestMajor</c>, so any installed SDK is
    ''' acceptable and the highest version is chosen for determinism.
    ''' </summary>
    Friend NotInheritable Class NuGetSdkResolver

        Private Sub New()
        End Sub

        Friend Shared Function SelectSdkVersion(installedSdks As ImmutableArray(Of String)) As String
            If installedSdks.IsDefaultOrEmpty Then
                Return Nothing
            End If

            Dim best As String = Nothing
            Dim bestVersion As Version = Nothing
            For Each sdk In installedSdks
                Dim parsed As Version = Nothing
                If Version.TryParse(sdk, parsed) Then
                    If bestVersion Is Nothing OrElse parsed > bestVersion Then
                        bestVersion = parsed
                        best = sdk
                    End If
                ElseIf bestVersion Is Nothing Then
                    If best Is Nothing OrElse String.Compare(sdk, best, StringComparison.Ordinal) > 0 Then
                        best = sdk
                    End If
                End If
            Next
            Return best
        End Function
    End Class

    ''' <summary>
    ''' Production restore runner: deploys the byte-stable temporary project under the cache key directory
    ''' (write once; never rewrites existing files) and spawns <c>dotnet restore</c>. Also trims the LRU
    ''' cache after a successful restore. Process/file access lives here so the coordinator stays pure.
    ''' </summary>
    Friend NotInheritable Class DotNetRestoreRunner
        Implements IRestoreRunner

        Public Function GetInstalledSdkVersionsAsync(cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of String)) Implements IRestoreRunner.GetInstalledSdkVersionsAsync
            Dim result = RunDotNet("--list-sdks")
            If Not result.Started Then
                Return Task.FromResult(ImmutableArray(Of String).Empty)
            End If

            Dim versions = New List(Of String)()
            For Each line In ReadLines(result.StandardOutput)
                Dim trimmed = line.Trim()
                If trimmed.Length > 0 Then
                    Dim space = trimmed.IndexOf(" "c)
                    If space > 0 Then
                        versions.Add(trimmed.Substring(0, space))
                    Else
                        versions.Add(trimmed)
                    End If
                End If
            Next
            Return Task.FromResult(versions.ToImmutableArray())
        End Function

        Public Function RestoreAsync(request As RestoreRequest, cancellationToken As CancellationToken) As Task(Of RestoreOutcome) Implements IRestoreRunner.RestoreAsync
            If request Is Nothing Then
                Throw New ArgumentNullException(NameOf(request))
            End If

            Try
                Directory.CreateDirectory(request.CacheDirectory)
                WriteOnce(request.ProjectFilePath, request.ProjectXml)
                WriteOnce(request.GlobalJsonPath, request.GlobalJson)

                Dim processResult = RunDotNet("restore """ & request.ProjectFilePath & """ --nologo")
                If Not processResult.Started Then
                    Return Task.FromResult(New RestoreOutcome(NuGetRestoreDiagnostics.DotNetMissingExitCode, String.Empty, False, Nothing))
                End If
                If processResult.ExitCode <> 0 Then
                    Return Task.FromResult(New RestoreOutcome(processResult.ExitCode, processResult.StandardError, False, Nothing))
                End If

                Dim nuGetCachePath = Path.Combine(request.CacheDirectory, "obj", "project.nuget.cache")
                Dim assetsPath = Path.Combine(request.CacheDirectory, "obj", "project.assets.json")
                Dim cacheWritten = File.Exists(nuGetCachePath)
                Dim assetsText As String = Nothing
                If cacheWritten AndAlso File.Exists(assetsPath) Then
                    assetsText = File.ReadAllText(assetsPath)
                End If

                If cacheWritten Then
                    Dim cacheRoot = Path.GetDirectoryName(request.CacheDirectory.TrimEnd(Path.DirectorySeparatorChar))
                    Dim key = Path.GetFileName(request.CacheDirectory.TrimEnd(Path.DirectorySeparatorChar))
                    If Not String.IsNullOrEmpty(cacheRoot) AndAlso Not String.IsNullOrEmpty(key) Then
                        NuGetRestoreCache.Trim(cacheRoot, key, Date.UtcNow)
                    End If
                End If

                Return Task.FromResult(New RestoreOutcome(processResult.ExitCode, processResult.StandardError, cacheWritten, assetsText))
            Catch generatedExceptionName As Exception
                Return Task.FromResult(New RestoreOutcome(NuGetRestoreDiagnostics.DotNetMissingExitCode, String.Empty, False, Nothing))
            End Try
        End Function

        Private Shared Sub WriteOnce(path As String, content As String)
            If Not File.Exists(path) Then
                File.WriteAllText(path, content)
            End If
        End Sub

        Private Shared Function RunDotNet(arguments As String) As DotNetProcessResult
            Try
                Dim startInfo As New ProcessStartInfo("dotnet", arguments) With {
                    .UseShellExecute = False,
                    .CreateNoWindow = True,
                    .RedirectStandardOutput = True,
                    .RedirectStandardError = True
                }
                Dim startedProcess As Process = Process.Start(startInfo)
                If startedProcess Is Nothing Then
                    Return New DotNetProcessResult(False, 0, String.Empty, String.Empty)
                End If
                Using process As Process = startedProcess
                    Dim standardOutput = process.StandardOutput.ReadToEnd()
                    Dim standardError = process.StandardError.ReadToEnd()
                    process.WaitForExit()
                    Return New DotNetProcessResult(True, process.ExitCode, standardOutput, standardError)
                End Using
            Catch generatedExceptionName As Exception
                ' dotnet executable not present / cannot be started.
                Return New DotNetProcessResult(False, 0, String.Empty, String.Empty)
            End Try
        End Function

        Private Shared Function ReadLines(text As String) As IEnumerable(Of String)
            Dim lines = New List(Of String)()
            If text Is Nothing Then
                Return lines
            End If
            Using reader As New StringReader(text)
                Dim line As String = reader.ReadLine()
                While line IsNot Nothing
                    lines.Add(line)
                    line = reader.ReadLine()
                End While
            End Using
            Return lines
        End Function

        Private NotInheritable Class DotNetProcessResult

            Friend Sub New(started As Boolean, exitCode As Integer, standardOutput As String, standardError As String)
                _started = started
                _exitCode = exitCode
                _standardOutput = standardOutput
                _standardError = standardError
            End Sub

            Private ReadOnly _started As Boolean
            Private ReadOnly _exitCode As Integer
            Private ReadOnly _standardOutput As String
            Private ReadOnly _standardError As String

            Friend ReadOnly Property Started As Boolean
                Get
                    Return _started
                End Get
            End Property

            Friend ReadOnly Property ExitCode As Integer
                Get
                    Return _exitCode
                End Get
            End Property

            Friend ReadOnly Property StandardOutput As String
                Get
                    Return _standardOutput
                End Get
            End Property

            Friend ReadOnly Property StandardError As String
                Get
                    Return _standardError
                End Get
            End Property
        End Class
    End Class
End Namespace
