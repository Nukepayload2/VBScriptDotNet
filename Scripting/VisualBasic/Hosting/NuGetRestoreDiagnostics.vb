' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' Host-local diagnostics (not analyzer rules shipped in this assembly); analyzer release tracking and
' message-shape analysis do not apply.
#Disable Warning RS2008, RS1032

Imports System.Collections.Immutable
Imports System.Text

Namespace Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

    ''' <summary>
    ''' Restore-failure diagnostics anchored at the offending <c>#R</c> line. The mapping from a raw
    ''' restore outcome to a diagnostic is a pure function so it can be unit
    ''' tested without spawning a process.
    ''' </summary>
    Friend NotInheritable Class NuGetRestoreDiagnostics

        ''' <summary>
        ''' Exit code returned by the production runner when <c>dotnet</c> cannot be started at all (the SDK
        ''' is not installed). Kept distinct from any real exit code <c>dotnet restore</c> can return.
        ''' </summary>
        Friend Const DotNetMissingExitCode As Integer = Integer.MinValue

        ''' <summary>
        ''' Exit code carried by a <see cref="RestoreOutcome"/> whose mirrored directory-level
        ''' <c>NuGet.config</c> could not be written. The configuration path and reason travel on
        ''' the outcome itself, so the coordinator reports them directly and never reaches the exit-code
        ''' translation table with this value.
        ''' </summary>
        Friend Const ConfigFailureExitCode As Integer = Integer.MinValue + 1

        Private Shared ReadOnly s_configFailure As New DiagnosticDescriptor(
            id:="VBI1012",
            title:="NuGet source configuration could not be used",
            messageFormat:="The NuGet configuration file '{0}' could not be used: {1}",
            category:="VBScriptingNuGet",
            defaultSeverity:=DiagnosticSeverity.Error,
            isEnabledByDefault:=True)

        Private Shared ReadOnly s_sdkMissing As New DiagnosticDescriptor(
            id:="VBI1006",
            title:="NuGet restore requires the .NET SDK",
            messageFormat:="Restoring NuGet packages requires the .NET SDK, but no matching .NET SDK was found. Install a .NET SDK (a newer major version is accepted via rollForward) and retry.",
            category:="VBScriptingNuGet",
            defaultSeverity:=DiagnosticSeverity.Error,
            isEnabledByDefault:=True)

        Private Shared ReadOnly s_packageNotFound As New DiagnosticDescriptor(
            id:="VBI1007",
            title:="NuGet package was not found",
            messageFormat:="Package '{0}' version '{1}' was not found in the configured NuGet sources. Verify the package name and version.",
            category:="VBScriptingNuGet",
            defaultSeverity:=DiagnosticSeverity.Error,
            isEnabledByDefault:=True)

        Private Shared ReadOnly s_networkFailure As New DiagnosticDescriptor(
            id:="VBI1008",
            title:="Unable to connect to NuGet source",
            messageFormat:="Unable to connect to the NuGet source(s). Check your network connection or configure an offline source.",
            category:="VBScriptingNuGet",
            defaultSeverity:=DiagnosticSeverity.Error,
            isEnabledByDefault:=True)

        Private Shared ReadOnly s_restoreFailed As New DiagnosticDescriptor(
            id:="VBI1009",
            title:="NuGet restore failed",
            messageFormat:="NuGet restore failed (exit code {1}): {0}",
            category:="VBScriptingNuGet",
            defaultSeverity:=DiagnosticSeverity.Error,
            isEnabledByDefault:=True)

        Private Shared ReadOnly s_net48Native As New DiagnosticDescriptor(
            id:="VBI1010",
            title:="Package contains native assets unsupported by the .NET Framework host",
            messageFormat:="Package '{0}' contains native assets. A .NET Framework host cannot probe native libraries; run this script in the .NET (net10.0) host instead.",
            category:="VBScriptingNuGet",
            defaultSeverity:=DiagnosticSeverity.Error,
            isEnabledByDefault:=True)

        Private Shared ReadOnly s_missingNativeAssets As New DiagnosticDescriptor(
            id:="VBI1011",
            title:="Package provides no native assets for the current RID",
            messageFormat:="Package '{0}' provides no native assets for the current RID '{1}'. Native assets were found for: {2}. Run on a supported RID or use a package version that ships native assets for '{1}'.",
            category:="VBScriptingNuGet",
            defaultSeverity:=DiagnosticSeverity.Error,
            isEnabledByDefault:=True)

        Private Sub New()
        End Sub

        Friend Shared Function CreateSdkMissingDiagnostic(location As Location) As Diagnostic
            Return Diagnostic.Create(s_sdkMissing, location)
        End Function

        ''' <summary>
        ''' Diagnostic for a directory-level <c>NuGet.config</c> the host could not read from the script's
        ''' directory chain or could not mirror into the cache key directory. Silently ignoring
        ''' the file would restore from the wrong sources, so it is an error naming the first failing path
        ''' and the reason.
        ''' </summary>
        Friend Shared Function CreateConfigFailureDiagnostic(path As String, reason As String, location As Location) As Diagnostic
            Return Diagnostic.Create(s_configFailure, location, If(path, String.Empty), If(String.IsNullOrEmpty(reason), "unknown error", reason))
        End Function

        Friend Shared Function CreateNet48NativeDiagnostic(packageName As String, location As Location) As Diagnostic
            Return Diagnostic.Create(s_net48Native, location, packageName)
        End Function

        ''' <summary>
        ''' Clarity diagnostic for a package whose native assets do not cover the current RID.
        ''' RID fallback belongs to restore; this only surfaces what the restore selected so a missing native
        ''' probe root is reported up front instead of failing later with DllNotFoundException.
        ''' </summary>
        Friend Shared Function CreateMissingNativeAssetsDiagnostic(packageName As String, rid As String, candidateRids As ImmutableArray(Of String), location As Location) As Diagnostic
            Return Diagnostic.Create(s_missingNativeAssets, location, packageName, rid, String.Join(", ", candidateRids))
        End Function

        ''' <summary>
        ''' Maximum length of the restore transcript a diagnostic carries. A restore can print a long MSBuild
        ''' log; the diagnostic only needs enough of it to be actionable.
        ''' </summary>
        Friend Const MaxFailureDetailLength As Integer = 8000

        ''' <summary>
        ''' Composes the transcript a restore-failure diagnostic is built from: stderr first (NuGet's own error
        ''' channel, where every existing marker lives) and then stdout, because MSBuild writes NETSDK-style
        ''' errors — including <c>NETSDK1013</c> — to stdout. Blank lines are dropped, duplicate lines are kept
        ''' once, and the result is capped at <see cref="MaxFailureDetailLength"/>.
        ''' </summary>
        Friend Shared Function ComposeFailureDetail(stderr As String, stdout As String) As String
            Dim builder As New StringBuilder()
            Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
            AppendFailureLines(builder, seen, stderr)
            AppendFailureLines(builder, seen, stdout)
            Return builder.ToString().Trim()
        End Function

        Private Shared Sub AppendFailureLines(builder As StringBuilder, seen As HashSet(Of String), text As String)
            If String.IsNullOrWhiteSpace(text) Then
                Return
            End If

            For Each rawLine In text.Split(New Char() {Convert.ToChar(13), Convert.ToChar(10)}, StringSplitOptions.RemoveEmptyEntries)
                Dim line = rawLine.Trim()
                If line.Length = 0 OrElse Not seen.Add(line) Then
                    Continue For
                End If

                If builder.Length + line.Length + Environment.NewLine.Length > MaxFailureDetailLength Then
                    Exit For
                End If

                builder.AppendLine(line)
            Next
        End Sub

        ''' <summary>
        ''' Maps a raw restore outcome to a single <c>#R</c>-anchored diagnostic:
        ''' SDK missing, NU1101 package-not-found, network failure, or a generic first-line summary
        ''' with the exit code. Pure: caller supplies the already-located <paramref name="location"/> and the
        ''' affected package identity.
        ''' </summary>
        Friend Shared Function ExitCodeToDiagnostic(
            exitCode As Integer,
            stderr As String,
            location As Location,
            packageName As String,
            packageVersion As String) As Diagnostic

            stderr = If(stderr, String.Empty)

            If exitCode = DotNetMissingExitCode OrElse LooksLikeSdkMissing(stderr) Then
                Return Diagnostic.Create(s_sdkMissing, location)
            End If

            If ContainsOrdinalIgnoreCase(stderr, "NU1101") Then
                Return Diagnostic.Create(s_packageNotFound, location, packageName, packageVersion)
            End If

            If IsNetworkFailure(stderr) Then
                Return Diagnostic.Create(s_networkFailure, location)
            End If

            Dim summary = FirstNonEmptyLine(stderr)
            Return Diagnostic.Create(s_restoreFailed, location, summary, exitCode.ToString())
        End Function

        ''' <summary>
        ''' Restore-failure diagnostic resolved against the request set being restored. Unlike
        ''' the raw overload, it ties the <c>#R</c> anchor and the message package identity to whichever
        ''' requested package NuGet actually named in <paramref name="stderr"/> (NU1101 / NU1107), so a
        ''' multi-package submission anchors and names the package NuGet reported.
        ''' When NuGet names a package outside the requests (e.g. a missing transitive dependency) or no id
        ''' can be extracted, the anchor falls back to the most recently added request and the message falls
        ''' back to the exit-code summary instead of claiming a specific requested package failed.
        ''' </summary>
        Friend Shared Function ExitCodeToDiagnostic(
            exitCode As Integer,
            stderr As String,
            requests As ImmutableArray(Of NuGetPackageRequest)) As Diagnostic

            Dim anchor As Location = Location.None
            If Not requests.IsDefaultOrEmpty Then
                ' The last entry is the request that most recently joined the session set; when NuGet does
                ' not name one of our requests it is the best guess for what broke this restore.
                anchor = requests(requests.Length - 1).Location
            End If

            Dim mentionedId = TryExtractPackageId(stderr)
            If mentionedId IsNot Nothing Then
                For Each request In requests
                    If String.Equals(request.Name, mentionedId, StringComparison.OrdinalIgnoreCase) Then
                        Return ExitCodeToDiagnostic(exitCode, stderr, request.Location, request.Name, request.Version)
                    End If
                Next
            End If

            If ContainsOrdinalIgnoreCase(stderr, "NU1101") OrElse ContainsOrdinalIgnoreCase(stderr, "NU1107") Then
                ' The failing id is not one of the direct requests; surface the raw NuGet line (it names the
                ' real package) rather than a package-not-found message with an empty identity.
                Return Diagnostic.Create(s_restoreFailed, anchor, FirstNonEmptyLine(stderr), exitCode.ToString())
            End If

            Return ExitCodeToDiagnostic(exitCode, stderr, anchor, String.Empty, String.Empty)
        End Function

        ''' <summary>
        ''' Best-effort extraction of the package id NuGet named as failing in <paramref name="stderr"/>
        ''' (NU1101 "Unable to find package X", NU1107 "Version conflict detected for X"). Returns Nothing
        ''' when no marker is present or no id token follows. Pure, no file access.
        ''' </summary>
        Friend Shared Function TryExtractPackageId(stderr As String) As String
            If String.IsNullOrEmpty(stderr) Then
                Return Nothing
            End If

            Dim markers As String() = {
                "unable to find package ",
                "version conflict detected for "}
            For Each marker In markers
                Dim markerIndex = stderr.IndexOf(marker, StringComparison.OrdinalIgnoreCase)
                If markerIndex < 0 Then
                    Continue For
                End If

                Dim start = markerIndex + marker.Length
                While start < stderr.Length AndAlso (stderr.Chars(start) = "'"c OrElse stderr.Chars(start) = """"c OrElse stderr.Chars(start) = " "c)
                    start += 1
                End While
                If start >= stderr.Length Then
                    Continue For
                End If

                Dim endIndex = start
                While endIndex < stderr.Length AndAlso Not Char.IsWhiteSpace(stderr.Chars(endIndex))
                    endIndex += 1
                End While
                If endIndex <= start Then
                    Continue For
                End If

                ' A NuGet id can contain '.' and '-' but never whitespace; trailing punctuation (period that
                ' terminates the sentence, quotes, comma) is trimmed off.
                Dim id = stderr.Substring(start, endIndex - start).TrimEnd("."c, ","c, ";"c, ":"c, "'"c, """"c)
                If id.Length > 0 Then
                    Return id
                End If
            Next
            Return Nothing
        End Function

        Private Shared Function IsNetworkFailure(stderr As String) As Boolean
            Dim markers = New String() {
                "NU1301",
                "NU1302",
                "Unable to load the service index",
                "unreachable",
                "No such host is known",
                "no such host",
                "timed out",
                "timeout",
                "Could not connect",
                "Unable to connect",
                "Failed to retrieve information"
            }
            For Each marker In markers
                If ContainsOrdinalIgnoreCase(stderr, marker) Then
                    Return True
                End If
            Next
            Return False
        End Function

        Private Shared Function LooksLikeSdkMissing(stderr As String) As Boolean
            Dim markers = New String() {
                "You must install .NET",
                "not possible to find any installed .NET Core SDKs",
                "not possible to find any installed .NET SDKs",
                "requires a .NET SDK"
            }
            For Each marker In markers
                If ContainsOrdinalIgnoreCase(stderr, marker) Then
                    Return True
                End If
            Next
            Return False
        End Function

        Private Shared Function FirstNonEmptyLine(text As String) As String
            Dim reader = New System.IO.StringReader(text)
            Dim first As String = Nothing
            Dim line As String = reader.ReadLine()
            While line IsNot Nothing
                If Not String.IsNullOrWhiteSpace(line) Then
                    Dim trimmed = line.Trim()
                    ' MSBuild writes its real error to stdout after localization noise ("正在确定要还原的项目…")
                    ' and, depending on the SDK, the first non-blank line is that progress line. Prefer the
                    ' first line that names an error; fall back to the first non-blank line when there is none.
                    If LooksLikeErrorLine(trimmed) Then
                        Return trimmed
                    End If
                    If first Is Nothing Then
                        first = trimmed
                    End If
                End If
                line = reader.ReadLine()
            End While
            Return If(first, String.Empty)
        End Function

        Private Shared Function LooksLikeErrorLine(line As String) As Boolean
            Return ContainsOrdinalIgnoreCase(line, "error") OrElse line.Contains("错误")
        End Function

        Private Shared Function ContainsOrdinalIgnoreCase(text As String, candidate As String) As Boolean
            Return text.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0
        End Function
    End Class
End Namespace
