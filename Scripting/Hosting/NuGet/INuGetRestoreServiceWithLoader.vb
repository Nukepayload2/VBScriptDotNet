' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis.Scripting.Hosting

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' Internal capability of the default restore service: the execution path owns one
    ''' <see cref="InteractiveAssemblyLoader"/> per call and must let the restore push the restored runtime assets
    ''' and native probe roots into that loader before the script compiles and runs. The public
    ''' <see cref="INuGetRestoreService"/> signature deliberately has no loader, so the adapter checks for this
    ''' Friend interface and falls back to the public method for any other implementation.
    ''' </summary>
    Friend Interface INuGetRestoreServiceWithLoader

        ''' <summary>
        ''' Same contract as <see cref="INuGetRestoreService.PrepareCompilationAsync"/>, plus the loader the caller
        ''' created for this execution.
        ''' </summary>
        Function PrepareCompilationAsync(source As VBScriptSource, options As VBScriptOptions, loader As InteractiveAssemblyLoader, cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of VBScriptDiagnostic))

    End Interface

End Namespace
