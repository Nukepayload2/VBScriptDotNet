' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' Result of one <see cref="IVBScriptSession.SubmitAsync"/> call. A compile error or a script exception is data
    ''' here, exactly as in <see cref="VBScriptResult"/>: the session stays usable, and the host reads
    ''' <see cref="Diagnostics"/> or <see cref="Exception"/>.
    ''' <para>
    ''' <see cref="IsFaulted"/> is the one place the session reports an unrecoverable state: it is
    ''' <see langword="True"/> only when the submission was cancelled. Cancellation is the only failure after which
    ''' the session refuses further submissions until <see cref="IVBScriptSession.Reset"/> or
    ''' <see cref="IVBScriptSession.DisposeAsync"/>; a run-time exception leaves the session usable, matching vbi's
    ''' REPL behaviour.
    ''' </para>
    ''' </summary>
    Public NotInheritable Class VBScriptSubmissionResult

        Private ReadOnly _diagnostics As ImmutableArray(Of VBScriptDiagnostic)
        Private ReadOnly _returnValue As Object
        Private ReadOnly _printedValue As Object
        Private ReadOnly _exception As Exception
        Private ReadOnly _isFaulted As Boolean

        Friend Sub New(diagnostics As ImmutableArray(Of VBScriptDiagnostic), returnValue As Object, printedValue As Object, exception As Exception, isFaulted As Boolean)
            _diagnostics = If(diagnostics.IsDefault, ImmutableArray(Of VBScriptDiagnostic).Empty, diagnostics)
            _returnValue = returnValue
            _printedValue = printedValue
            _exception = exception
            _isFaulted = isFaulted
        End Sub

        ''' <summary>Compile and restore diagnostics of this submission; empty when it compiled cleanly.</summary>
        Public ReadOnly Property Diagnostics As ImmutableArray(Of VBScriptDiagnostic)
            Get
                Return _diagnostics
            End Get
        End Property

        ''' <summary>The raw script return value of this submission, or <see langword="Nothing"/>.</summary>
        Public ReadOnly Property ReturnValue As Object
            Get
                Return _returnValue
            End Get
        End Property

        ''' <summary>
        ''' The line vbi would print for this submission: the value covered by the script compiler's
        ''' <c>HasReturnValue</c> rule, or <see langword="Nothing"/> when the submission produced no value or threw.
        ''' It is data only; the session never writes it to <see cref="IScriptOutput"/>.
        ''' </summary>
        Public ReadOnly Property PrintedValue As Object
            Get
                Return _printedValue
            End Get
        End Property

        ''' <summary>The run-time exception the script threw, or <see langword="Nothing"/>.</summary>
        Public ReadOnly Property Exception As Exception
            Get
                Return _exception
            End Get
        End Property

        ''' <summary>
        ''' <see langword="True"/> only when the submission was cancelled; the session then accepts nothing but
        ''' <see cref="IVBScriptSession.Reset"/> and <see cref="IVBScriptSession.DisposeAsync"/>.
        ''' </summary>
        Public ReadOnly Property IsFaulted As Boolean
            Get
                Return _isFaulted
            End Get
        End Property

        Public Overrides Function ToString() As String
            Dim text = "Diagnostics=" & _diagnostics.Length
            If _isFaulted Then
                text &= "; IsFaulted=True"
            ElseIf _exception IsNot Nothing Then
                text &= "; Exception=" & _exception.Message
            End If

            Return text
        End Function

    End Class

End Namespace
