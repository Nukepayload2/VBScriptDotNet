' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Globalization
Imports System.Threading.Thread
Imports Microsoft.CodeAnalysis.VisualBasic.ObjectDisplay
Imports Xunit

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests

    Public Class ObjectDisplayTests

        <Fact>
        Public Sub IntegralPrimitives()
            Assert.Equal("1", FormatPrimitive(CByte(1)))
            Assert.Equal("123", FormatPrimitive(CByte(123)))
            Assert.Equal("255", FormatPrimitive(Byte.MaxValue))
            Assert.Equal("1", FormatPrimitive(CSByte(1)))
            Assert.Equal("123", FormatPrimitive(CSByte(123)))
            Assert.Equal("-1", FormatPrimitive(CSByte(-1)))
            Assert.Equal("1", FormatPrimitive(CUShort(1)))
            Assert.Equal("123", FormatPrimitive(CUShort(123)))
            Assert.Equal("65535", FormatPrimitive(UShort.MaxValue))
            Assert.Equal("1", FormatPrimitive(CShort(1)))
            Assert.Equal("123", FormatPrimitive(CShort(123)))
            Assert.Equal("-1", FormatPrimitive(CShort(-1)))
            Assert.Equal("1", FormatPrimitive(CUInt(1)))
            Assert.Equal("123", FormatPrimitive(CUInt(123)))
            Assert.Equal("4294967295", FormatPrimitive(UInteger.MaxValue))
            Assert.Equal("1", FormatPrimitive(CInt(1)))
            Assert.Equal("123", FormatPrimitive(CInt(123)))
            Assert.Equal("-1", FormatPrimitive(CInt(-1)))
            Assert.Equal("1", FormatPrimitive(CULng(1)))
            Assert.Equal("123", FormatPrimitive(CULng(123)))
            Assert.Equal("18446744073709551615", FormatPrimitive(ULong.MaxValue))
            Assert.Equal("1", FormatPrimitive(CLng(1)))
            Assert.Equal("123", FormatPrimitive(CLng(123)))
            Assert.Equal("-1", FormatPrimitive(CLng(-1)))

            Assert.Equal("&H00", FormatPrimitiveUsingHexadecimalNumbers(CByte(0)))
            Assert.Equal("&H01", FormatPrimitiveUsingHexadecimalNumbers(CByte(1)))
            Assert.Equal("&H7F", FormatPrimitiveUsingHexadecimalNumbers(CByte(&H7F)))
            Assert.Equal("&HFF", FormatPrimitiveUsingHexadecimalNumbers(Byte.MaxValue))
            Assert.Equal("&H00", FormatPrimitiveUsingHexadecimalNumbers(CSByte(0)))
            Assert.Equal("&H01", FormatPrimitiveUsingHexadecimalNumbers(CSByte(1)))
            Assert.Equal("&H7F", FormatPrimitiveUsingHexadecimalNumbers(CSByte(&H7F)))
            Assert.Equal("&HFFFFFFFF", FormatPrimitiveUsingHexadecimalNumbers(CSByte(-1)))
            Assert.Equal("&HFFFFFFFE", FormatPrimitiveUsingHexadecimalNumbers(CSByte((-2))))
            Assert.Equal("&H0000", FormatPrimitiveUsingHexadecimalNumbers(CUShort(0)))
            Assert.Equal("&H0001", FormatPrimitiveUsingHexadecimalNumbers(CUShort(1)))
            Assert.Equal("&H007F", FormatPrimitiveUsingHexadecimalNumbers(CUShort(&H7F)))
            Assert.Equal("&HFFFF", FormatPrimitiveUsingHexadecimalNumbers(UShort.MaxValue))
            Assert.Equal("&H0000", FormatPrimitiveUsingHexadecimalNumbers(CShort(0)))
            Assert.Equal("&H0001", FormatPrimitiveUsingHexadecimalNumbers(CShort(1)))
            Assert.Equal("&H007F", FormatPrimitiveUsingHexadecimalNumbers(CShort(&H7F)))
            Assert.Equal("&HFFFFFFFF", FormatPrimitiveUsingHexadecimalNumbers(CShort(-1)))
            Assert.Equal("&HFFFFFFFE", FormatPrimitiveUsingHexadecimalNumbers(CShort((-2))))
            Assert.Equal("&H00000000", FormatPrimitiveUsingHexadecimalNumbers(CUInt(0)))
            Assert.Equal("&H00000001", FormatPrimitiveUsingHexadecimalNumbers(CUInt(1)))
            Assert.Equal("&H0000007F", FormatPrimitiveUsingHexadecimalNumbers(CUInt(&H7F)))
            Assert.Equal("&HFFFFFFFF", FormatPrimitiveUsingHexadecimalNumbers(UInteger.MaxValue))
            Assert.Equal("&H00000000", FormatPrimitiveUsingHexadecimalNumbers(CInt(0)))
            Assert.Equal("&H00000001", FormatPrimitiveUsingHexadecimalNumbers(CInt(1)))
            Assert.Equal("&H0000007F", FormatPrimitiveUsingHexadecimalNumbers(CInt(&H7F)))
            Assert.Equal("&HFFFFFFFF", FormatPrimitiveUsingHexadecimalNumbers(CInt(-1)))
            Assert.Equal("&HFFFFFFFE", FormatPrimitiveUsingHexadecimalNumbers(CInt((-2))))
            Assert.Equal("&H0000000000000000", FormatPrimitiveUsingHexadecimalNumbers(CULng(0)))
            Assert.Equal("&H0000000000000001", FormatPrimitiveUsingHexadecimalNumbers(CULng(1)))
            Assert.Equal("&H000000000000007F", FormatPrimitiveUsingHexadecimalNumbers(CULng(&H7F)))
            Assert.Equal("&HFFFFFFFFFFFFFFFF", FormatPrimitiveUsingHexadecimalNumbers(ULong.MaxValue))
            Assert.Equal("&H0000000000000000", FormatPrimitiveUsingHexadecimalNumbers(CLng(0)))
            Assert.Equal("&H0000000000000001", FormatPrimitiveUsingHexadecimalNumbers(CLng(1)))
            Assert.Equal("&H000000000000007F", FormatPrimitiveUsingHexadecimalNumbers(CLng(&H7F)))
            Assert.Equal("&HFFFFFFFFFFFFFFFF", FormatPrimitiveUsingHexadecimalNumbers(CLng(-1)))
            Assert.Equal("&HFFFFFFFFFFFFFFFE", FormatPrimitiveUsingHexadecimalNumbers(CLng((-2))))
        End Sub

        <Fact>
        Public Sub Booleans()
            Assert.Equal("True", FormatPrimitive(True))
            Assert.Equal("False", FormatPrimitive(False))
        End Sub

        <Fact>
        Public Sub NothingLiterals()
            Assert.Equal("Nothing", FormatPrimitive(Nothing))
        End Sub

        <Fact>
        Public Sub FloatingPointAndDecimalPrimitives()
            Assert.Equal("2", FormatPrimitive(CType(2, Decimal)))
            Assert.Equal("2", FormatPrimitive(CType(2, Single)))
            Assert.Equal("2", FormatPrimitive(CType(2, Double)))
        End Sub

        <Fact>
        Public Sub Characters()
            Assert.Equal("""x""c", FormatPrimitive("x"c, quoteStrings:=True))
            Assert.Equal("x", FormatPrimitive("x"c, quoteStrings:=False))
            Assert.Equal("""x""c", FormatPrimitiveUsingHexadecimalNumbers("x"c, quoteStrings:=True))
            Assert.Equal("x", FormatPrimitiveUsingHexadecimalNumbers("x"c, quoteStrings:=False))

            Assert.Equal("vbNullChar", FormatPrimitiveUsingHexadecimalNumbers(ChrW(0), quoteStrings:=True))
            Assert.Equal("ChrW(&H1E)", FormatPrimitiveUsingHexadecimalNumbers(ChrW(&H1E), quoteStrings:=True))
            Assert.Equal(New String({ChrW(&H1E)}), FormatPrimitiveUsingHexadecimalNumbers(ChrW(&H1E), quoteStrings:=False))
            Assert.Equal(New String({ChrW(20)}), FormatPrimitive(ChrW(20)))
            Assert.Equal("vbBack", FormatPrimitiveUsingHexadecimalNumbers(ChrW(&H8), quoteStrings:=True))
            Assert.Equal(vbBack, FormatPrimitive(ChrW(&H8)))
            Assert.Equal("vbLf", FormatPrimitiveUsingHexadecimalNumbers(ChrW(&HA), quoteStrings:=True))
            Assert.Equal("vbVerticalTab", FormatPrimitiveUsingHexadecimalNumbers(vbVerticalTab(0), quoteStrings:=True))
            Assert.Equal("vbTab", FormatPrimitiveUsingHexadecimalNumbers(ChrW(&H9), quoteStrings:=True))
            Assert.Equal("vbFormFeed", FormatPrimitiveUsingHexadecimalNumbers(ChrW(&HC), quoteStrings:=True))
            Assert.Equal("vbCr", FormatPrimitiveUsingHexadecimalNumbers(ChrW(&HD), quoteStrings:=True))
        End Sub

        <Fact>
        Public Sub Characters_QuotesAndEscaping()
            Assert.Equal(QuoteAndEscapingCombinations("a"c), {"a", """a""c", """a""c"})
            Assert.Equal(QuoteAndEscapingCombinations(vbTab(0)), {vbTab, """" & vbTab & """c", "vbTab"})
            Assert.Equal(QuoteAndEscapingCombinations(ChrW(&H26F4)), {ChrW(&H26F4).ToString(), """" & ChrW(&H26F4) & """c", """" & ChrW(&H26F4) & """c"})
            Assert.Equal(QuoteAndEscapingCombinations(ChrW(&H7F)), {ChrW(&H7F).ToString(), """" & ChrW(&H7F) & """c", "ChrW(127)"})
            Assert.Equal(QuoteAndEscapingCombinations(""""c), {"""", New String(""""c, 4) & "c", New String(""""c, 4) & "c"})
        End Sub

        <Fact>
        Public Sub Characters_VisualBasicDoubleQuoteCharacters()
            Dim quote = """"c

            Assert.Equal(New String(quote, 4) & "c", ObjectDisplay.FormatLiteral(quote, ObjectDisplayOptions.UseQuotes))
            Assert.Equal("ChrW(65282)", ObjectDisplay.FormatLiteral(ChrW(&HFF02), ObjectDisplayOptions.UseQuotes))
            Assert.Equal("ChrW(8220)", ObjectDisplay.FormatLiteral(ChrW(&H201C), ObjectDisplayOptions.UseQuotes))
            Assert.Equal("ChrW(8221)", ObjectDisplay.FormatLiteral(ChrW(&H201D), ObjectDisplayOptions.UseQuotes))
            Assert.Equal("ChrW(&HFF02)", ObjectDisplay.FormatLiteral(ChrW(&HFF02), ObjectDisplayOptions.UseQuotes Or ObjectDisplayOptions.UseHexadecimalNumbers))
            Assert.Equal("ChrW(&H201C)", ObjectDisplay.FormatLiteral(ChrW(&H201C), ObjectDisplayOptions.UseQuotes Or ObjectDisplayOptions.UseHexadecimalNumbers))
            Assert.Equal("ChrW(&H201D)", ObjectDisplay.FormatLiteral(ChrW(&H201D), ObjectDisplayOptions.UseQuotes Or ObjectDisplayOptions.UseHexadecimalNumbers))
        End Sub

        Private Shared Function QuoteAndEscapingCombinations(ch As Char) As IEnumerable(Of String)
            Return {
                ObjectDisplay.FormatLiteral(ch, ObjectDisplayOptions.None),
                ObjectDisplay.FormatLiteral(ch, ObjectDisplayOptions.UseQuotes),
                ObjectDisplay.FormatLiteral(ch, ObjectDisplayOptions.UseQuotes Or ObjectDisplayOptions.EscapeNonPrintableCharacters)
            }
        End Function

        <Fact>
        Public Sub Strings()
            Assert.Equal("", FormatPrimitive("", quoteStrings:=False))
            Assert.Equal("a", FormatPrimitive("a", quoteStrings:=False))
            Assert.Equal("""", FormatPrimitive("""", quoteStrings:=False))
            Assert.Equal("""""", FormatPrimitive("", quoteStrings:=True))
            Assert.Equal("""""""""", FormatPrimitive("""", quoteStrings:=True))

            Assert.Equal("ChrW(&HFFFE)", FormatPrimitiveUsingHexadecimalNumbers(ChrW(&HFFFE), quoteStrings:=True))
            Assert.Equal("ChrW(65534)", FormatPrimitive(ChrW(&HFFFE), quoteStrings:=True))
            Assert.Equal(New String({ChrW(&HFFFE)}), FormatPrimitiveUsingHexadecimalNumbers(ChrW(&HFFFE), quoteStrings:=False))
            Assert.Equal(New String({ChrW(65534)}), FormatPrimitive(ChrW(&HFFFE), quoteStrings:=False))

            Dim s = "a" & ChrW(&HFFFF) & ChrW(&HFFFE) & vbCrLf & "b"

            Assert.Equal("""a"" & ChrW(&HFFFE)", FormatPrimitiveUsingHexadecimalNumbers("a" & ChrW(&HFFFE), quoteStrings:=True))
            Assert.Equal("ChrW(&HFFFE) & ""a""", FormatPrimitiveUsingHexadecimalNumbers(ChrW(&HFFFE) & "a", quoteStrings:=True))
            Assert.Equal("""a"" & ChrW(&HFFFE) & ""a""", FormatPrimitiveUsingHexadecimalNumbers("a" & ChrW(&HFFFE) & "a", quoteStrings:=True))
            Assert.Equal("ChrW(&HFFFF) & ChrW(&HFFFE)", FormatPrimitiveUsingHexadecimalNumbers(ChrW(&HFFFF) & ChrW(&HFFFE), quoteStrings:=True))
            Assert.Equal("ChrW(&HFFFF) & ""a"" & ChrW(&HFFFE)", FormatPrimitiveUsingHexadecimalNumbers(ChrW(&HFFFF) & "a" & ChrW(&HFFFE), quoteStrings:=True))
            Assert.Equal("""a"" & ChrW(&HFFFF) & ChrW(&HFFFE) & vbCrLf & ""b""", FormatPrimitiveUsingHexadecimalNumbers(s, quoteStrings:=True))

            Assert.Equal(s, FormatPrimitiveUsingHexadecimalNumbers(s, quoteStrings:=False))
            Assert.Equal(s, ObjectDisplay.FormatLiteral(s, ObjectDisplayOptions.None))
            Assert.Equal("""a"" & ChrW(&HFFFF) & ChrW(&HFFFE) & vbCrLf & ""b""", ObjectDisplay.FormatLiteral(s, ObjectDisplayOptions.UseQuotes Or ObjectDisplayOptions.EscapeNonPrintableCharacters Or ObjectDisplayOptions.UseHexadecimalNumbers))

            Assert.Equal("""a"" & vbBack", FormatPrimitiveUsingHexadecimalNumbers("a" & vbBack, quoteStrings:=True))
            Assert.Equal("""a"" & vbCr", FormatPrimitiveUsingHexadecimalNumbers("a" & vbCr, quoteStrings:=True))
            Assert.Equal("""a"" & vbCrLf", FormatPrimitiveUsingHexadecimalNumbers("a" & vbCrLf, quoteStrings:=True))
            Assert.Equal("""a"" & vbFormFeed", FormatPrimitiveUsingHexadecimalNumbers("a" & vbFormFeed, quoteStrings:=True))
            Assert.Equal("""a"" & vbLf", FormatPrimitiveUsingHexadecimalNumbers("a" & vbLf, quoteStrings:=True))
            Assert.Equal("""a"" & vbNullChar", FormatPrimitiveUsingHexadecimalNumbers("a" & vbNullChar, quoteStrings:=True))
            Assert.Equal("""a"" & vbTab", FormatPrimitiveUsingHexadecimalNumbers("a" & vbTab, quoteStrings:=True))
            Assert.Equal("""a"" & vbVerticalTab", FormatPrimitiveUsingHexadecimalNumbers("a" & vbVerticalTab, quoteStrings:=True))
        End Sub

        <Fact>
        Public Sub Strings_VisualBasicDoubleQuoteCharacters()
            Dim quote = """"c
            Dim s = "a" & quote & ChrW(&HFF02) & ChrW(&H201C) & ChrW(&H201D) & "b"
            Dim expected = String.Concat(
                quote,
                "a",
                New String(quote, 2),
                quote,
                " & ChrW(&HFF02) & ChrW(&H201C) & ChrW(&H201D) & ",
                quote,
                "b",
                quote)

            Assert.Equal(expected, ObjectDisplay.FormatLiteral(s, ObjectDisplayOptions.UseQuotes Or ObjectDisplayOptions.EscapeNonPrintableCharacters Or ObjectDisplayOptions.UseHexadecimalNumbers))
            Assert.Equal(s, ObjectDisplay.FormatLiteral(s, ObjectDisplayOptions.None))
        End Sub

        <Fact>
        Public Sub TextForEscapedStringLiterals_01()
            Dim literal = SyntaxFactory.Literal(ChrW(&H2028) & "x")
            Assert.Equal("ChrW(8232) & ""x""", literal.Text)
            literal = SyntaxFactory.Literal(ChrW(&HDBFF))
            Assert.Equal("ChrW(56319)", literal.Text)
        End Sub

        <Fact>
        Public Sub TextForEscapedStringLiterals_02()
            Dim footBall = ChrW(&HD83C) & ChrW(&HDFC8)
            Assert.Equal(footBall, ObjectDisplay.FormatPrimitive(footBall, ObjectDisplayOptions.None))
            Assert.Equal("""" & footBall & """", ObjectDisplay.FormatPrimitive(footBall, ObjectDisplayOptions.UseQuotes Or ObjectDisplayOptions.EscapeNonPrintableCharacters Or ObjectDisplayOptions.UseHexadecimalNumbers))
            Assert.Equal("""" & footBall & """", ObjectDisplay.FormatPrimitive(footBall, ObjectDisplayOptions.UseQuotes Or ObjectDisplayOptions.EscapeNonPrintableCharacters))

            Dim trash = ChrW(&HDFC8) & ChrW(&HD83C)
            Assert.Equal(trash, ObjectDisplay.FormatPrimitive(trash, ObjectDisplayOptions.None))
            Assert.Equal("ChrW(&HDFC8) & ChrW(&HD83C)", ObjectDisplay.FormatPrimitive(trash, ObjectDisplayOptions.UseQuotes Or ObjectDisplayOptions.EscapeNonPrintableCharacters Or ObjectDisplayOptions.UseHexadecimalNumbers))
            Assert.Equal("ChrW(57288) & ChrW(55356)", ObjectDisplay.FormatPrimitive(trash, ObjectDisplayOptions.UseQuotes Or ObjectDisplayOptions.EscapeNonPrintableCharacters))
        End Sub

        <Fact>
        Public Sub Strings_QuotesAndEscaping()
            Assert.Equal(QuoteAndEscapingCombinations("a"), {"a", """a""", """a"""})
            Assert.Equal(QuoteAndEscapingCombinations(vbTab), {vbTab, """" & vbTab & """", "vbTab"})
            Assert.Equal(QuoteAndEscapingCombinations(ChrW(&H26F4).ToString()), {ChrW(&H26F4).ToString(), """" & ChrW(&H26F4) & """", """" & ChrW(&H26F4) & """"})
            Assert.Equal(QuoteAndEscapingCombinations(ChrW(&H7F).ToString()), {ChrW(&H7F).ToString(), """" & ChrW(&H7F) & """", "ChrW(127)"})
            Assert.Equal(QuoteAndEscapingCombinations(""""), {"""", New String(""""c, 4), New String(""""c, 4)})
        End Sub

        Private Shared Function QuoteAndEscapingCombinations(s As String) As IEnumerable(Of String)
            Return {
                ObjectDisplay.FormatLiteral(s, ObjectDisplayOptions.None),
                ObjectDisplay.FormatLiteral(s, ObjectDisplayOptions.UseQuotes),
                ObjectDisplay.FormatLiteral(s, ObjectDisplayOptions.UseQuotes Or ObjectDisplayOptions.EscapeNonPrintableCharacters)
            }
        End Function

        <Fact>
        Public Sub CultureInvariance()
            Dim originalCulture = CurrentThread.CurrentCulture
            Try
                CurrentThread.CurrentCulture = New CultureInfo(1031)

                Dim dateValue As New Date(2001, 1, 31)
                Assert.Equal("31.01.2001 00:00:00", dateValue.ToString("dd.MM.yyyy HH:mm:ss"))
                Assert.Equal("#1/31/2001 12:00:00 AM#", FormatPrimitive(dateValue))

                Dim decimalValue As New Decimal(12.5)
                Assert.Equal("12,5", decimalValue.ToString())
                Assert.Equal("12.5", FormatPrimitive(decimalValue))
                Assert.Equal("12.5", ObjectDisplay.FormatLiteral(decimalValue, ObjectDisplayOptions.None, CultureInfo.InvariantCulture))
                Assert.Equal("12,5", ObjectDisplay.FormatLiteral(decimalValue, ObjectDisplayOptions.None, CurrentThread.CurrentCulture))

                Dim doubleValue As Double = 12.5
                Assert.Equal("12,5", doubleValue.ToString())
                Assert.Equal("12.5", FormatPrimitive(doubleValue))
                Assert.Equal("12.5", ObjectDisplay.FormatLiteral(doubleValue, ObjectDisplayOptions.None, CultureInfo.InvariantCulture))
                Assert.Equal("12,5", ObjectDisplay.FormatLiteral(doubleValue, ObjectDisplayOptions.None, CurrentThread.CurrentCulture))

                Dim singleValue As Single = 12.5
                Assert.Equal("12,5", singleValue.ToString())
                Assert.Equal("12.5", FormatPrimitive(singleValue))
                Assert.Equal("12.5", ObjectDisplay.FormatLiteral(singleValue, ObjectDisplayOptions.None, CultureInfo.InvariantCulture))
                Assert.Equal("12,5", ObjectDisplay.FormatLiteral(singleValue, ObjectDisplayOptions.None, CurrentThread.CurrentCulture))

            Finally
                CurrentThread.CurrentCulture = originalCulture
            End Try
        End Sub

        <Fact>
        Public Sub TypeSuffixes()
            Assert.Equal("True", FormatPrimitiveIncludingTypeSuffix(True))
            Assert.Equal("42", FormatPrimitiveIncludingTypeSuffix(CSByte(&H2A)))
            Assert.Equal("42", FormatPrimitiveIncludingTypeSuffix(CByte(&H2A)))
            Assert.Equal("42S", FormatPrimitiveIncludingTypeSuffix(CShort(&H2A)))
            Assert.Equal("42US", FormatPrimitiveIncludingTypeSuffix(CUShort(&H2A)))
            Assert.Equal("42I", FormatPrimitiveIncludingTypeSuffix(CInt(&H2A)))
            Assert.Equal("42UI", FormatPrimitiveIncludingTypeSuffix(CUInt(&H2A)))
            Assert.Equal("42L", FormatPrimitiveIncludingTypeSuffix(CLng(&H2A)))
            Assert.Equal("42UL", FormatPrimitiveIncludingTypeSuffix(CULng(&H2A)))
            Assert.Equal("3.14159F", FormatPrimitiveIncludingTypeSuffix(CSng(3.14159)))
            Assert.Equal("26.2R", FormatPrimitiveIncludingTypeSuffix(26.2))
            Assert.Equal("12.5D", FormatPrimitiveIncludingTypeSuffix(12.5D, useHexadecimalNumbers:=True))
        End Sub

        <Fact>
        Public Sub StringEscaping()
            Const value = "a" & vbTab & "b"

            Assert.Equal("a" & vbTab & "b", ObjectDisplay.FormatPrimitive(value, ObjectDisplayOptions.None))
            Assert.Equal("""a" & vbTab & "b""", ObjectDisplay.FormatPrimitive(value, ObjectDisplayOptions.UseQuotes))
            Assert.Equal("""a"" & vbTab & ""b""", ObjectDisplay.FormatPrimitive(value, ObjectDisplayOptions.UseQuotes Or ObjectDisplayOptions.EscapeNonPrintableCharacters))
        End Sub

        Private Shared Function FormatPrimitive(obj As Object, Optional quoteStrings As Boolean = False) As String
            Return ObjectDisplay.FormatPrimitive(obj, If(quoteStrings, ObjectDisplayOptions.UseQuotes Or ObjectDisplayOptions.EscapeNonPrintableCharacters, ObjectDisplayOptions.None))
        End Function

        Private Shared Function FormatPrimitiveUsingHexadecimalNumbers(obj As Object, Optional quoteStrings As Boolean = False) As String
            Dim options = If(quoteStrings, ObjectDisplayOptions.UseQuotes Or ObjectDisplayOptions.EscapeNonPrintableCharacters, ObjectDisplayOptions.None)
            Return ObjectDisplay.FormatPrimitive(obj, options Or ObjectDisplayOptions.UseHexadecimalNumbers)
        End Function

        Private Shared Function FormatPrimitiveIncludingTypeSuffix(obj As Object, Optional useHexadecimalNumbers As Boolean = False) As String
            Dim options = If(useHexadecimalNumbers, ObjectDisplayOptions.UseHexadecimalNumbers, ObjectDisplayOptions.None)
            Return ObjectDisplay.FormatPrimitive(obj, options Or ObjectDisplayOptions.IncludeTypeSuffix)
        End Function

    End Class

End Namespace
