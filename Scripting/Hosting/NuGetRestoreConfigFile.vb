' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' One directory-level <c>NuGet.config</c> to mirror into the restore cache directory: the path relative to
    ''' the cache directory and its raw bytes. Keeping bytes rather than text makes the mirror byte-exact (the
    ''' cache key already fingerprints the same content).
    ''' </summary>
    Public NotInheritable Class NuGetRestoreConfigFile

        Private ReadOnly _relativePath As String
        Private ReadOnly _content As ImmutableArray(Of Byte)

        Public Sub New(relativePath As String, content As ImmutableArray(Of Byte))
            _relativePath = If(relativePath, String.Empty)
            _content = If(content.IsDefault, ImmutableArray(Of Byte).Empty, content)
        End Sub

        ''' <summary>Path relative to the cache directory, for example <c>sub\NuGet.config</c>.</summary>
        Public ReadOnly Property RelativePath As String
            Get
                Return _relativePath
            End Get
        End Property

        ''' <summary>Raw file content to write at <see cref="RelativePath"/>.</summary>
        Public ReadOnly Property Content As ImmutableArray(Of Byte)
            Get
                Return _content
            End Get
        End Property

    End Class

End Namespace
