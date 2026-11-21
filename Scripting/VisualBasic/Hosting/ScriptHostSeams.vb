' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.Text

Namespace Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

    ''' <summary>
    ''' The restore seam the VB script engine consumes. It mirrors the shared
    ''' <c>INuGetRestoreCoordinator</c> shape, but it is declared here so the hosting facade can supply a
    ''' coordinator without the shared scripting assembly granting it internals: the facade only knows this
    ''' interface (through InternalsVisibleTo on VB scripting), while the VB-side
    ''' <see cref="NuGetRestoreCoordinator"/> implements both this interface and the shared one the vendored
    ''' command-line runner still takes.
    ''' </summary>
    Friend Interface IScriptRestoreCoordinator

        ''' <summary>
        ''' Pre-scans the code about to be compiled, restores any referenced NuGet packages, and returns
        ''' diagnostics that should block the submission (anchored at the offending <c>#R</c> directive).
        ''' </summary>
        Function PrepareCompilationAsync(code As SourceText, filePath As String, options As ScriptOptions, cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of Diagnostic))

    End Interface

    ''' <summary>
    ''' <c>#R "nuget:…"</c> resolver that both hosts can create from a delegate. It derives from the shared
    ''' internal <c>NuGetPackageResolver</c> because that is the type the metadata reference resolver accepts;
    ''' deriving it here (VB scripting has the shared assembly's internals) is what lets the hosting facade
    ''' pass a resolver without an InternalsVisibleTo on the shared scripting assembly.
    ''' </summary>
    Friend Class ScriptNuGetPackageResolver
        Inherits NuGetPackageResolver

        Private ReadOnly _resolve As Func(Of String, String, ImmutableArray(Of String))

        Friend Sub New(resolve As Func(Of String, String, ImmutableArray(Of String)))
            If resolve Is Nothing Then
                Throw New ArgumentNullException(NameOf(resolve))
            End If

            _resolve = resolve
        End Sub

        Friend Overrides Function ResolveNuGetPackage(packageName As String, packageVersion As String) As ImmutableArray(Of String)
            Return _resolve(packageName, packageVersion)
        End Function

    End Class

    ''' <summary>
    ''' Host-facing factories for the shared internals the script hosts need to assemble
    ''' <see cref="ScriptOptions"/>. Keeping them here means a host never names
    ''' <c>RuntimeMetadataReferenceResolver</c> or any other shared internal directly.
    ''' </summary>
    Friend NotInheritable Class ScriptHostResolvers

        Private Sub New()
        End Sub

        ''' <summary>Platform metadata reference resolver with the same shape vbi's script path uses.</summary>
        Friend Shared Function CreateMetadataResolver(baseDirectory As String, Optional packageResolver As NuGetPackageResolver = Nothing) As MetadataReferenceResolver
            Return RuntimeMetadataReferenceResolver.CreateCurrentPlatformResolver(
                searchPaths:=ImmutableArray(Of String).Empty,
                baseDirectory:=baseDirectory,
                fileReferenceProvider:=Nothing,
                packageResolver:=packageResolver)
        End Function

        ''' <summary>
        ''' Host-facing accessor for <see cref="ScriptOptions.ParseOptions"/>, which is internal to the shared
        ''' scripting assembly. The facade needs it to parse the submitted code the same way the compiler does.
        ''' </summary>
        Friend Shared Function GetParseOptions(options As ScriptOptions) As ParseOptions
            If options Is Nothing Then
                Return Nothing
            End If

            Return options.ParseOptions
        End Function

    End Class

End Namespace
