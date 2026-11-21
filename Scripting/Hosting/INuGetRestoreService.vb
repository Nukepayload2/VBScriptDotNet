' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable
Imports System.Threading
Imports System.Threading.Tasks

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' The pre-scan/restore seam: called before a script is compiled so that <c>#R "nuget:…"</c> references can
    ''' be restored into the package session. Returns blocking diagnostics (empty means "compile may continue").
    ''' The default implementation wraps the internal <c>NuGetRestoreCoordinator</c>; a host can replace it with
    ''' its own restore strategy.
    ''' </summary>
    Public Interface INuGetRestoreService

        ''' <summary>
        ''' Scans <paramref name="source"/> for NuGet references, restores the referenced packages and returns
        ''' diagnostics anchored at the offending <c>#R</c> line. An empty result lets compilation proceed.
        ''' </summary>
        Function PrepareCompilationAsync(source As VBScriptSource, options As VBScriptOptions, cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of VBScriptDiagnostic))

    End Interface

End Namespace
