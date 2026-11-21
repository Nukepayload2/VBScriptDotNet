' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' Thrown when the script file a host asked to run does not exist. Check the path, or make it relative to
    ''' <see cref="VBScriptOptions.WorkingDirectory"/>.
    ''' </summary>
    Public Class VBScriptScriptNotFoundException
        Inherits VBScriptHostException

        Public Sub New()
            MyBase.New("The script file was not found. Check the path, or set VBScriptOptions.WorkingDirectory to the directory the path is relative to.")
        End Sub

        Public Sub New(message As String)
            MyBase.New(message)
        End Sub

        Public Sub New(message As String, innerException As Exception)
            MyBase.New(message, innerException)
        End Sub

    End Class

End Namespace
