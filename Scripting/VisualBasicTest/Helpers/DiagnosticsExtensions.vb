Imports System.Collections.Immutable
Imports System.Runtime.CompilerServices
Imports System.Text
Imports System.Text.RegularExpressions
Imports Microsoft.CodeAnalysis.PooledObjects
Imports Microsoft.CodeAnalysis.Text
Imports Xunit

Friend Module DiagnosticsExtensions
    <Extension>
    Friend Function VerifyDiagnostics(c As Compilation) As VisualBasicCompilation
        Dim vbc = TryCast(c, VisualBasicCompilation)
        If vbc Is Nothing Then
            Throw New NotSupportedException
        End If
        Return vbc.VerifyDiagnostics
    End Function

    <Extension>
    Friend Function VerifyDiagnostics(c As VisualBasicCompilation) As VisualBasicCompilation
        Dim diagnostics = c.GetDiagnostics(CompilationStage.Compile)
        diagnostics.Verify()
        Return c
    End Function

    <Extension>
    Public Sub Verify(actual As ImmutableArray(Of Diagnostic))
        Verify(DirectCast(actual, IEnumerable(Of Diagnostic)))
    End Sub

    <Extension>
    Public Sub Verify(actual As IEnumerable(Of Diagnostic))
        Dim expected = Array.Empty(Of DiagnosticDescription)

        Dim includeDefaultSeverity = False
        Dim includeEffectiveSeverity = False
        Dim unmatchedActualDescription = actual.Select(Function(d2) New DiagnosticDescription(d2)).ToList()
        Dim unmatchedActualIndex = actual.Select(Function(underscore, i) i).ToList()
        Dim unmatchedExpected = ArrayBuilder(Of DiagnosticDescription).GetInstance()

        If unmatchedActualDescription.Count > 0 OrElse unmatchedExpected.Count > 0 Then
            Dim text = DiagnosticDescription.GetAssertText(expected, actual, unmatchedExpected.ToArray(), actual.Select(Function(a, i) (a, i)).Join(unmatchedActualIndex, Function(ai) ai.i, Function(i) i, Function(ai, underscore) ai.a))
            Assert.True(False, text)
        End If
        unmatchedExpected.Free()
    End Sub

    Private Class LinePositionComparer
        Implements IComparer(Of LinePosition?)

        Friend Shared Instance As New LinePositionComparer

        Public Function Compare(x As LinePosition?, y As LinePosition?) As Integer Implements IComparer(Of LinePosition?).Compare
            If x Is Nothing Then
                If y Is Nothing Then
                    Return 0
                End If
                Return -1
            End If

            If y Is Nothing Then
                Return 1
            End If

            Dim lineDiff As Integer = x.Value.Line.CompareTo(y.Value.Line)
            If lineDiff <> 0 Then
                Return lineDiff
            End If

            Return x.Value.Character.CompareTo(y.Value.Character)
        End Function
    End Class

    Private Class DiagnosticDescription
        Private ReadOnly _diag As Diagnostic

        Public Sub New(diag As Diagnostic)
            _diag = diag
        End Sub

        Public Overrides Function ToString() As String
            Return _diag?.ToString()
        End Function

        Private Shared Function Sort(diagnostics As IEnumerable(Of Diagnostic)) As IEnumerable(Of Diagnostic)
            Return diagnostics.OrderBy(Function(d) d.Location.GetMappedLineSpan().StartLinePosition, LinePositionComparer.Instance)
        End Function

        Private Shared Sub GetCommaSeparatedLines(sb As StringBuilder, lines As ArrayBuilder(Of String))
            Dim n As Integer = lines.Count
            For i As Integer = 0 To n - 1
                sb.Append(lines(i))
                If i < n - 1 Then
                    sb.Append(","c)
                End If
                sb.AppendLine()
            Next i
        End Sub

        Public Shared Function GetAssertText(expected() As DiagnosticDescription, actual As IEnumerable(Of Diagnostic), unamtchedExpected() As DiagnosticDescription, unmatchedActual As IEnumerable(Of Diagnostic)) As String
            Const CSharp As Integer = 1
            Const VisualBasic As Integer = 2
            Dim language = VisualBasic
            Dim includeDiagnosticMessagesAsComments = (language = CSharp)
            Dim indentDepth As Integer = If(language = CSharp, 4, 1)
            Dim includeDefaultSeverity = False
            Dim includeEffectiveSeverity = False

            ' If this is a new test (empty expectations) or a test that's already sorted,
            ' we sort the actual diagnostics to minimize diff noise as diagnostics change.
            actual = Sort(actual)
            unmatchedActual = Sort(unmatchedActual)

            Dim assertText = New StringBuilder
            assertText.AppendLine()

            ' write out the error baseline as method calls
            Dim i As Integer
            assertText.AppendLine("Expected:")
            Dim expectedText = ArrayBuilder(Of String).GetInstance()
            For Each d In expected
                expectedText.Add(GetDiagnosticDescription(d, indentDepth))
            Next d
            GetCommaSeparatedLines(assertText, expectedText)

            ' write out the actual results as method calls (copy/paste this to update baseline)
            assertText.AppendLine("Actual:")
            Dim e = actual.GetEnumerator()
            i = 0
            Do While e.MoveNext()
                Dim d As Diagnostic = e.Current
                Dim message As String = d.ToString()
                If Regex.Match(message, "{\d+}").Success Then
                    Assert.True(False, "Diagnostic messages should never contain unsubstituted placeholders." & vbLf & "    " & message)
                End If

                If i > 0 Then
                    assertText.AppendLine(",")
                End If

                If includeDiagnosticMessagesAsComments Then
                    Indent(assertText, indentDepth)
                    assertText.Append("// ")
                    assertText.AppendLine(d.ToString())
                    Dim l = d.Location
                    If l.IsInSource Then
                        Indent(assertText, indentDepth)
                        assertText.Append("// ")
                        assertText.AppendLine(l.SourceTree.GetText().Lines.GetLineFromPosition(l.SourceSpan.Start).ToString())
                    End If
                End If

                Dim description = New DiagnosticDescription(d)
                assertText.Append(GetDiagnosticDescription(description, indentDepth))
                i += 1
            Loop
            If i > 0 Then
                assertText.AppendLine()
            End If

            assertText.AppendLine("Diff:")

            Dim unmatchedExpectedText = ArrayBuilder(Of String).GetInstance()
            For Each d In unamtchedExpected
                unmatchedExpectedText.Add(GetDiagnosticDescription(d, indentDepth))
            Next d

            Dim unmatchedActualText = ArrayBuilder(Of String).GetInstance()
            e = unmatchedActual.GetEnumerator()
            i = 0
            Do While e.MoveNext()
                Dim d As Diagnostic = e.Current
                Dim diffDescription = New DiagnosticDescription(d)
                unmatchedActualText.Add(GetDiagnosticDescription(diffDescription, indentDepth))
                i += 1
            Loop

            assertText.Append(DiffUtil.DiffReport(unmatchedExpectedText, unmatchedActualText, separator:=Environment.NewLine))

            unmatchedExpectedText.Free()
            unmatchedActualText.Free()

            expectedText.Free()

            Return assertText.ToString()
        End Function
        Private Shared Sub Indent(sb As StringBuilder, count As Integer)
            sb.Append(" "c, 4 * count)
        End Sub
        Private Shared Function GetDiagnosticDescription(d As DiagnosticDescription, indentDepth As Integer) As String
            Return New String(" "c, 4 * indentDepth) + d.ToString()
        End Function

    End Class
End Module
