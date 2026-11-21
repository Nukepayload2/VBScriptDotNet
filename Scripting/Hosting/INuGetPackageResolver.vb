' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' The compile-time resolution seam: turns a <c>#R "nuget:name, version"</c> directive into the assembly
    ''' paths the compiler should reference. The default implementation reads the state written by the restore
    ''' coordination layer (the <c>NuGetPackageSession</c> cache); it never restores.
    ''' </summary>
    Public Interface INuGetPackageResolver

        ''' <summary>
        ''' Compile asset paths of a restored package: index 0 is the package's main asset, the remaining entries
        ''' its dependency closure. An empty result means the package is not restored.
        ''' </summary>
        Function Resolve(packageName As String, packageVersion As String) As ImmutableArray(Of String)

    End Interface

End Namespace
