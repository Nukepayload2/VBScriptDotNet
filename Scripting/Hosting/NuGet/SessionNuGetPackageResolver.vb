' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' Default <see cref="INuGetPackageResolver"/>: reads the compile-asset cache the restore coordination layer
    ''' writes into <see cref="NuGetPackageSession"/>. Read-only by construction; it never restores.
    ''' </summary>
    Friend NotInheritable Class SessionNuGetPackageResolver
        Implements INuGetPackageResolver

        Private ReadOnly _session As NuGetPackageSession

        Friend Sub New(session As NuGetPackageSession)
            If session Is Nothing Then
                Throw New ArgumentNullException(NameOf(session))
            End If

            _session = session
        End Sub

        Public Function Resolve(packageName As String, packageVersion As String) As ImmutableArray(Of String) Implements INuGetPackageResolver.Resolve
            If String.IsNullOrEmpty(packageVersion) Then
                Return ImmutableArray(Of String).Empty
            End If

            Return _session.TryGetCompilePaths(packageName, packageVersion)
        End Function

    End Class

End Namespace
