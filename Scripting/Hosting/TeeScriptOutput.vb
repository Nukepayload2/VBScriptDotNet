' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.IO

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' Fans one call's script output out to two destinations: the per-call buffer that becomes
    ''' <see cref="VBScriptResult.Output"/> and the <see cref="IScriptOutput"/> the host registered (which is
    ''' <see cref="NullScriptOutput"/> unless the host asked for something else). Internal: the runner owns this
    ''' decision, it is not a contract.
    ''' </summary>
    Friend NotInheritable Class TeeScriptOutput
        Implements IScriptOutput

        Private ReadOnly _out As TeeTextWriter
        Private ReadOnly _error As TeeTextWriter

        Friend Sub New(capture As CapturingScriptOutput, target As IScriptOutput)
            If capture Is Nothing Then
                Throw New ArgumentNullException(NameOf(capture))
            End If

            If target Is Nothing Then
                Throw New ArgumentNullException(NameOf(target))
            End If

            _out = New TeeTextWriter(capture.Out, target.Out)
            _error = New TeeTextWriter(capture.Error, target.[Error])
        End Sub

        Public ReadOnly Property Out As TextWriter Implements IScriptOutput.Out
            Get
                Return _out
            End Get
        End Property

        Public ReadOnly Property [Error] As TextWriter Implements IScriptOutput.[Error]
            Get
                Return _error
            End Get
        End Property

        ''' <summary>Writes every character to both writers, so capture and host routing never diverge.</summary>
        Private NotInheritable Class TeeTextWriter
            Inherits TextWriter

            Private ReadOnly _first As TextWriter
            Private ReadOnly _second As TextWriter

            Friend Sub New(first As TextWriter, second As TextWriter)
                MyBase.New(Globalization.CultureInfo.InvariantCulture)
                _first = first
                _second = second
            End Sub

            Public Overrides ReadOnly Property Encoding As Text.Encoding
                Get
                    Return _first.Encoding
                End Get
            End Property

            Public Overrides Sub Write(value As Char)
                _first.Write(value)
                _second.Write(value)
            End Sub

            Public Overrides Sub Write(value As String)
                _first.Write(value)
                _second.Write(value)
            End Sub

            Public Overrides Sub WriteLine(value As String)
                _first.WriteLine(value)
                _second.WriteLine(value)
            End Sub

            Public Overrides Sub Flush()
                _first.Flush()
                _second.Flush()
            End Sub

        End Class

    End Class

End Namespace
