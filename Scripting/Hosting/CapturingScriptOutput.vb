' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.IO

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' Buffers <see cref="IScriptOutput.Out"/> so the one-shot entries can hand the text back as
    ''' <see cref="VBScriptResult.Output"/>. Internal on purpose: buffering belongs to the call, not to the
    ''' contract.
    ''' </summary>
    Friend NotInheritable Class CapturingScriptOutput
        Implements IScriptOutput

        Private ReadOnly _out As New StringWriter(Globalization.CultureInfo.InvariantCulture)
        Private ReadOnly _error As New StringWriter(Globalization.CultureInfo.InvariantCulture)

        Friend Sub New()
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

        Friend ReadOnly Property OutText As String
            Get
                Return _out.ToString()
            End Get
        End Property

        Friend ReadOnly Property ErrorText As String
            Get
                Return _error.ToString()
            End Get
        End Property

    End Class

End Namespace
