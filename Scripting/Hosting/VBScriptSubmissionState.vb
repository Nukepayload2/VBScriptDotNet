' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' The three answers <see cref="IVBScriptSession.IsCompleteSubmission"/> gives for a line of interactive
    ''' input.
    ''' </summary>
    Public Enum VBScriptSubmissionState

        ''' <summary>The input parses cleanly and the compiler considers the submission finished; submit it.</summary>
        Complete = 0

        ''' <summary>The input stops inside a construct that needs more lines (<c>If … Then</c>, <c>Class</c>, a line continuation, …); keep reading.</summary>
        Incomplete = 1

        ''' <summary>The input has a real syntax error (for example <c>Dim x = )</c>); report it, do not wait for more input.</summary>
        Invalid = 2

    End Enum

End Namespace
