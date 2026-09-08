' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' Host-local diagnostics (not analyzer rules shipped in this assembly); analyzer release tracking and
' message-shape analysis do not apply.
#Disable Warning RS2008, RS1032

Imports System.Collections.Immutable

Namespace Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

    ''' <summary>
    ''' Restore-failure diagnostics anchored at the offending <c>#R</c> line (design §D2/§E3 translation
    ''' table). The mapping from a raw restore outcome to a diagnostic is a pure function so it can be unit
    ''' tested without spawning a process.
    ''' </summary>
    Friend NotInheritable Class NuGetRestoreDiagnostics

        ''' <summary>
        ''' Exit code returned by the production runner when <c>dotnet</c> cannot be started at all (the SDK
        ''' is not installed). Kept distinct from any real exit code <c>dotnet restore</c> can return.
        ''' </summary>
        Friend Const DotNetMissingExitCode As Integer = Integer.MinValue

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
            title:="Package contains native assets unsupported by the net48 host",
            messageFormat:="Package '{0}' contains native assets. The .NET Framework (net48) host cannot probe native libraries; run this script in the .NET (net10) host instead.",
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

        Friend Shared Function CreateNet48NativeDiagnostic(packageName As String, location As Location) As Diagnostic
            Return Diagnostic.Create(s_net48Native, location, packageName)
        End Function

        ''' <summary>
        ''' Clarity diagnostic for a package whose native assets do not cover the current RID (design §G2.4).
        ''' RID fallback belongs to restore; this only surfaces what the restore selected so a missing native
        ''' probe root is reported up front instead of failing later with DllNotFoundException.
        ''' </summary>
        Friend Shared Function CreateMissingNativeAssetsDiagnostic(packageName As String, rid As String, candidateRids As ImmutableArray(Of String), location As Location) As Diagnostic
            Return Diagnostic.Create(s_missingNativeAssets, location, packageName, rid, String.Join(", ", candidateRids))
        End Function

        ''' <summary>
        ''' Maps a raw restore outcome to a single <c>#R</c>-anchored diagnostic (design §E3 translation
        ''' table): SDK missing, NU1101 package-not-found, network failure, or a generic first-line summary
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
        ''' Restore-failure diagnostic resolved against the request set being restored (design §E3). Unlike
        ''' the raw overload, it ties the <c>#R</c> anchor and the message package identity to whichever
        ''' requested package NuGet actually named in <paramref name="stderr"/> (NU1101 / NU1107), so a
        ''' multi-package submission no longer anchors the first request while the message names a later one.
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
            Dim line As String = reader.ReadLine()
            While line IsNot Nothing
                If Not String.IsNullOrWhiteSpace(line) Then
                    Return line.Trim()
                End If
                line = reader.ReadLine()
            End While
            Return String.Empty
        End Function

        Private Shared Function ContainsOrdinalIgnoreCase(text As String, candidate As String) As Boolean
            Return text.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0
        End Function
    End Class
End Namespace
