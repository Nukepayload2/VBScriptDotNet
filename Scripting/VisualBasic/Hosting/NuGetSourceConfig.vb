' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.IO
Imports System.Security.Cryptography
Imports System.Text

Namespace Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

    ''' <summary>
    ''' One directory-level <c>NuGet.config</c> taken from the script's directory chain, together with the
    ''' path it must be mirrored to inside the restore cache key directory (design §C1/§C2). The content is
    ''' kept as raw bytes so mirroring is byte-exact and the fingerprint never depends on an encoding
    ''' decision made here.
    ''' </summary>
    Friend NotInheritable Class SourceConfigFile

        Friend Sub New(mirrorRelativePath As String, content As Byte())
            If String.IsNullOrWhiteSpace(mirrorRelativePath) Then
                Throw New ArgumentException("Mirror relative path required.", NameOf(mirrorRelativePath))
            End If
            _mirrorRelativePath = mirrorRelativePath
            _content = If(content, Array.Empty(Of Byte)())
        End Sub

        Private ReadOnly _mirrorRelativePath As String
        Private ReadOnly _content As Byte()

        ''' <summary>
        ''' Path of the mirrored copy relative to the cache key directory (e.g. <c>sub\NuGet.config</c>),
        ''' which is the same relative path it had below the mirror root on the script's directory chain.
        ''' </summary>
        Friend ReadOnly Property MirrorRelativePath As String
            Get
                Return _mirrorRelativePath
            End Get
        End Property

        Friend ReadOnly Property Content As Byte()
            Get
                Return _content
            End Get
        End Property
    End Class

    ''' <summary>
    ''' Result of one directory-chain collection (design §C1/§C5): the config files to mirror plus the
    ''' mirror root the generated project must be placed under. A config that exists but cannot be read is
    ''' reported through <see cref="FailedPath"/> rather than thrown, so the coordinator can surface it as a
    ''' diagnostic instead of failing as an opaque restore error.
    ''' </summary>
    Friend NotInheritable Class NuGetSourceConfigResult

        Friend Shared ReadOnly Empty As New NuGetSourceConfigResult(ImmutableArray(Of SourceConfigFile).Empty, Nothing, Nothing, Nothing)

        Friend Sub New(files As ImmutableArray(Of SourceConfigFile),
                       mirrorRoot As String,
                       failedPath As String,
                       failureReason As String)
            _files = If(files.IsDefault, ImmutableArray(Of SourceConfigFile).Empty, files)
            _mirrorRoot = mirrorRoot
            _failedPath = failedPath
            _failureReason = If(failureReason, String.Empty)
        End Sub

        Private ReadOnly _files As ImmutableArray(Of SourceConfigFile)
        Private ReadOnly _mirrorRoot As String
        Private ReadOnly _failedPath As String
        Private ReadOnly _failureReason As String

        ''' <summary>Config files to mirror, ordered by <see cref="SourceConfigFile.MirrorRelativePath"/>.</summary>
        Friend ReadOnly Property Files As ImmutableArray(Of SourceConfigFile)
            Get
                Return _files
            End Get
        End Property

        ''' <summary>
        ''' Directory the generated project is placed under: the parent of the topmost (outermost)
        ''' directory-level config, or the script's own directory when the chain has no config
        ''' (<see cref="String.Empty"/> when there is no script path at all).
        ''' </summary>
        Friend ReadOnly Property MirrorRoot As String
            Get
                Return _mirrorRoot
            End Get
        End Property

        ''' <summary>The config that could not be read, or <c>Nothing</c> when the collection succeeded.</summary>
        Friend ReadOnly Property FailedPath As String
            Get
                Return _failedPath
            End Get
        End Property

        Friend ReadOnly Property FailureReason As String
            Get
                Return _failureReason
            End Get
        End Property

        Friend ReadOnly Property HasFailure As Boolean
            Get
                Return _failedPath IsNot Nothing
            End Get
        End Property
    End Class

    ''' <summary>
    ''' File-system seam for <see cref="NuGetSourceConfig.Collect"/> (design §C1), mirroring the
    ''' <see cref="IRestoreRunner"/> style: production uses <see cref="PhysicalSourceConfigFileSystem"/> and
    ''' tests inject an in-memory double so no file is touched.
    ''' </summary>
    Friend Interface ISourceConfigFileSystem

        Function DirectoryExists(path As String) As Boolean

        ''' <summary>
        ''' Files directly inside <paramref name="directory"/>. Enumerating (rather than probing a single
        ''' spelling with <c>File.Exists</c>) is what makes <c>NuGet.config</c> match case-insensitively on
        ''' Linux as well as on Windows.
        ''' </summary>
        Function EnumerateFiles(directory As String) As IEnumerable(Of String)

        Function ReadAllBytes(path As String) As Byte()
    End Interface

    Friend NotInheritable Class PhysicalSourceConfigFileSystem
        Implements ISourceConfigFileSystem

        Friend Shared ReadOnly Instance As New PhysicalSourceConfigFileSystem()

        Private Sub New()
        End Sub

        Public Function DirectoryExists(path As String) As Boolean Implements ISourceConfigFileSystem.DirectoryExists
            Return Directory.Exists(path)
        End Function

        Public Function EnumerateFiles(directory As String) As IEnumerable(Of String) Implements ISourceConfigFileSystem.EnumerateFiles
            Try
                ' Fully qualified: this method's "directory" parameter shadows the Directory type (VB is
                ' case-insensitive).
                Return System.IO.Directory.GetFiles(directory)
            Catch generatedExceptionName As Exception
                ' The directory vanished between the probe and the enumeration, or cannot be listed: treat it as
                ' carrying no configuration.
                Return Array.Empty(Of String)()
            End Try
        End Function

        Public Function ReadAllBytes(path As String) As Byte() Implements ISourceConfigFileSystem.ReadAllBytes
            Return File.ReadAllBytes(path)
        End Function
    End Class

    ''' <summary>
    ''' Pure directory-chain config collection and source fingerprint (design §C1). NuGet resolves its
    ''' configuration chain from the <em>project file's directory</em> upwards, and the temporary restore
    ''' project lives under <c>%LOCALAPPDATA%</c>, so a <c>NuGet.config</c> next to a script has no effect.
    ''' Rather than merging config files (which would drop <c>packageSourceCredentials</c> and
    ''' <c>&lt;clear/&gt;</c> semantics), the collected files are mirrored into the cache key directory at the
    ''' same relative depth, and the generated project is placed at the depth of the script's own directory —
    ''' NuGet then walks a chain identical to the real one, including credentials and ambient merging.
    ''' The mirrored set is also fingerprinted into the cache key so "same key" keeps meaning "same restore
    ''' inputs".
    ''' </summary>
    Friend NotInheritable Class NuGetSourceConfig

        ''' <summary>
        ''' File name NuGet uses for directory-level configuration. Matched case-insensitively so Windows and
        ''' Linux agree and a user's <c>NuGet.Config</c> spelling still counts.
        ''' </summary>
        Friend Const ConfigFileName As String = "NuGet.config"

        Friend Const ProjectFileName As String = "project.vbproj"

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Walks up from the script's directory collecting one <c>NuGet.config</c> per level until the root
        ''' (design §C1). A missing/blank/non-absolute-unresolvable script path, or a script directory that
        ''' does not exist, yields the empty result — the pure-REPL and no-config cases, which must stay
        ''' byte-identical to the previous behavior.
        ''' </summary>
        Friend Shared Function Collect(scriptFilePath As String, fileSystem As ISourceConfigFileSystem) As NuGetSourceConfigResult
            If fileSystem Is Nothing Then
                Throw New ArgumentNullException(NameOf(fileSystem))
            End If
            If String.IsNullOrWhiteSpace(scriptFilePath) Then
                Return NuGetSourceConfigResult.Empty
            End If

            Dim scriptDirectory = GetScriptDirectory(scriptFilePath)
            If scriptDirectory Is Nothing OrElse Not fileSystem.DirectoryExists(scriptDirectory) Then
                Return NuGetSourceConfigResult.Empty
            End If

            ' Innermost first; the last hit is the topmost (outermost) directory-level config, which is the
            ' one NuGet applies first and therefore the one the mirror hierarchy must be rooted at.
            Dim hits As New List(Of String)()
            Dim current = scriptDirectory
            While Not String.IsNullOrEmpty(current)
                If Not fileSystem.DirectoryExists(current) Then
                    Exit While
                End If

                Dim candidate = FindConfigFile(current, fileSystem)
                If candidate IsNot Nothing Then
                    hits.Add(candidate)
                End If

                Dim parent = Path.GetDirectoryName(TrimTrailingSeparators(current))
                If String.IsNullOrEmpty(parent) OrElse String.Equals(parent, current, StringComparison.OrdinalIgnoreCase) Then
                    Exit While
                End If
                current = parent
            End While

            If hits.Count = 0 Then
                Return New NuGetSourceConfigResult(ImmutableArray(Of SourceConfigFile).Empty, scriptDirectory, Nothing, Nothing)
            End If

            Dim mirrorRoot = Path.GetDirectoryName(TrimTrailingSeparators(hits(hits.Count - 1)))
            Dim files As New List(Of SourceConfigFile)()
            For Each hit In hits
                ' Mirror under the canonical file name: a differently-cased original (or one whose spelling an
                ' enumeration happened to return) must still be found by NuGet's own discovery once copied.
                Dim hitDirectory = Path.GetDirectoryName(TrimTrailingSeparators(hit))
                Dim relativeDirectory = TryGetRelativePath(mirrorRoot, hitDirectory)
                Dim relativePath = If(String.IsNullOrEmpty(relativeDirectory),
                                      ConfigFileName,
                                      Path.Combine(relativeDirectory, ConfigFileName))

                Dim content As Byte()
                Try
                    content = fileSystem.ReadAllBytes(hit)
                Catch ex As Exception
                    Return New NuGetSourceConfigResult(files.ToImmutableArray(), mirrorRoot, hit, ex.Message)
                End Try
                If content Is Nothing Then
                    Return New NuGetSourceConfigResult(files.ToImmutableArray(), mirrorRoot, hit, Nothing)
                End If

                files.Add(New SourceConfigFile(relativePath, content))
            Next

            files.Sort(Function(left, right) StringComparer.OrdinalIgnoreCase.Compare(left.MirrorRelativePath, right.MirrorRelativePath))
            Return New NuGetSourceConfigResult(files.ToImmutableArray(), mirrorRoot, Nothing, Nothing)
        End Function

        ''' <summary>
        ''' The single directory-level config file in <paramref name="directory"/>, or <c>Nothing</c>. Names are
        ''' compared case-insensitively so a <c>nuget.config</c> spelling counts on every platform; if one
        ''' directory somehow carries several spellings the first in ordinal order wins, deterministically.
        ''' </summary>
        Private Shared Function FindConfigFile(directory As String, fileSystem As ISourceConfigFileSystem) As String
            Dim match As String = Nothing
            For Each candidate In fileSystem.EnumerateFiles(directory)
                If candidate Is Nothing OrElse Not String.Equals(Path.GetFileName(candidate), ConfigFileName, StringComparison.OrdinalIgnoreCase) Then
                    Continue For
                End If
                If match Is Nothing OrElse String.CompareOrdinal(candidate, match) < 0 Then
                    match = candidate
                End If
            Next
            Return match
        End Function

        ''' <summary>
        ''' Path of the generated project relative to the cache key directory (design §C1 rule 4): the
        ''' script's directory depth below the mirror root. <c>project.vbproj</c> when there is no config, so
        ''' the layout — and the cache directory's own file set — stays exactly as it was.
        ''' </summary>
        Friend Shared Function ProjectRelativePath(scriptFilePath As String, mirrorRoot As String) As String
            If String.IsNullOrEmpty(mirrorRoot) Then
                Return ProjectFileName
            End If

            Dim scriptDirectory = GetScriptDirectory(scriptFilePath)
            If scriptDirectory Is Nothing Then
                Return ProjectFileName
            End If

            Dim relativeDirectory = TryGetRelativePath(mirrorRoot, scriptDirectory)
            If String.IsNullOrEmpty(relativeDirectory) Then
                Return ProjectFileName
            End If

            Return Path.Combine(relativeDirectory, ProjectFileName)
        End Function

        ''' <summary>
        ''' Stable fingerprint of the mirrored config set (design §C1 rule 5): SHA-256, hex encoded, over
        ''' every file's lower-cased mirror relative path, a NUL, and the raw bytes, in relative-path order.
        ''' Only relative paths and bytes are consumed — never absolute paths, timestamps or machine names —
        ''' so it is machine independent and reproducible, while any content change is guaranteed to change
        ''' the restore cache key. An empty set fingerprints as the empty string, which is byte-identical to
        ''' the value production used before this fingerprint was wired up.
        ''' </summary>
        Friend Shared Function Fingerprint(files As ImmutableArray(Of SourceConfigFile)) As String
            If files.IsDefaultOrEmpty Then
                Return String.Empty
            End If

            ' Order is part of the contract, not of the caller's array: sort a copy so an enumeration-order
            ' difference cannot produce a different fingerprint for the same set.
            Dim ordered As New List(Of SourceConfigFile)(files)
            ordered.Sort(Function(left, right) StringComparer.OrdinalIgnoreCase.Compare(left.MirrorRelativePath, right.MirrorRelativePath))

            Using sha = SHA256.Create()
                Using stream As New MemoryStream()
                    For Each file In ordered
                        Dim pathBytes = Encoding.UTF8.GetBytes(file.MirrorRelativePath.ToLowerInvariant())
                        stream.Write(pathBytes, 0, pathBytes.Length)
                        stream.WriteByte(0)
                        stream.Write(file.Content, 0, file.Content.Length)
                    Next
                    stream.Position = 0

                    Dim hash = sha.ComputeHash(stream)
                    Dim hex As New StringBuilder(hash.Length * 2)
                    For Each b In hash
                        hex.Append(b.ToString("x2"))
                    Next
                    Return hex.ToString()
                End Using
            End Using
        End Function

        ''' <summary>
        ''' The script's directory as an absolute path with no trailing separator, or <c>Nothing</c> when the
        ''' path is blank or not resolvable (relative paths resolve against the host's current directory,
        ''' matching how the compiler already resolved the script file).
        ''' </summary>
        Private Shared Function GetScriptDirectory(scriptFilePath As String) As String
            If String.IsNullOrWhiteSpace(scriptFilePath) Then
                Return Nothing
            End If

            Try
                Dim fullPath = scriptFilePath
                If Not Path.IsPathRooted(fullPath) Then
                    fullPath = Path.GetFullPath(fullPath)
                End If
                Return TrimTrailingSeparators(Path.GetDirectoryName(fullPath))
            Catch generatedExceptionName As Exception
                ' Invalid path characters and similar: treated as "no directory chain", exactly like a blank
                ' path, so the caller falls back to the pre-existing no-config behavior.
                Return Nothing
            End Try
        End Function

        ''' <summary>
        ''' <paramref name="fullPath"/> relative to <paramref name="ancestor"/>, or <c>Nothing</c> when it is
        ''' not below it. An empty string means the two paths are the same directory. Implemented lexically
        ''' (both paths come from the same walk, so the ancestor is a real prefix) because
        ''' <c>Path.GetRelativePath</c> does not exist on netstandard2.0.
        ''' </summary>
        Private Shared Function TryGetRelativePath(ancestor As String, fullPath As String) As String
            If String.IsNullOrEmpty(ancestor) OrElse String.IsNullOrEmpty(fullPath) Then
                Return Nothing
            End If

            Dim a = TrimTrailingSeparators(ancestor)
            Dim f = TrimTrailingSeparators(fullPath)
            If String.Equals(a, f, StringComparison.OrdinalIgnoreCase) Then
                Return String.Empty
            End If
            If f.Length <= a.Length OrElse Not f.StartsWith(a, StringComparison.OrdinalIgnoreCase) Then
                Return Nothing
            End If

            ' Require a separator boundary so "C:\ab" is not treated as an ancestor of "C:\abc". A root like
            ' "C:\" keeps its trailing separator, in which case the boundary is already satisfied.
            If Not IsSeparator(a.Chars(a.Length - 1)) AndAlso Not IsSeparator(f.Chars(a.Length)) Then
                Return Nothing
            End If

            Return f.Substring(a.Length + 1)
        End Function

        ''' <summary>
        ''' Removes trailing directory separators while keeping a volume root such as <c>C:\</c> intact, so
        ''' relative-path arithmetic never has to special-case the drive root.
        ''' </summary>
        Private Shared Function TrimTrailingSeparators(path As String) As String
            If String.IsNullOrEmpty(path) Then
                Return path
            End If

            Dim [end] = path.Length
            While [end] > 1 AndAlso IsSeparator(path.Chars([end] - 1))
                If [end] = 3 AndAlso path.Chars(1) = ":"c Then
                    Exit While
                End If
                [end] -= 1
            End While
            Return path.Substring(0, [end])
        End Function

        Private Shared Function IsSeparator(ch As Char) As Boolean
            Return ch = Path.DirectorySeparatorChar OrElse ch = Path.AltDirectorySeparatorChar
        End Function
    End Class
End Namespace
