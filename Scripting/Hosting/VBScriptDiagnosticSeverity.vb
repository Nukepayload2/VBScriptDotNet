' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' Severity of a <see cref="VBScriptDiagnostic"/>. Mirrors the compiler's own scale without exposing
    ''' <c>Microsoft.CodeAnalysis.DiagnosticSeverity</c> from the public surface.
    ''' </summary>
    Public Enum VBScriptDiagnosticSeverity
        Hidden = 0
        Info = 1
        Warning = 2
        [Error] = 3
    End Enum

End Namespace
