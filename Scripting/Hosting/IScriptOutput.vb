' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Generic
Imports System.IO

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' Where script output goes. The default registration is <see cref="NullScriptOutput"/>: a library must not
    ''' write the process console. <see cref="ConsoleScriptOutput"/> is the explicit opt-in.
    ''' </summary>
    Public Interface IScriptOutput

        ReadOnly Property Out As TextWriter

        ReadOnly Property [Error] As TextWriter

    End Interface

End Namespace
