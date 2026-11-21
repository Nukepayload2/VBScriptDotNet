' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' Read-only snapshot of one variable declared by the submissions of a session. Globals are not
    ''' included; names are compared case-insensitively when the session de-duplicates them.
    ''' </summary>
    Public NotInheritable Class VBScriptVariable

        Private ReadOnly _name As String
        Private ReadOnly _typeName As String
        Private ReadOnly _isReadOnly As Boolean
        Private ReadOnly _value As Object

        Friend Sub New(name As String, typeName As String, isReadOnly As Boolean, value As Object)
            _name = If(name, String.Empty)
            _typeName = If(typeName, String.Empty)
            _isReadOnly = isReadOnly
            _value = value
        End Sub

        ''' <summary>The variable name as the declaring submission spelled it.</summary>
        Public ReadOnly Property Name As String
            Get
                Return _name
            End Get
        End Property

        ''' <summary>The full name of the variable's type, for example <c>System.Int32</c>.</summary>
        Public ReadOnly Property TypeName As String
            Get
                Return _typeName
            End Get
        End Property

        ''' <summary><see langword="True"/> when the variable is a constant or was declared read-only.</summary>
        Public ReadOnly Property IsReadOnly As Boolean
            Get
                Return _isReadOnly
            End Get
        End Property

        ''' <summary>The value read when the snapshot was taken.</summary>
        Public ReadOnly Property Value As Object
            Get
                Return _value
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return _name & " As " & _typeName
        End Function

    End Class

End Namespace
