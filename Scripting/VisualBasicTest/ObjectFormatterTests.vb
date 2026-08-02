' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting
Imports ObjectFormatterFixtures
Imports Xunit

Public Class ObjectFormatterTests
    Inherits ObjectFormatterTestBase

    Private Shared ReadOnly s_formatter As ObjectFormatter = New TestVisualBasicObjectFormatter()

    <Fact>
    Public Sub InlineCharacters()
        Assert.Equal("ChrW(20)", s_formatter.FormatObject(ChrW(20), SingleLineOptions))
        Assert.Equal("vbBack", s_formatter.FormatObject(ChrW(&H8), SingleLineOptions))
    End Sub

    <Fact>
    Public Sub QuotedStrings()
        Dim s = "a" & ChrW(&HFFFE) & ChrW(&HFFFF) & vbCrLf & "b"

        Dim options = New PrintOptions With {.NumberRadix = ObjectFormatterHelpers.NumberRadixHexadecimal}
        Dim withQuotes = New TestVisualBasicObjectFormatter(quoteStringsAndCharacters:=True)
        Dim withoutQuotes = New TestVisualBasicObjectFormatter(quoteStringsAndCharacters:=False)

        ' ObjectFormatter should substitute spaces for non-printable characters
        Assert.Equal("""a"" & ChrW(&HFFFE) & ChrW(&HFFFF) & vbCrLf & ""b""", withQuotes.FormatObject(s, options))
        Assert.Throws(Of ArgumentException)(Sub() withoutQuotes.FormatObject(s, options))
    End Sub

    <Fact>
    Public Sub QuotedStrings_EscapesAllVisualBasicDoubleQuoteCharacters()
        Dim halfWidth = """"c
        Dim fullWidth = ChrW(&HFF02)
        Dim leftSmart = ChrW(&H201C)
        Dim rightSmart = ChrW(&H201D)
        Dim s = "a" & halfWidth & fullWidth & leftSmart & rightSmart & "b"

        Dim formatted = s_formatter.FormatObject(s, New PrintOptions With {.NumberRadix = ObjectFormatterHelpers.NumberRadixHexadecimal})

        Assert.StartsWith(String.Concat(halfWidth, "a", New String(halfWidth, 3)), formatted)
        Assert.Contains("ChrW(&HFF02)", formatted)
        Assert.Contains("ChrW(&H201C)", formatted)
        Assert.Contains("ChrW(&H201D)", formatted)

        Dim options = ScriptOptions.Default.
            AddReferences(GetType(Microsoft.VisualBasic.Strings).Assembly).
            AddImports("Microsoft.VisualBasic")
        Assert.Equal(s, VisualBasicScript.EvaluateAsync("? " & formatted, options).Result)
    End Sub

    <Fact>
    Public Sub Objects()
        Dim str As String
        Dim nested As Object = New Outer.Nested(Of Integer)()

        str = s_formatter.FormatObject(nested, SingleLineOptions)
        Assert.Equal("Outer.Nested(Of Integer) { A=1, B=2 }", str)

        str = s_formatter.FormatObject(nested, HiddenOptions)
        Assert.Equal("Outer.Nested(Of Integer)", str)

        str = s_formatter.FormatObject(A(Of Integer).X, HiddenOptions)
        Assert.Equal("A(Of Integer).B(Of Integer)", str)

        Dim obj As Object = New A(Of Integer).B(Of Boolean).C.D(Of String, Double).E()
        str = s_formatter.FormatObject(obj, HiddenOptions)
        Assert.Equal("A(Of Integer).B(Of Boolean).C.D(Of String, Double).E", str)

        Dim sort = New Sort()
        str = New TestVisualBasicObjectFormatter(maximumLineLength:=51).FormatObject(sort, SingleLineOptions)
        Assert.Equal("Sort { ab=1, aB2=-1, Ac=-1, Ad=1, ad2=-1, aE=1, aF=...", str)
        Assert.Equal(51 + 3, str.Length)

        str = New TestVisualBasicObjectFormatter(maximumLineLength:=5).FormatObject(sort, SingleLineOptions)
        Assert.Equal("Sort ...", str)
        Assert.Equal(5 + 3, str.Length)

        str = New TestVisualBasicObjectFormatter(maximumLineLength:=4).FormatObject(sort, SingleLineOptions)
        Assert.Equal("Sort...", str)

        str = New TestVisualBasicObjectFormatter(maximumLineLength:=3).FormatObject(sort, SingleLineOptions)
        Assert.Equal("Sor...", str)

        str = New TestVisualBasicObjectFormatter(maximumLineLength:=2).FormatObject(sort, SingleLineOptions)
        Assert.Equal("So...", str)

        str = New TestVisualBasicObjectFormatter(maximumLineLength:=1).FormatObject(sort, SingleLineOptions)
        Assert.Equal("S...", str)

        str = New TestVisualBasicObjectFormatter(maximumLineLength:=80).FormatObject(sort, SingleLineOptions)
        Assert.Equal("Sort { ab=1, aB2=-1, Ac=-1, Ad=1, ad2=-1, aE=1, aF=-1, AG=1 }", str)
    End Sub

    <Fact>
    Public Sub ArrayMethodParameters()
        Dim result = New TestVisualBasicObjectFormatter(maximumLineLength:=100).FormatMethodSignature(Signatures.Arrays)
        Assert.Equal("ObjectFormatterFixtures.Signatures.ArrayParameters(Integer(), Integer(,), Integer(,,))", result)
    End Sub

    <Fact>
    Public Sub EscapeWithoutQuotes()
        Dim primitiveFormatter As New TestPrimitiveObjectFormatter()
        Assert.Throws(Of ArgumentException)(Sub() primitiveFormatter.TestEscapeStringWithoutQuotes())
        Assert.Throws(Of ArgumentException)(Sub() primitiveFormatter.TestEscapeCharWithoutQuotes())
    End Sub

    Private Class TestPrimitiveObjectFormatter
        Inherits VisualBasicPrimitiveFormatter

        Public Sub TestEscapeStringWithoutQuotes()
            FormatLiteral("a", useQuotes:=False, escapeNonPrintable:=True)
        End Sub

        Public Sub TestEscapeCharWithoutQuotes()
            FormatLiteral("a"c, useQuotes:=False, escapeNonPrintable:=True)
        End Sub
    End Class

    ' TODO: port tests from C#
End Class
