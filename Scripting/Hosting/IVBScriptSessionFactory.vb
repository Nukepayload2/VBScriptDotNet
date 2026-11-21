' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' Creates stateful script sessions (for example the submission loop behind an immediate window). Registered as
    ''' a singleton by <c>AddVBScripting</c>; resolve it from the container and keep the sessions it returns out of
    ''' the container: a session's lifetime is a window or a panel, not a request.
    ''' <para>
    ''' The factory only constructs: it has no state of its own, so one instance may create any number of
    ''' independent sessions. Each session owns its assembly loader and its re-entry guard.
    ''' </para>
    ''' </summary>
    Public Interface IVBScriptSessionFactory

#If NET Then
        ''' <summary>
        ''' Creates a session over a copy of <paramref name="options"/> (the vbi default set is applied to the copy;
        ''' the caller's object is not mutated). The session keeps one globals instance across submissions and one
        ''' initial options snapshot for <see cref="IVBScriptSession.Reset"/>.
        ''' </summary>
        <System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Compiles and loads assemblies dynamically; the script and its references cannot be statically analyzed.")>
        <System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Compiles and loads assemblies dynamically, which is not available in AOT-only applications.")>
        Function CreateSession(options As VBScriptOptions) As IVBScriptSession
#Else
        ''' <summary>
        ''' Creates a session over a copy of <paramref name="options"/> (the vbi default set is applied to the copy;
        ''' the caller's object is not mutated). The session keeps one globals instance across submissions and one
        ''' initial options snapshot for <see cref="IVBScriptSession.Reset"/>.
        ''' </summary>
        Function CreateSession(options As VBScriptOptions) As IVBScriptSession
#End If

    End Interface

End Namespace
