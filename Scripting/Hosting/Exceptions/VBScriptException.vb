' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' Root of every exception this library throws, so a host can catch the whole family in one clause.
    ''' <para>
    ''' The hierarchy is split on purpose: <see cref="VBScriptHostException"/> means host misuse (bad options,
    ''' occupied runner or session, missing script file, stopped host) and <see cref="VBScriptScriptException"/>
    ''' means the script itself failed to compile through an entry point that has no result object to return.
    ''' Script failures that do have a result object are data in
    ''' <see cref="VBScriptResult"/>, not exceptions.
    ''' </para>
    ''' </summary>
    Public Class VBScriptException
        Inherits Exception

        Public Sub New()
            MyBase.New()
        End Sub

        Public Sub New(message As String)
            MyBase.New(message)
        End Sub

        Public Sub New(message As String, innerException As Exception)
            MyBase.New(message, innerException)
        End Sub

    End Class

End Namespace
