' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' Optimization level of the generated submission IL. Mirrors the compiler's own scale without
    ''' exposing <c>Microsoft.CodeAnalysis.OptimizationLevel</c> from the public surface.
    ''' </summary>
    Public Enum VBScriptOptimizationLevel
        Debug = 0
        Release = 1
    End Enum

End Namespace
