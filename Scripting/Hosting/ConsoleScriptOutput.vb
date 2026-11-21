' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.IO

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' Writes to the process console. Only used when the host asks for it (<c>UseConsoleOutput()</c>), never by
    ''' default.
    ''' </summary>
    Public NotInheritable Class ConsoleScriptOutput
        Implements IScriptOutput

        Public Shared ReadOnly Instance As New ConsoleScriptOutput()

        Public Sub New()
        End Sub

        Public ReadOnly Property Out As TextWriter Implements IScriptOutput.Out
            Get
                Return Console.Out
            End Get
        End Property

        Public ReadOnly Property [Error] As TextWriter Implements IScriptOutput.Error
            Get
                Return Console.Error
            End Get
        End Property

    End Class

End Namespace
