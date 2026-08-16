' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Namespace Microsoft.CodeAnalysis.VisualBasic.Syntax

    Partial Public NotInheritable Class ShebangDirectiveTriviaSyntax

        Public ReadOnly Property Content As SyntaxToken
            Get
                ' The path is carried by the SkippedTokensTrivia attached to the ExclamationToken's
                ' trailing trivia. The EndOfLineTrivia after it must not be part of the content.
                Dim text As String = String.Empty
                For Each trivia In Me.ExclamationToken.TrailingTrivia
                    If trivia.Kind = SyntaxKind.SkippedTokensTrivia Then
                        text = trivia.ToString()
                        Exit For
                    End If
                Next
                Return SyntaxFactory.StringLiteralToken(text, text)
            End Get
        End Property

        Public Function WithContent(content As SyntaxToken) As ShebangDirectiveTriviaSyntax
            If content <> Me.Content Then
                Select Case content.Kind
                    Case SyntaxKind.StringLiteralToken
                        ' Replace only the SkippedTokensTrivia that carries the path, keeping the rest of the
                        ' trailing trivia (notably the EndOfLineTrivia) so the line stays terminated.
                        Dim skippedTrivia = SyntaxFactory.Trivia(SyntaxFactory.SkippedTokensTrivia(New SyntaxTokenList(content)))
                        Dim trailingTrivia = Me.ExclamationToken.TrailingTrivia
                        Dim replaced = False
                        For Each trivia In trailingTrivia
                            If trivia.Kind = SyntaxKind.SkippedTokensTrivia Then
                                trailingTrivia = trailingTrivia.Replace(trivia, skippedTrivia)
                                replaced = True
                                Exit For
                            End If
                        Next
                        If Not replaced Then
                            trailingTrivia = trailingTrivia.Insert(0, skippedTrivia)
                        End If
                        Return Me.WithExclamationToken(Me.ExclamationToken.WithTrailingTrivia(trailingTrivia))

                    Case SyntaxKind.None
                        ' Mirrors C#: None keeps the original content untouched.
                        Return Me

                    Case Else
                        Throw New ArgumentException(NameOf(content))
                End Select
            End If
            Return Me
        End Function

    End Class

End Namespace
