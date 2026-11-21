' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' One script diagnostic in host-owned form. <see cref="Line"/> and <see cref="Column"/> are 1-based; they
    ''' are 0 when the diagnostic is not anchored in source (for example a restore diagnostic).
    ''' </summary>
    Public NotInheritable Class VBScriptDiagnostic

        Private ReadOnly _id As String
        Private ReadOnly _severity As VBScriptDiagnosticSeverity
        Private ReadOnly _message As String
        Private ReadOnly _filePath As String
        Private ReadOnly _line As Integer
        Private ReadOnly _column As Integer

        Public Sub New(id As String, severity As VBScriptDiagnosticSeverity, message As String, filePath As String, line As Integer, column As Integer)
            _id = If(id, String.Empty)
            _severity = severity
            _message = If(message, String.Empty)
            _filePath = filePath
            _line = line
            _column = column
        End Sub

        ''' <summary>Compiler diagnostic id, for example <c>BC30201</c>.</summary>
        Public ReadOnly Property Id As String
            Get
                Return _id
            End Get
        End Property

        Public ReadOnly Property Severity As VBScriptDiagnosticSeverity
            Get
                Return _severity
            End Get
        End Property

        Public ReadOnly Property Message As String
            Get
                Return _message
            End Get
        End Property

        ''' <summary>Source path of the diagnostic, or <see langword="Nothing"/> when it is not anchored in a file.</summary>
        Public ReadOnly Property FilePath As String
            Get
                Return _filePath
            End Get
        End Property

        ''' <summary>1-based line, or 0 when <see cref="FilePath"/> is <see langword="Nothing"/>.</summary>
        Public ReadOnly Property Line As Integer
            Get
                Return _line
            End Get
        End Property

        ''' <summary>1-based column, or 0 when <see cref="FilePath"/> is <see langword="Nothing"/>.</summary>
        Public ReadOnly Property Column As Integer
            Get
                Return _column
            End Get
        End Property

        Public Overrides Function ToString() As String
            Dim location As String
            If String.IsNullOrEmpty(_filePath) Then
                location = String.Empty
            Else
                location = _filePath & "(" & _line & "," & _column & "): "
            End If

            Return location & _severity.ToString() & " " & _id & ": " & _message
        End Function

    End Class

End Namespace
