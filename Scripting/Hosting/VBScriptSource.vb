' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' Script source in host-owned form: the text plus the path <c>#Load</c> and diagnostics are anchored on.
    ''' Keeps <c>Microsoft.CodeAnalysis.Text.SourceText</c> out of the public seam.
    ''' </summary>
    Public NotInheritable Class VBScriptSource

        Private ReadOnly _text As String
        Private ReadOnly _filePath As String

        Public Sub New(text As String)
            Me.New(text, Nothing)
        End Sub

        Public Sub New(text As String, filePath As String)
            _text = If(text, String.Empty)
            _filePath = filePath
        End Sub

        Public ReadOnly Property Text As String
            Get
                Return _text
            End Get
        End Property

        ''' <summary>Source path, or <see langword="Nothing"/> for an in-memory snippet.</summary>
        Public ReadOnly Property FilePath As String
            Get
                Return _filePath
            End Get
        End Property

        Public Overrides Function ToString() As String
            If String.IsNullOrEmpty(_filePath) Then
                Return "<snippet>"
            End If

            Return _filePath
        End Function

    End Class

End Namespace
