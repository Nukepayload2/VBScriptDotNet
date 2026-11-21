' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' Host misuse, not script failure. A script that fails is data in <see cref="VBScriptResult"/>; this type is
    ''' for configuration/contract errors (bad globals type, occupied runner, missing script file, stopped host).
    ''' Messages always name the offending option and the fix.
    ''' <para>
    ''' A script that fails to compile through an entry point with no result object to return is
    ''' <see cref="VBScriptScriptException"/>, which is deliberately <b>not</b> derived from this type: catching
    ''' <see cref="VBScriptHostException"/> must not swallow a script compile failure. Both derive from
    ''' <see cref="VBScriptException"/>.
    ''' </para>
    ''' </summary>
    Public Class VBScriptHostException
        Inherits VBScriptException

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
