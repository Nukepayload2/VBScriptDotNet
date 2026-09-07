' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable
Imports System.IO
Imports System.Security.Cryptography
Imports System.Text

Namespace Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

    ''' <summary>
    ''' Content-addressed restore cache (design §E2). The key is a hash of the exact package set plus the
    ''' host image tuple; the script source is never part of it. Cache directories live under
    ''' <c>%LOCALAPPDATA%\Nukepayload2\vbi\nuget-restore\&lt;key&gt;</c> and are LRU-trimmed after each
    ''' successful restore.
    ''' </summary>
    Friend NotInheritable Class NuGetRestoreCache

        Friend Const MaxCacheDirectories As Integer = 256
        Friend Const TrimToDirectories As Integer = 128
        Friend Shared ReadOnly StaleAfter As TimeSpan = TimeSpan.FromDays(30)

        Private Sub New()
        End Sub

        Friend Shared Function DefaultCacheRoot() As String
            Dim localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            If String.IsNullOrEmpty(localAppData) Then
                localAppData = Path.GetTempPath()
            End If
            Return Path.Combine(localAppData, "Nukepayload2", "vbi", "nuget-restore")
        End Function

        ''' <summary>
        ''' SHA-256 key over the canonical package set and host image tuple (design §E1). Deterministic across
        ''' machines with the same ingredients and independent of the script source.
        ''' </summary>
        Friend Shared Function ComputeKey(
            packageKeys As ImmutableArray(Of String),
            hostTargetFramework As String,
            rid As String,
            sdkVersion As String,
            sourceFingerprint As String,
            net48 As Boolean) As String

            Dim descriptor As New StringBuilder()
            For Each key In packageKeys
                descriptor.Append(key)
                descriptor.Append(";")
            Next
            descriptor.Append("|")
            descriptor.Append(hostTargetFramework)
            descriptor.Append("|")
            descriptor.Append(rid)
            descriptor.Append("|")
            descriptor.Append(sdkVersion)
            descriptor.Append("|")
            descriptor.Append(sourceFingerprint)
            descriptor.Append("|")
            descriptor.Append(If(net48, "net48", "net"))

            Using sha = SHA256.Create()
                Dim hash = sha.ComputeHash(Encoding.UTF8.GetBytes(descriptor.ToString()))
                Dim hex As New StringBuilder(hash.Length * 2)
                For Each b In hash
                    hex.Append(b.ToString("x2"))
                Next
                Return hex.ToString()
            End Using
        End Function

        Friend Shared Function GetCacheDirectory(cacheRoot As String, key As String) As String
            Return Path.Combine(cacheRoot, key)
        End Function

        ''' <summary>
        ''' LRU trim after a successful restore (design §E2): a key directory not used for
        ''' <see cref="StaleAfter"/> and not the one just restored is deletable; when the cache grows beyond
        ''' <see cref="MaxCacheDirectories"/> the oldest directories are removed until
        ''' <see cref="TrimToDirectories"/> remain. The just-restored key is never removed.
        ''' </summary>
        Friend Shared Sub Trim(cacheRoot As String, protectedKey As String, nowUtc As Date)
            If String.IsNullOrEmpty(cacheRoot) OrElse Not Directory.Exists(cacheRoot) Then
                Return
            End If

            Dim entries = New List(Of String)()
            For Each folder In Directory.GetDirectories(cacheRoot)
                entries.Add(folder)
            Next

            Dim protectedPath = GetCacheDirectory(cacheRoot, protectedKey)
            For Each folder In entries.ToArray()
                If String.Equals(folder, protectedPath, StringComparison.OrdinalIgnoreCase) Then
                    Continue For
                End If
                If (nowUtc - Directory.GetLastWriteTimeUtc(folder)) > StaleAfter Then
                    TryDeleteDirectory(folder)
                    entries.Remove(folder)
                End If
            Next

            If entries.Count <= MaxCacheDirectories Then
                Return
            End If

            entries.Sort(Function(left, right)
                             Return Directory.GetLastWriteTimeUtc(left).CompareTo(Directory.GetLastWriteTimeUtc(right))
                         End Function)

            Dim index = 0
            While entries.Count > TrimToDirectories
                Dim staleCandidate = entries(index)
                If String.Equals(staleCandidate, protectedPath, StringComparison.OrdinalIgnoreCase) Then
                    index += 1
                    Continue While
                End If
                If TryDeleteDirectory(staleCandidate) Then
                    entries.RemoveAt(index)
                Else
                    index += 1
                End If
                If index >= entries.Count Then
                    Exit While
                End If
            End While
        End Sub

        Private Shared Function TryDeleteDirectory(path As String) As Boolean
            Try
                If Directory.Exists(path) Then
                    Directory.Delete(path, recursive:=True)
                End If
                Return True
            Catch generatedExceptionName As Exception
                ' A concurrent process may hold the directory; the next restore regenerates it (NuGet
                ' self-heals, design E7).
                Return False
            End Try
        End Function
    End Class
End Namespace
