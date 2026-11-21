' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' Result of one one-shot execution or compilation. A script failure (compile error, script exception,
    ''' non-zero <c>Return</c>) is data here: <see cref="ExitCode"/> plus <see cref="Diagnostics"/> plus
    ''' <see cref="Exception"/>. Host misuse is reported with <see cref="VBScriptHostException"/> instead.
    ''' </summary>
    Public NotInheritable Class VBScriptResult

        Private ReadOnly _exitCode As Integer
        Private ReadOnly _returnValue As Object
        Private ReadOnly _diagnostics As ImmutableArray(Of VBScriptDiagnostic)
        Private ReadOnly _output As String
        Private ReadOnly _exception As Exception

        Friend Sub New(exitCode As Integer, returnValue As Object, diagnostics As ImmutableArray(Of VBScriptDiagnostic), output As String, exception As Exception)
            _exitCode = exitCode
            _returnValue = returnValue
            _diagnostics = If(diagnostics.IsDefault, ImmutableArray(Of VBScriptDiagnostic).Empty, diagnostics)
            _output = If(output, String.Empty)
            _exception = exception
        End Sub

        ''' <summary>
        ''' Exit code with the same meaning vbi gives it: the script's <c>Return</c> value, 1 for compile or
        ''' restore errors, or the exception HRESULT when the script threw. 0 when the script produced no value.
        ''' </summary>
        Public ReadOnly Property ExitCode As Integer
            Get
                Return _exitCode
            End Get
        End Property

        ''' <summary>The script's <c>Return</c> value, or <see langword="Nothing"/>.</summary>
        Public ReadOnly Property ReturnValue As Object
            Get
                Return _returnValue
            End Get
        End Property

        ''' <summary>Compile or restore diagnostics; empty when the submission compiled cleanly.</summary>
        Public ReadOnly Property Diagnostics As ImmutableArray(Of VBScriptDiagnostic)
            Get
                Return _diagnostics
            End Get
        End Property

        ''' <summary>
        ''' Text written by <see cref="VBScriptGlobals.Print"/> during this run. <c>Console.WriteLine</c> is not
        ''' captured: the host owns the console, the library does not.
        ''' </summary>
        Public ReadOnly Property Output As String
            Get
                Return _output
            End Get
        End Property

        ''' <summary>The run-time exception the script threw, or <see langword="Nothing"/>.</summary>
        Public ReadOnly Property Exception As Exception
            Get
                Return _exception
            End Get
        End Property

        Public Overrides Function ToString() As String
            Dim text = "ExitCode=" & _exitCode & "; Diagnostics=" & _diagnostics.Length
            If _exception IsNot Nothing Then
                text &= "; Exception=" & _exception.Message
            End If

            Return text
        End Function

    End Class

End Namespace
