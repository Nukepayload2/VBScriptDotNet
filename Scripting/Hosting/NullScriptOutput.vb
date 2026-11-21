' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.IO

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>Drops everything. The default <see cref="IScriptOutput"/> so a library never pollutes stdout.</summary>
    Public NotInheritable Class NullScriptOutput
        Implements IScriptOutput

        Public Shared ReadOnly Instance As New NullScriptOutput()

        Public Sub New()
        End Sub

        Public ReadOnly Property Out As TextWriter Implements IScriptOutput.Out
            Get
                Return TextWriter.Null
            End Get
        End Property

        Public ReadOnly Property [Error] As TextWriter Implements IScriptOutput.Error
            Get
                Return TextWriter.Null
            End Get
        End Property

    End Class

End Namespace
