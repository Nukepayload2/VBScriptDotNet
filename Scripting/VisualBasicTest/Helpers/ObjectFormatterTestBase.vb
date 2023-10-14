Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Xunit

Public MustInherit Class ObjectFormatterTestBase
    Protected Shared ReadOnly Property SingleLineOptions As PrintOptions
        Get
            Return New PrintOptions With {
                .MemberDisplayFormat = MemberDisplayFormat.SingleLine
            }
        End Get
    End Property

    Protected Shared ReadOnly Property SeparateLinesOptions As PrintOptions
        Get
            Return New PrintOptions With {
                .MemberDisplayFormat = MemberDisplayFormat.SeparateLines,
                .MaximumOutputLength = Integer.MaxValue
            }
        End Get
    End Property

    Protected Shared ReadOnly Property HiddenOptions As PrintOptions
        Get
            Return New PrintOptions With {
                .MemberDisplayFormat = MemberDisplayFormat.Hidden
            }
        End Get
    End Property

    Public Sub AssertMembers(str As String, ParamArray expected As String())
        Dim i As Integer = 0
        For Each line In str.Split(New String() {Environment.NewLine & "  "}, StringSplitOptions.None)
            If i = 0 Then
                Assert.Equal(expected(i) & " {", line)
            ElseIf i = expected.Length - 1 Then
                Assert.Equal(expected(i) & Environment.NewLine & "}" & Environment.NewLine, line)
            Else
                Assert.Equal(expected(i) & ",", line)
            End If

            i += 1
        Next

        Assert.Equal(expected.Length, i)
    End Sub
End Class