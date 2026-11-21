' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.Text

Namespace Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

    ''' <summary>
    ''' Generates the temporary restore project (<c>.vbproj</c> + <c>global.json</c>) for a cache key.
    ''' Pure and byte-stable: the text is a function of the key ingredients only (canonical package set,
    ''' host TFM, RID, framework references, net48 flag) and carries no time or machine fingerprint, so the
    ''' same key always writes the same bytes and NuGet's no-op restore holds.
    ''' </summary>
    Friend NotInheritable Class NuGetProjectGenerator

        Private Const ReferenceAssembliesPackagePrefix As String = "Microsoft.NETFramework.ReferenceAssemblies."
        Private Const ReferenceAssembliesVersion As String = "1.0.3"

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Project XML for the temporary restore project. <paramref name="net48"/> mirrors the key's
        ''' .NET Framework flag and, when set, pulls in the reference assemblies package matching
        ''' <paramref name="targetFramework"/> (a net472 host gets the net472 package, not a hard-coded net48
        ''' one); precedent <c>Interactive\vbi\vbi.vbproj</c>.
        ''' </summary>
        Friend Shared Function GenerateProjectXml(
            targetFramework As String,
            net48 As Boolean,
            packages As ImmutableArray(Of NuGetPackageRequest),
            Optional runtimeIdentifier As String = Nothing,
            Optional frameworkReferences As ImmutableArray(Of String) = Nothing) As String

            Dim sorted = New List(Of NuGetPackageRequest)()
            For Each package In packages
                sorted.Add(package)
            Next
            sorted.Sort(Function(left, right) String.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase))

            Dim sb As New StringBuilder()
            sb.AppendLine("<?xml version=""1.0"" encoding=""utf-8""?>")
            sb.AppendLine("<Project Sdk=""Microsoft.NET.Sdk"">")
            sb.AppendLine("  <PropertyGroup>")
            sb.AppendLine("    <OutputType>Library</OutputType>")
            sb.AppendLine("    <TargetFramework>" & targetFramework & "</TargetFramework>")
            If Not String.IsNullOrEmpty(runtimeIdentifier) Then
                sb.AppendLine("    <RuntimeIdentifier>" & runtimeIdentifier & "</RuntimeIdentifier>")
            End If
            sb.AppendLine("    <RestoreProjectStyle>PackageReference</RestoreProjectStyle>")
            sb.AppendLine("    <NuGetAudit>false</NuGetAudit>")
            sb.AppendLine("  </PropertyGroup>")
            sb.AppendLine("  <ItemGroup>")

            For Each package In sorted
                sb.AppendLine("    <PackageReference Include=""" & package.Name & """ Version=""" & package.Version & """ />")
            Next

            If net48 Then
                ' The reference-assemblies package is per TFM, so a 4.7.2 host must restore the net472
                ' package: a net48 package with a net472 project is the wrong reference surface (and machines
                ' without the targeting pack cannot build the restored graph).
                sb.AppendLine("    <PackageReference Include=""" & GetReferenceAssembliesPackageId(targetFramework) & """ Version=""" & ReferenceAssembliesVersion & """>")
                sb.AppendLine("      <PrivateAssets>all</PrivateAssets>")
                sb.AppendLine("      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>")
                sb.AppendLine("    </PackageReference>")
            End If

            sb.AppendLine("  </ItemGroup>")

            If Not frameworkReferences.IsDefaultOrEmpty Then
                sb.AppendLine("  <ItemGroup>")
                Dim orderedRefs = New List(Of String)()
                orderedRefs.AddRange(frameworkReferences)
                orderedRefs.Sort(StringComparer.Ordinal)
                For Each referenceName In orderedRefs
                    sb.AppendLine("    <FrameworkReference Include=""" & referenceName & """ />")
                Next
                sb.AppendLine("  </ItemGroup>")
            End If

            sb.Append("</Project>")
            sb.AppendLine()
            Return sb.ToString()
        End Function

        ''' <summary>
        ''' <c>global.json</c> content: pins the resolved installed SDK with a latest-major roll-forward.
        ''' Written only when a NuGet reference is present. Single-line and newline-free so the
        ''' bytes are identical on every platform for the same SDK version.
        ''' </summary>
        Friend Shared Function GenerateGlobalJson(sdkVersion As String) As String
            Return "{""sdk"":{""version"":""" & sdkVersion & """,""rollForward"":""latestMajor""}}"
        End Function

        ''' <summary>
        ''' Reference-assemblies package id for the temporary project's TFM. Only meaningful for
        ''' .NET Framework monikers; a non-moniker falls back to net48 so the generated project stays valid.
        ''' </summary>
        Friend Shared Function GetReferenceAssembliesPackageId(targetFramework As String) As String
            If Not String.IsNullOrEmpty(targetFramework) AndAlso
               targetFramework.StartsWith("net4", StringComparison.OrdinalIgnoreCase) Then
                Return ReferenceAssembliesPackagePrefix & targetFramework.ToLowerInvariant()
            End If

            Return ReferenceAssembliesPackagePrefix & "net48"
        End Function
    End Class
End Namespace
