Imports System.Runtime.CompilerServices
Imports System.Text
Imports Xunit

Public Class AssertEx
    Public Shared Sub AssertEqualToleratingWhitespaceDifferences(
    expected As String,
    actual As String,
    Optional message As String = Nothing,
    Optional escapeQuotes As Boolean = False,
    <CallerFilePath> Optional expectedValueSourcePath As String = Nothing,
    <CallerLineNumber> Optional expectedValueSourceLine As Integer = 0)

        Dim normalizedExpected = NormalizeWhitespace(expected)
        Dim normalizedActual = NormalizeWhitespace(actual)

        If normalizedExpected <> normalizedActual Then
            Assert.True(False, GetAssertMessage(expected, actual, message, escapeQuotes, expectedValueSourcePath, expectedValueSourceLine))
        End If
    End Sub
    Friend Shared Function NormalizeWhitespace(input As String) As String
        Dim output = New StringBuilder
        Dim inputLines = input.Split(ControlChars.Lf, ControlChars.Cr)
        For Each line In inputLines
            Dim trimmedLine = line.Trim()
            If trimmedLine.Length > 0 Then
                If Not (trimmedLine.Chars(0) = "{"c OrElse trimmedLine.Chars(0) = "}"c) Then
                    output.Append("  ")
                End If

                output.AppendLine(trimmedLine)
            End If
        Next line

        Return output.ToString()
    End Function

	Public Shared Function GetAssertMessage(expected As String, actual As String, Optional prefix As String = Nothing, Optional escapeQuotes As Boolean = False, Optional expectedValueSourcePath As String = Nothing, Optional expectedValueSourceLine As Integer = 0) As String
		Return GetAssertMessage(DiffUtil.Lines(expected), DiffUtil.Lines(actual), prefix, escapeQuotes, expectedValueSourcePath, expectedValueSourceLine)
	End Function

	Public Shared Function GetAssertMessage(Of T)(expected As IEnumerable(Of T), actual As IEnumerable(Of T), Optional prefix As String = Nothing, Optional escapeQuotes As Boolean = False, Optional expectedValueSourcePath As String = Nothing, Optional expectedValueSourceLine As Integer = 0) As String
		Dim itemInspector As Func(Of T, String) = If(escapeQuotes, New Func(Of T, String)(Function(t2) t2.ToString().Replace("""", """""")), Nothing)
		Return GetAssertMessage(expected, actual, prefix:=prefix, itemInspector:=itemInspector, itemSeparator:=vbCrLf, expectedValueSourcePath:=expectedValueSourcePath, expectedValueSourceLine:=expectedValueSourceLine)
	End Function

	Private Shared ReadOnly s_diffToolPath As String = Environment.GetEnvironmentVariable("ROSLYN_DIFFTOOL")

	Public Shared Function GetAssertMessage(Of T)(expected As IEnumerable(Of T), actual As IEnumerable(Of T), Optional comparer As IEqualityComparer(Of T) = Nothing, Optional prefix As String = Nothing, Optional itemInspector As Func(Of T, String) = Nothing, Optional itemSeparator As String = Nothing, Optional expectedValueSourcePath As String = Nothing, Optional expectedValueSourceLine As Integer = 0) As String
		If itemInspector Is Nothing Then
			If GetType(T) Is GetType(Byte) Then
				itemInspector = Function(b) $"0x{b:X2}"
			Else
				itemInspector = New Func(Of T, String)(Function(obj) If(obj IsNot Nothing, obj.ToString(), "<null>"))
			End If
		End If

		If itemSeparator Is Nothing Then
			If GetType(T) Is GetType(Byte) Then
				itemSeparator = ", "
			Else
				itemSeparator = "," & Environment.NewLine
			End If
		End If

		Dim expectedString = String.Join(itemSeparator, expected.Take(10).Select(itemInspector))
		Dim actualString = String.Join(itemSeparator, actual.Select(itemInspector))

		Dim message = New StringBuilder

		If Not String.IsNullOrEmpty(prefix) Then
			message.AppendLine(prefix)
			message.AppendLine()
		End If

		message.AppendLine("Expected:")
		message.AppendLine(expectedString)
		If expected.Count() > 10 Then
			message.AppendLine("... truncated ...")
		End If

		message.AppendLine("Actual:")
		message.AppendLine(actualString)
		message.AppendLine("Differences:")
		message.AppendLine(DiffUtil.DiffReport(expected, actual, itemSeparator, comparer, itemInspector))

		Return message.ToString()
	End Function

End Class
