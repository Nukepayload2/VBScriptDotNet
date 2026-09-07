' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable
Imports Microsoft.CodeAnalysis.Scripting.Hosting

Namespace Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

    ''' <summary>
    ''' Resolves #R "nuget:&lt;name&gt;, &lt;version&gt;" against a <see cref="NuGetPackageSession"/>. Multiple
    ''' assets are returned as an array whose index 0 is the package's main asset and the remaining entries
    ''' its dependency closure. Read-only: it only queries the session.
    ''' </summary>
    Friend NotInheritable Class NuGetPackageResolverImpl
        Inherits NuGetPackageResolver

        Private ReadOnly _session As NuGetPackageSession

        Friend Sub New(session As NuGetPackageSession)
            If session Is Nothing Then
                Throw New ArgumentNullException(NameOf(session))
            End If
            _session = session
        End Sub

        Friend Overrides Function ResolveNuGetPackage(packageName As String, packageVersion As String) As ImmutableArray(Of String)
            If String.IsNullOrEmpty(packageVersion) Then
                Return ImmutableArray(Of String).Empty
            End If
            Return _session.TryGetCompilePaths(packageName, packageVersion)
        End Function
    End Class
End Namespace
