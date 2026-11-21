' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.IO
Imports System.Text
Imports Microsoft.Extensions.Logging

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' Routes script output to an <see cref="ILogger"/> line by line: <see cref="Out"/> at Information,
    ''' <see cref="Error"/> at Error. Dispose to flush a trailing partial line.
    ''' </summary>
    Public NotInheritable Class LoggerScriptOutput
        Implements IScriptOutput, IDisposable

        Private ReadOnly _logger As ILogger
        Private ReadOnly _out As LoggerTextWriter
        Private ReadOnly _error As LoggerTextWriter

        Public Sub New(logger As ILogger)
            If logger Is Nothing Then
                Throw New ArgumentNullException(NameOf(logger))
            End If

            _logger = logger
            _out = New LoggerTextWriter(logger, LogLevel.Information)
            _error = New LoggerTextWriter(logger, LogLevel.Error)
        End Sub

        Public ReadOnly Property Out As TextWriter Implements IScriptOutput.Out
            Get
                Return _out
            End Get
        End Property

        Public ReadOnly Property [Error] As TextWriter Implements IScriptOutput.Error
            Get
                Return _error
            End Get
        End Property

        Public Sub Dispose() Implements IDisposable.Dispose
            _out.Dispose()
            _error.Dispose()
        End Sub

        ''' <summary>Line-buffering writer: one log entry per completed line.</summary>
        Private NotInheritable Class LoggerTextWriter
            Inherits TextWriter

            Private ReadOnly _logger As ILogger
            Private ReadOnly _level As LogLevel
            Private ReadOnly _buffer As New StringBuilder()

            Friend Sub New(logger As ILogger, level As LogLevel)
                MyBase.New(Globalization.CultureInfo.InvariantCulture)
                _logger = logger
                _level = level
            End Sub

            Public Overrides ReadOnly Property Encoding As Encoding
                Get
                    Return Encoding.UTF8
                End Get
            End Property

            Public Overrides Sub Write(value As Char)
                If value = ControlChars.Cr Then
                    Return
                End If

                If value = ControlChars.Lf Then
                    FlushLine()
                    Return
                End If

                _buffer.Append(value)
            End Sub

            Public Overrides Sub Write(buffer As Char(), index As Integer, count As Integer)
                If buffer Is Nothing Then
                    Return
                End If

                For i = index To index + count - 1
                    Write(buffer(i))
                Next
            End Sub

            Public Overrides Sub WriteLine(value As String)
                Write(value)
                FlushLine()
            End Sub

            Public Overrides Sub Flush()
                FlushLine()
            End Sub

            Private Sub FlushLine()
                If _buffer.Length = 0 Then
                    Return
                End If

                Dim line = _buffer.ToString()
                _buffer.Clear()
                _logger.Log(_level, line)
            End Sub

            Protected Overrides Sub Dispose(disposing As Boolean)
                If disposing Then
                    FlushLine()
                End If

                MyBase.Dispose(disposing)
            End Sub
        End Class

    End Class

End Namespace
