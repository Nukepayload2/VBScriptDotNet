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
