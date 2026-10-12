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
    ''' A single restore request: where the temporary project lives, the byte-stable project content
    ''' (design §E), and the directory-level <c>NuGet.config</c> files mirrored from the script's directory
    ''' chain (design §C1/§C2). The runner writes the mirrored config and the project once, then runs
    ''' <c>dotnet restore</c> against the project.
    ''' </summary>
    Friend NotInheritable Class RestoreRequest

        Friend Sub New(cacheDirectory As String,
                       projectXml As String,
                       globalJson As String,
                       Optional mirrorFiles As ImmutableArray(Of SourceConfigFile) = Nothing,
                       Optional projectRelativePath As String = Nothing)
            If String.IsNullOrWhiteSpace(cacheDirectory) Then
                Throw New ArgumentException("Cache directory required.", NameOf(cacheDirectory))
            End If
            ValidateProjectRelativePath(projectRelativePath)
            _cacheDirectory = cacheDirectory
            _projectXml = If(projectXml, String.Empty)
            _globalJson = If(globalJson, String.Empty)
            _mirrorFiles = If(mirrorFiles.IsDefault, ImmutableArray(Of SourceConfigFile).Empty, mirrorFiles)
            _projectRelativePath = If(String.IsNullOrEmpty(projectRelativePath), NuGetSourceConfig.ProjectFileName, projectRelativePath)
        End Sub

        Private ReadOnly _cacheDirectory As String
        Private ReadOnly _projectXml As String
        Private ReadOnly _globalJson As String
        Private ReadOnly _mirrorFiles As ImmutableArray(Of SourceConfigFile)
        Private ReadOnly _projectRelativePath As String

        Friend ReadOnly Property CacheDirectory As String
            Get
                Return _cacheDirectory
            End Get
        End Property

        ''' <summary>
        ''' The temporary project's path. It sits at the depth of the script's own directory below the
        ''' mirror root (design §C2), so NuGet walks a configuration chain identical to the real one; with no
        ''' directory-level config it is <c>&lt;cache&gt;\project.vbproj</c> exactly as before.
        ''' </summary>
        Friend ReadOnly Property ProjectFilePath As String
            Get
                Return Path.Combine(_cacheDirectory, _projectRelativePath)
            End Get
        End Property

        ''' <summary>
        ''' Directory the project is written to, which is also the directory NuGet creates <c>obj</c> under.
        ''' Equal to <see cref="CacheDirectory"/> when the script's directory chain carries no config.
        ''' </summary>
        Friend ReadOnly Property ProjectDirectory As String
            Get
                Return Path.GetDirectoryName(ProjectFilePath)
            End Get
        End Property

        ''' <summary>
        ''' Directory-level config files to mirror under <see cref="CacheDirectory"/> before restoring. Empty
        ''' when the script's directory chain carries none.
        ''' </summary>
        Friend ReadOnly Property MirrorFiles As ImmutableArray(Of SourceConfigFile)
            Get
                Return _mirrorFiles
            End Get
        End Property

        ''' <summary>
        ''' Rejects a rooted or parent-traversing relative project path so neither the mirrored config nor the
        ''' generated project can escape the cache key directory it belongs to. The value is derived from the
        ''' script path, so this asserts an invariant rather than validating user input.
        ''' </summary>
        Private Shared Sub ValidateProjectRelativePath(projectRelativePath As String)
            If String.IsNullOrEmpty(projectRelativePath) Then
                Return
            End If
            If Path.IsPathRooted(projectRelativePath) Then
                Throw New ArgumentException("Project path must be relative to the cache directory.", NameOf(projectRelativePath))
            End If
            For Each part In projectRelativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                If part = ".." Then
                    Throw New ArgumentException("Project path must not traverse out of the cache directory.", NameOf(projectRelativePath))
                End If
            Next
        End Sub

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
    ''' No interpretation is performed here; decision/translation are separate pure functions. A mirrored
    ''' config file that could not be written is reported separately from a restore failure, so the
    ''' coordinator can surface it as a configuration diagnostic instead of an opaque restore error.
    ''' </summary>
    Friend NotInheritable Class RestoreOutcome

        Friend Sub New(exitCode As Integer,
                       standardError As String,
                       nuGetCacheWritten As Boolean,
                       assetsJsonText As String,
                       Optional configFailurePath As String = Nothing,
                       Optional configFailureReason As String = Nothing)
            _exitCode = exitCode
            _standardError = If(standardError, String.Empty)
            _nuGetCacheWritten = nuGetCacheWritten
            _assetsJsonText = assetsJsonText
            _configFailurePath = configFailurePath
            _configFailureReason = If(configFailureReason, String.Empty)
        End Sub

        Private ReadOnly _exitCode As Integer
        Private ReadOnly _standardError As String
        Private ReadOnly _nuGetCacheWritten As Boolean
        Private ReadOnly _assetsJsonText As String
        Private ReadOnly _configFailurePath As String
        Private ReadOnly _configFailureReason As String

        ''' <summary>Mirrored config file that could not be written, or <c>Nothing</c> when none failed.</summary>
        Friend ReadOnly Property ConfigFailurePath As String
            Get
                Return _configFailurePath
            End Get
        End Property

        Friend ReadOnly Property ConfigFailureReason As String
            Get
                Return _configFailureReason
            End Get
        End Property

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

            ' Mirror the script's directory-level configuration into the key directory first: the generated
            ' project sits at the script's depth below the mirror root, so NuGet's upward configuration walk
            ' from the project sees the same files, in the same order, as it would from the real script
            ' directory (design §C1/§C2) — credentials and <clear/> semantics included, because the files are
            ' copied verbatim rather than merged or re-expressed as project properties.
            Dim mirrorFailurePath As String = Nothing
            Dim mirrorFailureReason As String = Nothing
            If Not WriteMirrorFiles(request.CacheDirectory, request.MirrorFiles, mirrorFailurePath, mirrorFailureReason) Then
                Return Task.FromResult(New RestoreOutcome(NuGetRestoreDiagnostics.ConfigFailureExitCode, String.Empty, False, Nothing, mirrorFailurePath, mirrorFailureReason))
            End If

            Try
                WriteProjectFiles(request)

                Dim processResult = RunDotNet("restore """ & request.ProjectFilePath & """ --nologo")
                If Not processResult.Started Then
                    Return Task.FromResult(New RestoreOutcome(NuGetRestoreDiagnostics.DotNetMissingExitCode, String.Empty, False, Nothing))
                End If
                If processResult.ExitCode <> 0 Then
                    Return Task.FromResult(New RestoreOutcome(processResult.ExitCode, processResult.StandardError, False, Nothing))
                End If

                ' NuGet writes obj beside the project file, which is below the mirror root once the script's
                ' directory chain contributes a config.
                Dim nuGetCachePath = Path.Combine(request.ProjectDirectory, "obj", "project.nuget.cache")
                Dim assetsPath = Path.Combine(request.ProjectDirectory, "obj", "project.assets.json")
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

        ''' <summary>
        ''' Writes the byte-stable project and its global.json, once each (design §E). The project's parent
        ''' directory is created first: with a directory-level config the project sits below the mirror root,
        ''' and <see cref="WriteOnce"/> itself never creates directories — without this, that layout failed
        ''' with a <see cref="DirectoryNotFoundException"/> that surfaced as a bogus "SDK missing" diagnostic
        ''' (found by a real <c>vbi</c> run, not by the request-shape tests). Extracted so the deployment
        ''' layout can be verified without spawning a process.
        ''' </summary>
        Friend Shared Sub WriteProjectFiles(request As RestoreRequest)
            Directory.CreateDirectory(request.ProjectDirectory)
            WriteOnce(request.ProjectFilePath, request.ProjectXml)
            WriteOnce(request.GlobalJsonPath, request.GlobalJson)
        End Sub

        ''' <summary>
        ''' Mirrors the collected directory-level config files under <paramref name="cacheDirectory"/> at the
        ''' same relative depth they had below the mirror root (design §C2), each written once and never
        ''' rewritten: because the fingerprint is part of the cache key, an existing key directory can only
        ''' ever describe this exact config content. Returns False and reports the first failure through
        ''' <paramref name="failurePath"/>/<paramref name="failureReason"/> instead of throwing, so the
        ''' coordinator can surface it as a configuration diagnostic (design §C5).
        ''' </summary>
        Friend Shared Function WriteMirrorFiles(cacheDirectory As String,
                                                files As ImmutableArray(Of SourceConfigFile),
                                                ByRef failurePath As String,
                                                ByRef failureReason As String) As Boolean
            failurePath = Nothing
            failureReason = Nothing
            If files.IsDefaultOrEmpty Then
                Return True
            End If

            For Each mirrorFile In files
                Dim mirrorPath = Path.Combine(cacheDirectory, mirrorFile.MirrorRelativePath)
                If File.Exists(mirrorPath) Then
                    Continue For
                End If

                Try
                    Dim mirrorDirectory = Path.GetDirectoryName(mirrorPath)
                    If Not String.IsNullOrEmpty(mirrorDirectory) Then
                        Directory.CreateDirectory(mirrorDirectory)
                    End If
                    File.WriteAllBytes(mirrorPath, mirrorFile.Content)
                Catch generatedExceptionName As Exception
                    failurePath = mirrorPath
                    failureReason = generatedExceptionName.Message
                    Return False
                End Try
            Next
            Return True
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
