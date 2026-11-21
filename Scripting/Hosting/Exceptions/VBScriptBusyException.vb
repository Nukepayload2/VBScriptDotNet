' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' Thrown when the same runner or session is asked to execute twice concurrently. Concurrency is not a
    ''' promised capability; this makes the misuse explicit instead of corrupting state silently.
    ''' </summary>
    Public Class VBScriptBusyException
        Inherits VBScriptHostException

        Public Sub New()
            MyBase.New("This VBScript runner (or session) is already executing another submission. " &
                       "A runner or session is not thread-safe: await the first call before starting the next one, " &
                       "or use one runner per concurrent worker.")
        End Sub

        Public Sub New(message As String)
            MyBase.New(message)
        End Sub

        Public Sub New(message As String, innerException As Exception)
            MyBase.New(message, innerException)
        End Sub

    End Class

End Namespace
