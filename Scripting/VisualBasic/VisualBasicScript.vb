' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

Namespace Microsoft.CodeAnalysis.VisualBasic.Scripting

    ''' <summary>
    ''' A factory for creating and running Visual Basic scripts.
    ''' </summary>
    Public NotInheritable Class VisualBasicScript
        Private Sub New()
        End Sub

        ''' <summary>
        ''' Create a new Visual Basic script.
        ''' </summary>
        Public Shared Function Create(Of T)(code As String,
                                            Optional options As ScriptOptions = Nothing,
                                            Optional globalsType As Type = Nothing,
                                            Optional assemblyLoader As InteractiveAssemblyLoader = Nothing) As Script(Of T)
            Return Script.CreateInitialScript(Of T)(VisualBasicScriptCompiler.Instance, SourceText.From(If(code, String.Empty), options?.FileEncoding), options, globalsType, assemblyLoader)
        End Function

        ''' <summary>
        ''' Create a new Visual Basic script.
        ''' </summary>
        Public Shared Function Create(Of T)(code As Stream,
                                            Optional options As ScriptOptions = Nothing,
                                            Optional globalsType As Type = Nothing,
                                            Optional assemblyLoader As InteractiveAssemblyLoader = Nothing) As Script(Of T)
            Return Script.CreateInitialScript(Of T)(VisualBasicScriptCompiler.Instance, SourceText.From(code, options?.FileEncoding), options, globalsType, assemblyLoader)
        End Function

        ''' <summary>
        ''' Create a new Visual Basic script.
        ''' </summary>
        Public Shared Function Create(code As String,
                                      Optional options As ScriptOptions = Nothing,
                                      Optional globalsType As Type = Nothing,
                                      Optional assemblyLoader As InteractiveAssemblyLoader = Nothing) As Script(Of Object)
            Return Create(Of Object)(code, options, globalsType, assemblyLoader)
        End Function

        ''' <summary>
        ''' Create a new Visual Basic script.
        ''' </summary>
        Public Shared Function Create(code As Stream,
                                      Optional options As ScriptOptions = Nothing,
                                      Optional globalsType As Type = Nothing,
                                      Optional assemblyLoader As InteractiveAssemblyLoader = Nothing) As Script(Of Object)
            Return Create(Of Object)(code, options, globalsType, assemblyLoader)
        End Function

        ''' <summary>
        ''' Run a Visual Basic script.
        ''' </summary>
        Public Shared Function RunAsync(Of T)(code As String,
                                              Optional options As ScriptOptions = Nothing,
                                              Optional globals As Object = Nothing,
                                              Optional cancellationToken As CancellationToken = Nothing) As Task(Of ScriptState(Of T))
            Return Create(Of T)(code, options, globals?.GetType()).RunAsync(globals, cancellationToken)
        End Function

        ''' <summary>
        ''' Run a Visual Basic script.
        ''' </summary>
        Public Shared Function RunAsync(Of T)(code As Stream,
                                              Optional options As ScriptOptions = Nothing,
                                              Optional globals As Object = Nothing,
                                              Optional globalsType As Type = Nothing,
                                              Optional cancellationToken As CancellationToken = Nothing) As Task(Of ScriptState(Of T))
            Return Create(Of T)(code, options, globalsType).RunAsync(globals, cancellationToken)
        End Function

        ''' <summary>
        ''' Run a Visual Basic script.
        ''' </summary>
        Public Shared Function RunAsync(code As String,
                                        Optional options As ScriptOptions = Nothing,
                                        Optional globals As Object = Nothing,
                                        Optional cancellationToken As CancellationToken = Nothing) As Task(Of ScriptState(Of Object))
            Return RunAsync(Of Object)(code, options, globals, cancellationToken)
        End Function

        ''' <summary>
        ''' Run a Visual Basic script.
        ''' </summary>
        Public Shared Function RunAsync(code As Stream,
                                        Optional options As ScriptOptions = Nothing,
                                        Optional globals As Object = Nothing,
                                        Optional globalsType As Type = Nothing,
                                        Optional cancellationToken As CancellationToken = Nothing) As Task(Of ScriptState(Of Object))
            Return RunAsync(Of Object)(code, options, globals, globalsType, cancellationToken)
        End Function

        ''' <summary>
        ''' Run a Visual Basic script and return its resulting value.
        ''' </summary>
        Public Shared Function EvaluateAsync(Of T)(code As String,
                                                   Optional options As ScriptOptions = Nothing,
                                                   Optional globals As Object = Nothing,
                                                   Optional cancellationToken As CancellationToken = Nothing) As Task(Of T)
            Return RunAsync(Of T)(code, options, globals, cancellationToken).GetEvaluationResultAsync()
        End Function

        ''' <summary>
        ''' Run a Visual Basic script and return its resulting value.
        ''' </summary>
        Public Shared Function EvaluateAsync(Of T)(code As Stream,
                                                   Optional options As ScriptOptions = Nothing,
                                                   Optional globals As Object = Nothing,
                                                   Optional globalsType As Type = Nothing,
                                                   Optional cancellationToken As CancellationToken = Nothing) As Task(Of T)
            Return RunAsync(Of T)(code, options, globals, globalsType, cancellationToken).GetEvaluationResultAsync()
        End Function

        ''' <summary>
        ''' Run a Visual Basic script and return its resulting value.
        ''' </summary>
        Public Shared Function EvaluateAsync(code As String,
                                             Optional options As ScriptOptions = Nothing,
                                             Optional globals As Object = Nothing,
                                             Optional cancellationToken As CancellationToken = Nothing) As Task(Of Object)
            Return EvaluateAsync(Of Object)(code, options, globals, cancellationToken)
        End Function

        ''' <summary>
        ''' Run a Visual Basic script and return its resulting value.
        ''' </summary>
        Public Shared Function EvaluateAsync(code As Stream,
                                             Optional options As ScriptOptions = Nothing,
                                             Optional globals As Object = Nothing,
                                             Optional globalsType As Type = Nothing,
                                             Optional cancellationToken As CancellationToken = Nothing) As Task(Of Object)
            Return EvaluateAsync(Of Object)(code, options, globals, globalsType, cancellationToken)
        End Function

        Public Shared Function RunInteractive(args() As String, vbiDirectory As String, interactiveResponseFileName As String) As Integer
            Return RunInteractiveAsync(args, vbiDirectory, interactiveResponseFileName).GetAwaiter().GetResult()
        End Function

        Public Shared Function RunInteractiveAsync(args() As String, vbiDirectory As String, interactiveResponseFileName As String) As Task(Of Integer)
            Dim buildPaths = New BuildPaths(
                clientDir:=vbiDirectory,
                workingDir:=Directory.GetCurrentDirectory(),
                sdkDir:=Path.GetDirectoryName(GetType(Object).Assembly.Location),
                tempDir:=Path.GetTempPath())

            Return RunInteractiveAsync(args, ConsoleIO.Default, buildPaths, interactiveResponseFileName)
        End Function

        ''' <summary>
        ''' The vbi entry point split in two: file scripts and <c>/check</c> execute through the VB script
        ''' engine (<see cref="ScriptCommandLineHost"/> + <see cref="ScriptRunnerCore"/>), while the interactive
        ''' shapes still go through the vendored <see cref="CommandLineRunner"/> and its REPL loop - called,
        ''' never modified.
        ''' <para>
        ''' The optional <see cref="ConsoleIO"/> and <see cref="BuildPaths"/> overload exists so the test harness
        ''' can drive this exact construction with separated stdout/stderr and a controlled working directory,
        ''' instead of rebuilding a second "vbi" out of the runner.
        ''' </para>
        ''' </summary>
        Friend Shared Async Function RunInteractiveAsync(args() As String, console As ConsoleIO, buildPaths As BuildPaths, interactiveResponseFileName As String) As Task(Of Integer)
            Dim compiler = New VisualBasicInteractiveCompiler(
                responseFile:=Path.Combine(buildPaths.ClientDirectory, interactiveResponseFileName),
                buildPaths:=buildPaths,
                args:=args,
                analyzerLoader:=New NotImplementedAnalyzerLoader())

            ' NuGet restore wiring: interactive-only (compile mode never reaches this point).
            ' A NuGetPackageSession + NuGetPackageResolverImpl + NuGetRestoreCoordinator are threaded through
            ' the host/runner optional parameters; with no nuget references the coordinator returns
            ' empty and the resolver is never consulted, so the default path stays unchanged. One shared
            ' InteractiveAssemblyLoader is created here and handed to both entries (so every submission's
            ' ScriptBuilder registers into it) and to the coordinator (so a successful restore pushes the
            ' runtime-lib overrides and native probe roots before the script runs).
            Dim session = New NuGetPackageSession()
            Dim assemblyLoader As New InteractiveAssemblyLoader()
            Dim packageResolver = New NuGetPackageResolverImpl(session)
            Dim restoreCoordinator = New NuGetRestoreCoordinator(session, loader:=assemblyLoader)

            If ScriptCommandLineHost.IsScriptExecutionInvocation(compiler.Arguments) Then
                Dim host = New ScriptCommandLineHost(
                    console,
                    compiler,
                    VisualBasicScriptCompiler.Instance,
                    VisualBasicObjectFormatter.Instance,
                    packageResolver,
                    restoreCoordinator,
                    assemblyLoader)

                Return Await host.RunAsync().ConfigureAwait(False)
            End If

            Dim runner = New CommandLineRunner(
                console,
                compiler,
                VisualBasicScriptCompiler.Instance,
                VisualBasicObjectFormatter.Instance,
                packageResolver,
                restoreCoordinator,
                assemblyLoader)

            Return Await runner.RunInteractiveAsync().ConfigureAwait(False)
        End Function

    End Class

End Namespace

