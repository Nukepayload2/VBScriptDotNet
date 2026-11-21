' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable
Imports System.Threading
Imports System.Threading.Tasks

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' The restore-execution seam: turns one package request into one restore result. The default implementation
    ''' wraps the internal <c>DotNetRestoreRunner</c>; a host can replace it to change how (or whether) a restore
    ''' is executed, without touching restore coordination or package resolution.
    ''' </summary>
    Public Interface INuGetRestoreRunner

        ''' <summary>
        ''' Installed .NET SDK versions the coordinator can pick from (raw, unsorted). Returning an empty result
        ''' means "no SDK available"; the coordinator then reports its SDK-missing diagnostic without executing a
        ''' restore. A custom runner that does not use the .NET SDK can return a single placeholder version so the
        ''' restore reaches <see cref="RestoreAsync"/>.
        ''' </summary>
        Function GetInstalledSdkVersionsAsync(cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of String))

        ''' <summary>Executes one restore request and returns the raw outcome (no diagnostic interpretation).</summary>
        Function RestoreAsync(request As NuGetRestoreRequest, cancellationToken As CancellationToken) As Task(Of NuGetRestoreOutcome)

    End Interface

End Namespace
