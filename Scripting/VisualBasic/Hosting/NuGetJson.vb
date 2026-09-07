' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text

' Minimal, dependency-free JSON reader for project.assets.json (design §E4). The restore engine is compiled
' for both netstandard2.0 and net10.0, so it cannot lean on System.Text.Json. Only the subset of JSON used
' by NuGet's assets file is modelled: objects, arrays, strings, numbers/booleans/null kept as literals.
Namespace Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

    Friend MustInherit Class NuGetJsonValue
    End Class

    Friend NotInheritable Class NuGetJsonNull
        Inherits NuGetJsonValue

        Friend Shared ReadOnly Instance As New NuGetJsonNull()

        Private Sub New()
        End Sub
    End Class

    Friend NotInheritable Class NuGetJsonLiteral
        Inherits NuGetJsonValue

        Friend Sub New(value As String)
            _value = value
        End Sub

        Private ReadOnly _value As String

        Friend ReadOnly Property Value As String
            Get
                Return _value
            End Get
        End Property
    End Class

    Friend NotInheritable Class NuGetJsonString
        Inherits NuGetJsonValue

        Friend Sub New(value As String)
            _value = value
        End Sub

        Private ReadOnly _value As String

        Friend ReadOnly Property Value As String
            Get
                Return _value
            End Get
        End Property
    End Class

    Friend NotInheritable Class NuGetJsonArray
        Inherits NuGetJsonValue

        Private ReadOnly _items As New List(Of NuGetJsonValue)()

        Friend Sub Add(value As NuGetJsonValue)
            _items.Add(value)
        End Sub

        Friend ReadOnly Property Count As Integer
            Get
                Return _items.Count
            End Get
        End Property

        Friend Default ReadOnly Property Item(index As Integer) As NuGetJsonValue
            Get
                Return _items(index)
            End Get
        End Property
    End Class

    Friend NotInheritable Class NuGetJsonObject
        Inherits NuGetJsonValue

        Private ReadOnly _members As New Dictionary(Of String, NuGetJsonValue)(StringComparer.Ordinal)

        Friend Sub Add(name As String, value As NuGetJsonValue)
            _members(name) = value
        End Sub

        Friend Function TryGetMember(name As String) As NuGetJsonValue
            Dim value As NuGetJsonValue = Nothing
            If _members.TryGetValue(name, value) Then
                Return value
            End If
            Return Nothing
        End Function

        Friend Function TryGetString(name As String, ByRef value As String) As Boolean
            Dim member = TryGetMember(name)
            Dim str = TryCast(member, NuGetJsonString)
            If str IsNot Nothing Then
                value = str.Value
                Return True
            End If
            value = Nothing
            Return False
        End Function

        Friend Function EnumerateMembers() As IEnumerable(Of KeyValuePair(Of String, NuGetJsonValue))
            Return _members
        End Function
    End Class

    ''' <summary>
    ''' Recursive-descent JSON parser. Throws <see cref="InvalidOperationException"/> on malformed input;
    ''' designed for machine-generated NuGet assets files, not for arbitrary JSON.
    ''' </summary>
    Friend NotInheritable Class NuGetJson

        Private Sub New()
        End Sub

        Friend Shared Function Parse(text As String) As NuGetJsonValue
            If text Is Nothing Then
                Throw New ArgumentNullException(NameOf(text))
            End If
            Dim parser As New Parser(text)
            Dim value = parser.ParseValue()
            parser.SkipWhitespace()
            If Not parser.EndOfText Then
                Throw New InvalidOperationException("Trailing content after JSON value.")
            End If
            Return value
        End Function

        Private NotInheritable Class Parser

            Private ReadOnly _text As String
            Private _position As Integer

            Friend Sub New(text As String)
                _text = text
            End Sub

            Friend ReadOnly Property EndOfText As Boolean
                Get
                    Return _position >= _text.Length
                End Get
            End Property

            Friend Sub SkipWhitespace()
                While Not EndOfText AndAlso IsWhitespace(Current())
                    _position += 1
                End While
            End Sub

            Private Function Current() As Char
                Return _text.Chars(_position)
            End Function

            Friend Function ParseValue() As NuGetJsonValue
                SkipWhitespace()
                If EndOfText Then
                    Throw New InvalidOperationException("Unexpected end of JSON.")
                End If

                Dim c = Current()
                Select Case c
                    Case "{"c
                        Return ParseObject()
                    Case "["c
                        Return ParseArray()
                    Case """"c
                        Return New NuGetJsonString(ParseString())
                    Case "t"c, "f"c, "n"c
                        Return New NuGetJsonLiteral(ParseLiteral())
                    Case Else
                        If c = "-"c OrElse Char.IsDigit(c) Then
                            Return New NuGetJsonLiteral(ParseNumber())
                        End If
                End Select
                Throw New InvalidOperationException("Unexpected character '" & c & "' in JSON.")
            End Function

            Private Function ParseObject() As NuGetJsonObject
                _position += 1 ' consume '{'
                Dim result As New NuGetJsonObject()
                SkipWhitespace()
                If Not EndOfText AndAlso Current() = "}"c Then
                    _position += 1
                    Return result
                End If

                While True
                    SkipWhitespace()
                    If EndOfText OrElse Current() <> """"c Then
                        Throw New InvalidOperationException("Expected a member name in JSON object.")
                    End If
                    Dim name = ParseString()
                    SkipWhitespace()
                    If EndOfText OrElse Current() <> ":"c Then
                        Throw New InvalidOperationException("Expected ':' after JSON member name.")
                    End If
                    _position += 1
                    result.Add(name, ParseValue())

                    SkipWhitespace()
                    If EndOfText Then
                        Throw New InvalidOperationException("Unterminated JSON object.")
                    End If
                    Dim comma = Current()
                    If comma = ","c Then
                        _position += 1
                    ElseIf comma = "}"c Then
                        _position += 1
                        Return result
                    Else
                        Throw New InvalidOperationException("Expected ',' or '}' in JSON object.")
                    End If
                End While
                Return result
            End Function

            Private Function ParseArray() As NuGetJsonArray
                _position += 1 ' consume '['
                Dim result As New NuGetJsonArray()
                SkipWhitespace()
                If Not EndOfText AndAlso Current() = "]"c Then
                    _position += 1
                    Return result
                End If

                While True
                    result.Add(ParseValue())
                    SkipWhitespace()
                    If EndOfText Then
                        Throw New InvalidOperationException("Unterminated JSON array.")
                    End If
                    Dim comma = Current()
                    If comma = ","c Then
                        _position += 1
                    ElseIf comma = "]"c Then
                        _position += 1
                        Return result
                    Else
                        Throw New InvalidOperationException("Expected ',' or ']' in JSON array.")
                    End If
                End While
                Return result
            End Function

            Private Function ParseString() As String
                _position += 1 ' consume opening '"'
                Dim builder As New StringBuilder()
                While Not EndOfText
                    Dim c = Current()
                    If c = """"c Then
                        _position += 1
                        Return builder.ToString()
                    End If
                    If c = "\"c Then
                        _position += 1
                        If EndOfText Then
                            Throw New InvalidOperationException("Unterminated JSON string escape.")
                        End If
                        Dim escaped = Current()
                        _position += 1
                        Select Case escaped
                            Case """"c
                                builder.Append(""""c)
                            Case "\"c
                                builder.Append("\"c)
                            Case "/"c
                                builder.Append("/"c)
                            Case "b"c
                                builder.Append(Convert.ToChar(8))
                            Case "f"c
                                builder.Append(Convert.ToChar(12))
                            Case "n"c
                                builder.Append(Convert.ToChar(10))
                            Case "r"c
                                builder.Append(Convert.ToChar(13))
                            Case "t"c
                                builder.Append(Convert.ToChar(9))
                            Case "u"c
                                builder.Append(ParseUnicodeEscape())
                            Case Else
                                Throw New InvalidOperationException("Invalid JSON string escape '\" & escaped & "'.")
                        End Select
                    Else
                        builder.Append(c)
                        _position += 1
                    End If
                End While
                Throw New InvalidOperationException("Unterminated JSON string.")
            End Function

            Private Function ParseUnicodeEscape() As Char
                If _position + 4 > _text.Length Then
                    Throw New InvalidOperationException("Incomplete JSON \\u escape.")
                End If
                Dim hex = _text.Substring(_position, 4)
                _position += 4
                Dim code As Integer
                If Not Integer.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, code) Then
                    Throw New InvalidOperationException("Invalid JSON \\u escape.")
                End If
                Return Convert.ToChar(code)
            End Function

            Private Function ParseLiteral() As String
                Dim start = _position
                While Not EndOfText AndAlso (Char.IsLetter(Current()) OrElse Current() = "_"c)
                    _position += 1
                End While
                Return _text.Substring(start, _position - start)
            End Function

            Private Function ParseNumber() As String
                Dim start = _position
                While Not EndOfText
                    Dim c = Current()
                    If Char.IsDigit(c) OrElse c = "-"c OrElse c = "+"c OrElse c = "."c OrElse c = "e"c OrElse c = "E"c Then
                        _position += 1
                    Else
                        Exit While
                    End If
                End While
                Return _text.Substring(start, _position - start)
            End Function

            Private Shared Function IsWhitespace(c As Char) As Boolean
                Return c = " "c OrElse c = Convert.ToChar(9) OrElse c = Convert.ToChar(10) OrElse c = Convert.ToChar(13)
            End Function
        End Class
    End Class
End Namespace
