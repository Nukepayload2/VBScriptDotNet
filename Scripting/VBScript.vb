' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting
Imports Nukepayload2.VBScriptDotNet.Hosting

Namespace Nukepayload2.VBScriptDotNet.Scripting

    ''' <summary>
    ''' One-shot script entry points: compile or run a snippet and get a <see cref="VBScriptResult"/> back.
    ''' Intended for one-shot calls and tests; an application integrates through the DI runner, which owns
    ''' lifetime, cancellation and output services. There is no container behind these methods.
    ''' <para>
    ''' A failed script is data (<see cref="VBScriptResult.ExitCode"/> plus diagnostics plus exception); host
    ''' misuse throws <see cref="VBScriptHostException"/>. <see cref="EvaluateAsync(Of T)"/> has no result object to
    ''' return on failure, so it throws <see cref="VBScriptHostException"/> there.
    ''' </para>
    ''' </summary>
    Public NotInheritable Class VBScript

        Private Sub New()
        End Sub

        ' The trim/AOT annotations only exist on net10; net472 has no such attribute types and this project
        ' deliberately takes no polyfill package for them. VB cannot split an attribute list from its declaration
        ' with #If/#End If, so each annotated entry is a thin wrapper and the implementation below is unannotated.

#If NET Then
        ''' <summary>Runs a snippet and returns its exit code, diagnostics, return value and captured <c>Print</c> text.</summary>
        <System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Compiles and loads assemblies dynamically; the script and its references cannot be statically analyzed.")>
        <System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Compiles and loads assemblies dynamically, which is not available in AOT-only applications.")>
        Public Shared Function RunAsync(code As String, Optional options As VBScriptOptions = Nothing, Optional globals As Object = Nothing, Optional cancellationToken As CancellationToken = Nothing) As Task(Of VBScriptResult)
            Return RunAsyncCore(code, options, globals, cancellationToken)
        End Function
#Else
        ''' <summary>Runs a snippet and returns its exit code, diagnostics, return value and captured <c>Print</c> text.</summary>
        Public Shared Function RunAsync(code As String, Optional options As VBScriptOptions = Nothing, Optional globals As Object = Nothing, Optional cancellationToken As CancellationToken = Nothing) As Task(Of VBScriptResult)
            Return RunAsyncCore(code, options, globals, cancellationToken)
        End Function
#End If

        Private Shared Async Function RunAsyncCore(code As String, options As VBScriptOptions, globals As Object, cancellationToken As CancellationToken) As Task(Of VBScriptResult)
            Dim effectiveOptions = VBScriptHostInternals.CreateEffectiveOptions(options)
            Dim output As New CapturingScriptOutput()
            Dim resolvedGlobals = VBScriptHostInternals.ResolveGlobals(effectiveOptions, globals, output)

            Using loader As New InteractiveAssemblyLoader()
                Dim request = VBScriptHostInternals.CreateRunRequest(
                    effectiveOptions,
                    VBScriptHostInternals.CreateSourceText(code, effectiveOptions),
                    Nothing,
                    resolvedGlobals,
                    loader,
                    compileOnly:=False)

                Dim outcome = Await ScriptRunnerCore.Instance.RunAsync(request, cancellationToken).ConfigureAwait(False)
                Return VBScriptHostInternals.MapRunOutcome(outcome, output.OutText)
            End Using
        End Function

#If NET Then
        ''' <summary>Runs a snippet and returns the value of its <c>Return</c> expression, converted to <typeparamref name="T"/>.</summary>
        <System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Compiles and loads assemblies dynamically; the script and its references cannot be statically analyzed.")>
        <System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Compiles and loads assemblies dynamically, which is not available in AOT-only applications.")>
        Public Shared Function EvaluateAsync(Of T)(code As String, Optional options As VBScriptOptions = Nothing, Optional globals As Object = Nothing, Optional cancellationToken As CancellationToken = Nothing) As Task(Of T)
            Return EvaluateAsyncCore(Of T)(code, options, globals, cancellationToken)
        End Function
#Else
        ''' <summary>Runs a snippet and returns the value of its <c>Return</c> expression, converted to <typeparamref name="T"/>.</summary>
        Public Shared Function EvaluateAsync(Of T)(code As String, Optional options As VBScriptOptions = Nothing, Optional globals As Object = Nothing, Optional cancellationToken As CancellationToken = Nothing) As Task(Of T)
            Return EvaluateAsyncCore(Of T)(code, options, globals, cancellationToken)
        End Function
#End If

        Private Shared Async Function EvaluateAsyncCore(Of T)(code As String, options As VBScriptOptions, globals As Object, cancellationToken As CancellationToken) As Task(Of T)
            Dim effectiveOptions = VBScriptHostInternals.CreateEffectiveOptions(options)
            Dim output As New CapturingScriptOutput()
            Dim resolvedGlobals = VBScriptHostInternals.ResolveGlobals(effectiveOptions, globals, output)

            Using loader As New InteractiveAssemblyLoader()
                ' The typed evaluation channel is shared with the DI runner (VBScriptHostInternals.EvaluateAsync), so
                ' the two entries cannot drift: object-returning session, compile errors as
                ' VBScriptScriptException, run-time exceptions rethrown untouched.
                Return Await VBScriptHostInternals.EvaluateAsync(Of T)(
                    effectiveOptions,
                    VBScriptHostInternals.CreateSourceText(code, effectiveOptions),
                    resolvedGlobals,
                    loader,
                    cancellationToken).ConfigureAwait(False)
            End Using
        End Function

#If NET Then
        ''' <summary>Compiles a snippet without running it (<c>vbi /check</c> semantics): diagnostics plus exit code.</summary>
        <System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Compiles and loads assemblies dynamically; the script and its references cannot be statically analyzed.")>
        <System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Compiles and loads assemblies dynamically, which is not available in AOT-only applications.")>
        Public Shared Function CompileAsync(code As String, Optional options As VBScriptOptions = Nothing, Optional cancellationToken As CancellationToken = Nothing) As Task(Of VBScriptResult)
            Return CompileAsyncCore(code, options, cancellationToken)
        End Function
#Else
        ''' <summary>Compiles a snippet without running it (<c>vbi /check</c> semantics): diagnostics plus exit code.</summary>
        Public Shared Function CompileAsync(code As String, Optional options As VBScriptOptions = Nothing, Optional cancellationToken As CancellationToken = Nothing) As Task(Of VBScriptResult)
            Return CompileAsyncCore(code, options, cancellationToken)
        End Function
#End If

        Private Shared Async Function CompileAsyncCore(code As String, options As VBScriptOptions, cancellationToken As CancellationToken) As Task(Of VBScriptResult)
            Dim effectiveOptions = VBScriptHostInternals.CreateEffectiveOptions(options)
            Dim output As New CapturingScriptOutput()
            Dim resolvedGlobals = VBScriptHostInternals.ResolveGlobals(effectiveOptions, Nothing, output)

            Using loader As New InteractiveAssemblyLoader()
                Dim request = VBScriptHostInternals.CreateRunRequest(
                    effectiveOptions,
                    VBScriptHostInternals.CreateSourceText(code, effectiveOptions),
                    Nothing,
                    resolvedGlobals,
                    loader,
                    compileOnly:=True)

                Dim outcome = Await ScriptRunnerCore.Instance.RunAsync(request, cancellationToken).ConfigureAwait(False)
                Return VBScriptHostInternals.MapRunOutcome(outcome, output.OutText)
            End Using
        End Function

    End Class

End Namespace
