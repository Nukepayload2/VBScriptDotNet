' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.Text

Namespace Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

    ''' <summary>
    ''' Generates the temporary restore project (<c>.vbproj</c> + <c>global.json</c>) for a cache key
    ''' (design §E). Pure and byte-stable: the text is a function of the key ingredients only (canonical
    ''' package set, host TFM, RID, framework references, net48 flag) and carries no time or machine
    ''' fingerprint, so the same key always writes the same bytes and NuGet's no-op restore (design E7) holds.
    ''' </summary>
    Friend NotInheritable Class NuGetProjectGenerator

        Private Const ReferenceAssembliesNet48Id As String = "Microsoft.NETFramework.ReferenceAssemblies.net48"
        Private Const ReferenceAssembliesNet48Version As String = "1.0.3"

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Project XML for the temporary restore project. <paramref name="net48"/> mirrors the key's net48
        ''' flag and, when set, pulls in the .NET Framework reference assemblies package (precedent
        ''' <c>Interactive\vbi\vbi.vbproj</c>).
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
                sb.AppendLine("    <PackageReference Include=""" & ReferenceAssembliesNet48Id & """ Version=""" & ReferenceAssembliesNet48Version & """>")
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
        ''' <c>global.json</c> content: pins the resolved installed SDK with a latest-major roll-forward
        ''' (design §E3). Written only when a NuGet reference is present. Single-line and newline-free so the
        ''' bytes are identical on every platform for the same SDK version.
        ''' </summary>
        Friend Shared Function GenerateGlobalJson(sdkVersion As String) As String
            Return "{""sdk"":{""version"":""" & sdkVersion & """,""rollForward"":""latestMajor""}}"
        End Function
    End Class
End Namespace
