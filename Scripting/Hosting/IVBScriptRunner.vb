' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Generic
Imports System.Threading
Imports System.Threading.Tasks

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' Runs scripts inside a host. Resolve it from the container with
    ''' <c>GetRequiredService(Of IVBScriptRunner)</c>; it is registered transient, so one instance serves one unit
    ''' of work.
    ''' <para>
    ''' A runner is not thread-safe: calling it again before the previous call completed throws
    ''' <see cref="VBScriptBusyException"/> instead of silently corrupting state. Use one runner per concurrent
    ''' worker.
    ''' </para>
    ''' <para>
    ''' Script failures are data (<see cref="VBScriptResult"/>: exit code, diagnostics, output, run-time exception).
    ''' Host misuse throws <see cref="VBScriptHostException"/> (missing script file, stopped host, occupied runner).
    ''' <see cref="EvaluateAsync(Of T)"/> has no result object to carry a failure, so a compile error there throws
    ''' <see cref="VBScriptScriptException"/>, a script run-time exception is rethrown untouched, and a conversion
    ''' failure throws <see cref="VBScriptHostException"/>.
    ''' </para>
    ''' </summary>
    Public Interface IVBScriptRunner

        ' The trim/AOT attributes only exist on net10; net472 has no such attribute types and this project takes no
        ' polyfill package for them. VB cannot split an attribute list from its declaration with #If/#End If, so the
        ' declarations are repeated per target framework.

#If NET Then
        ''' <summary>Runs a script file and returns its exit code, diagnostics, return value, captured output and run-time exception.</summary>
        <System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Compiles and loads assemblies dynamically; the script and its references cannot be statically analyzed.")>
        <System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Compiles and loads assemblies dynamically, which is not available in AOT-only applications.")>
        Function RunFileAsync(path As String, Optional arguments As IReadOnlyList(Of String) = Nothing, Optional cancellationToken As CancellationToken = Nothing) As Task(Of VBScriptResult)

        ''' <summary>Runs a snippet and returns its exit code, diagnostics, return value, captured output and run-time exception.</summary>
        <System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Compiles and loads assemblies dynamically; the script and its references cannot be statically analyzed.")>
        <System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Compiles and loads assemblies dynamically, which is not available in AOT-only applications.")>
        Function RunCodeAsync(code As String, Optional cancellationToken As CancellationToken = Nothing) As Task(Of VBScriptResult)

        ''' <summary>Compiles a script file without running it (<c>vbi /check</c> semantics): diagnostics plus exit code.</summary>
        <System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Compiles and loads assemblies dynamically; the script and its references cannot be statically analyzed.")>
        <System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Compiles and loads assemblies dynamically, which is not available in AOT-only applications.")>
        Function CompileFileAsync(path As String, Optional cancellationToken As CancellationToken = Nothing) As Task(Of VBScriptResult)

        ''' <summary>Compiles a snippet without running it (<c>vbi /check</c> semantics): diagnostics plus exit code.</summary>
        <System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Compiles and loads assemblies dynamically; the script and its references cannot be statically analyzed.")>
        <System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Compiles and loads assemblies dynamically, which is not available in AOT-only applications.")>
        Function CompileCodeAsync(code As String, Optional cancellationToken As CancellationToken = Nothing) As Task(Of VBScriptResult)

        ''' <summary>Runs a snippet and returns the value of its <c>Return</c> expression, converted to <typeparamref name="T"/>.</summary>
        <System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Compiles and loads assemblies dynamically; the script and its references cannot be statically analyzed.")>
        <System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Compiles and loads assemblies dynamically, which is not available in AOT-only applications.")>
        Function EvaluateAsync(Of T)(code As String, Optional cancellationToken As CancellationToken = Nothing) As Task(Of T)
#Else
        ''' <summary>Runs a script file and returns its exit code, diagnostics, return value, captured output and run-time exception.</summary>
        Function RunFileAsync(path As String, Optional arguments As IReadOnlyList(Of String) = Nothing, Optional cancellationToken As CancellationToken = Nothing) As Task(Of VBScriptResult)

        ''' <summary>Runs a snippet and returns its exit code, diagnostics, return value, captured output and run-time exception.</summary>
        Function RunCodeAsync(code As String, Optional cancellationToken As CancellationToken = Nothing) As Task(Of VBScriptResult)

        ''' <summary>Compiles a script file without running it (<c>vbi /check</c> semantics): diagnostics plus exit code.</summary>
        Function CompileFileAsync(path As String, Optional cancellationToken As CancellationToken = Nothing) As Task(Of VBScriptResult)

        ''' <summary>Compiles a snippet without running it (<c>vbi /check</c> semantics): diagnostics plus exit code.</summary>
        Function CompileCodeAsync(code As String, Optional cancellationToken As CancellationToken = Nothing) As Task(Of VBScriptResult)

        ''' <summary>Runs a snippet and returns the value of its <c>Return</c> expression, converted to <typeparamref name="T"/>.</summary>
        Function EvaluateAsync(Of T)(code As String, Optional cancellationToken As CancellationToken = Nothing) As Task(Of T)
#End If

    End Interface

End Namespace
