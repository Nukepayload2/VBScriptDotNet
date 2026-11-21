' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Generic
Imports System.Text

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' A script compile failure surfaced through
    ''' <see cref="Nukepayload2.VBScriptDotNet.Scripting.VBScript.EvaluateAsync(Of T)"/> or
    ''' <see cref="IVBScriptRunner.EvaluateAsync(Of T)"/>, both of which have no result object to return. The
    ''' one-shot entries that do return data (<c>RunAsync</c>/<c>CompileAsync</c>) never throw this; they put the
    ''' same diagnostics into <see cref="VBScriptResult"/>.
    ''' <para>
    ''' This is a script failure, not host misuse, so it derives from <see cref="VBScriptException"/> and
    ''' <b>not</b> from <see cref="VBScriptHostException"/>: a host catching host exceptions must not swallow a
    ''' script compile failure.
    ''' </para>
    ''' </summary>
    Public Class VBScriptScriptException
        Inherits VBScriptException

        Private ReadOnly _diagnostics As IReadOnlyList(Of VBScriptDiagnostic)

        Public Sub New(diagnostics As IReadOnlyList(Of VBScriptDiagnostic))
            MyBase.New(BuildMessage(diagnostics))

            If diagnostics Is Nothing Then
                _diagnostics = Array.Empty(Of VBScriptDiagnostic)()
            Else
                _diagnostics = diagnostics
            End If
        End Sub

        ''' <summary>The compile diagnostics; an empty list (never <see langword="Nothing"/>) when there were none.</summary>
        Public ReadOnly Property Diagnostics As IReadOnlyList(Of VBScriptDiagnostic)
            Get
                Return _diagnostics
            End Get
        End Property

        Private Shared Function BuildMessage(diagnostics As IReadOnlyList(Of VBScriptDiagnostic)) As String
            Dim count = If(diagnostics Is Nothing, 0, diagnostics.Count)
            Dim builder As New StringBuilder("The script failed to compile.")

            If count > 0 Then
                Dim first = diagnostics(0)
                builder.Append(" ").Append(first.Id).Append(": ").Append(first.Message)
            End If

            builder.Append(" (").Append(count).Append(" diagnostic(s) reported.)")
            Return builder.ToString()
        End Function

    End Class

End Namespace
