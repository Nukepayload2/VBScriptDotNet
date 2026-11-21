' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.Text

Namespace Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

    ''' <summary>
    ''' The file-script half of vbi's command line. It is a deliberately thin adapter over the same sequence
    ''' the vendored command-line runner uses before execution (display switches, source-file validation and
    ''' reading, <see cref="ScriptOptions"/> construction, argument diagnostics), and then hands the execution
    ''' itself to <see cref="ScriptRunnerCore"/>.
    ''' <para>
    ''' vbi splits at <see cref="VisualBasicScript.RunInteractiveAsync"/> instead of editing the vendored runner:
    ''' when the invocation is a file script or <c>/check</c>, this host runs it; the interactive-only shapes
    ''' still go through <see cref="CommandLineRunner"/>, whose REPL loop is untouched (only called, never
    ''' modified). Keeping both halves here also keeps the product's own execution semantics out of the shared
    ''' scripting assembly.
    ''' </para>
    ''' </summary>
    Friend NotInheritable Class ScriptCommandLineHost

        Private ReadOnly _console As ConsoleIO
        Private ReadOnly _compiler As CommonCompiler
        Private ReadOnly _scriptCompiler As VisualBasicScriptCompiler
        Private ReadOnly _objectFormatter As ObjectFormatter
        Private ReadOnly _packageResolver As NuGetPackageResolver
        Private ReadOnly _restoreCoordinator As IScriptRestoreCoordinator
        Private ReadOnly _assemblyLoader As InteractiveAssemblyLoader

        Friend Sub New(
                console As ConsoleIO,
                compiler As CommonCompiler,
                scriptCompiler As VisualBasicScriptCompiler,
                objectFormatter As ObjectFormatter,
                Optional packageResolver As NuGetPackageResolver = Nothing,
                Optional nuGetRestoreCoordinator As IScriptRestoreCoordinator = Nothing,
                Optional assemblyLoader As InteractiveAssemblyLoader = Nothing)
            If console Is Nothing Then
                Throw New ArgumentNullException(NameOf(console))
            End If

            If compiler Is Nothing Then
                Throw New ArgumentNullException(NameOf(compiler))
            End If

            If scriptCompiler Is Nothing Then
                Throw New ArgumentNullException(NameOf(scriptCompiler))
            End If

            If objectFormatter Is Nothing Then
                Throw New ArgumentNullException(NameOf(objectFormatter))
            End If

            _console = console
            _compiler = compiler
            _scriptCompiler = scriptCompiler
            _objectFormatter = objectFormatter
            _packageResolver = packageResolver
            _restoreCoordinator = nuGetRestoreCoordinator
            _assemblyLoader = assemblyLoader
        End Sub

        ''' <summary>
        ''' True when the invocation must execute a file script or <c>/check</c> instead of entering the REPL.
        ''' This is exactly the branch the vendored runner takes: <c>/check</c> forces the script path even
        ''' with <c>/i</c>, and interactive mode without <c>/check</c> stays in the REPL (with an optional
        ''' initial source file).
        ''' </summary>
        Friend Shared Function IsScriptExecutionInvocation(arguments As CommandLineArguments) As Boolean
            Return arguments.Check OrElse Not arguments.InteractiveMode
        End Function

        ''' <summary>vbi.exe's file-script entry: error logger lifetime plus the exit code.</summary>
        Friend Function Run() As Integer
            Return RunAsync().GetAwaiter().GetResult()
        End Function

        Friend Async Function RunAsync() As Task(Of Integer)
            Dim errorLogger As SarifErrorLogger = Nothing
            If _compiler.Arguments.ErrorLogOptions?.Path IsNot Nothing Then
                errorLogger = _compiler.GetErrorLogger(_console.Error)
                If errorLogger Is Nothing Then
                    Return CommonCompiler.Failed
                End If
            End If

            Using errorLogger
                Return Await RunCoreAsync(errorLogger).ConfigureAwait(False)
            End Using
        End Function

        Private Async Function RunCoreAsync(errorLogger As ErrorLogger) As Task(Of Integer)
            Dim sourceFiles = _compiler.Arguments.SourceFiles

            If _compiler.Arguments.DisplayVersion Then
                _compiler.PrintVersion(_console.Out)
                Return CommonCompiler.Succeeded
            End If

            If _compiler.Arguments.DisplayLangVersions Then
                _compiler.PrintLangVersions(_console.Out)
                Return CommonCompiler.Succeeded
            End If

            If sourceFiles.IsEmpty AndAlso _compiler.Arguments.DisplayLogo Then
                _compiler.PrintLogo(_console.Out)

                If Not _compiler.Arguments.DisplayHelp Then
                    _console.Out.WriteLine(ScriptingResources.HelpPrompt)
                End If
            End If

            If _compiler.Arguments.DisplayHelp Then
                _compiler.PrintHelp(_console.Out)
                Return CommonCompiler.Succeeded
            End If

            Dim code As SourceText = Nothing
            Dim diagnosticsInfos As New List(Of DiagnosticInfo)()

            If Not sourceFiles.IsEmpty Then
                If sourceFiles.Length > 1 OrElse Not sourceFiles(0).IsScript Then
                    diagnosticsInfos.Add(New DiagnosticInfo(_compiler.MessageProvider, _compiler.MessageProvider.ERR_ExpectedSingleScript))
                Else
                    code = _compiler.TryReadFileContent(sourceFiles(0), diagnosticsInfos)
                End If
            End If

            ' Only emit symbols for non-interactive mode, exactly as the runner does.
            Dim emitDebugInformation = Not _compiler.Arguments.InteractiveMode

            Dim scriptPathOpt = If(sourceFiles.IsEmpty, Nothing, sourceFiles(0).Path)
            Dim scriptOptions = GetScriptOptions(_compiler.Arguments, scriptPathOpt, _compiler.MessageProvider, diagnosticsInfos, emitDebugInformation, _packageResolver)

            Dim errors = _compiler.Arguments.Errors.Concat(diagnosticsInfos.Select(Function(item) Diagnostic.Create(item)))
            If _compiler.ReportDiagnostics(errors, _console.Error, errorLogger, compilation:=Nothing) Then
                Return CommonCompiler.Failed
            End If

            Dim cancellationToken As New CancellationToken()
            Return Await ExecuteScriptAsync(scriptOptions, code, errorLogger, cancellationToken).ConfigureAwait(False)
        End Function

        ''' <summary>
        ''' Same option shape the vendored runner builds for a file script: platform metadata resolver and
        ''' logging source resolver, resolved references, imports from the compilation options, and the
        ''' script parse options from the command line.
        ''' </summary>
        Private Shared Function GetScriptOptions(
                arguments As CommandLineArguments,
                scriptPathOpt As String,
                messageProvider As CommonMessageProvider,
                diagnostics As List(Of DiagnosticInfo),
                emitDebugInformation As Boolean,
                packageResolver As NuGetPackageResolver) As ScriptOptions

            Dim touchedFilesLoggerOpt As TouchedFileLogger = If(arguments.TouchedFilesPath IsNot Nothing, New TouchedFileLogger(), Nothing)

            Dim metadataResolver = CommandLineRunner.GetMetadataReferenceResolver(arguments, touchedFilesLoggerOpt, packageResolver)
            Dim sourceResolver = CommandLineRunner.GetSourceReferenceResolver(arguments, touchedFilesLoggerOpt)

            Dim resolvedReferences As New List(Of MetadataReference)()
            If Not arguments.ResolveMetadataReferences(metadataResolver, diagnostics, messageProvider, resolvedReferences) Then
                ' can't resolve some references
                Return Nothing
            End If

            Return New ScriptOptions(
                filePath:=If(scriptPathOpt, ""),
                references:=ImmutableArray.CreateRange(resolvedReferences),
                namespaces:=CommandLineHelpers.GetImports(arguments),
                metadataResolver:=metadataResolver,
                sourceResolver:=sourceResolver,
                emitDebugInformation:=emitDebugInformation,
                fileEncoding:=Nothing,
                optimizationLevel:=arguments.CompilationOptions.OptimizationLevel,
                allowUnsafe:=True,
                checkOverflow:=False,
                warningLevel:=4,
                parseOptions:=arguments.ParseOptions)
        End Function

        ''' <summary>
        ''' The CLI is only an adapter here: request construction, output of the diagnostics it owns, and
        ''' exit-code translation. The execution semantics live in <see cref="ScriptRunnerCore"/>.
        ''' </summary>
        Private Async Function ExecuteScriptAsync(options As ScriptOptions, code As SourceText, errorLogger As ErrorLogger, cancellationToken As CancellationToken) As Task(Of Integer)
            Dim globals = New CommandLineScriptGlobals(_console.Out, _objectFormatter)
            globals.Args.AddRange(_compiler.Arguments.ScriptArguments)

            Dim request As New ScriptRunRequest With {
                .Compiler = _scriptCompiler,
                .Source = code,
                .FilePath = options.FilePath,
                .ScriptOptions = options,
                .Globals = globals,
                .AssemblyLoader = _assemblyLoader,
                .CompileOnly = _compiler.Arguments.Check,
                .BaseDirectory = _compiler.Arguments.BaseDirectory,
                .RestoreCoordinator = _restoreCoordinator,
                .RestoreDiagnosticsReporter =
                    Function(diagnostics) _compiler.ReportDiagnostics(diagnostics, _console.Error, errorLogger, compilation:=Nothing)
            }

            Dim outcome = Await ScriptRunnerCore.Instance.RunAsync(request, cancellationToken).ConfigureAwait(False)

            If Not outcome.Diagnostics.IsDefaultOrEmpty AndAlso
               _compiler.ReportDiagnostics(outcome.Diagnostics, _console.Error, errorLogger, compilation:=Nothing) Then
                ' /check errors and run-time compilation errors both exit Failed; successful /check runs
                ' (warnings only) fall through to the core-provided 0.
                Return CommonCompiler.Failed
            End If

            If outcome.Exception IsNot Nothing Then
                DisplayException(outcome.Exception)
            End If

            Return outcome.ExitCode
        End Function

        Private Sub DisplayException(e As Exception)
            Try
                _console.SetForegroundColor(ConsoleColor.Red)

                Dim fileLoadException = TryCast(e, FileLoadException)
                If fileLoadException IsNot Nothing AndAlso TypeOf fileLoadException.InnerException Is InteractiveAssemblyLoaderException Then
                    _console.Error.WriteLine(fileLoadException.InnerException.Message)
                Else
                    _console.Error.Write(_objectFormatter.FormatException(e))
                End If
            Finally
                _console.ResetColor()
            End Try
        End Sub

    End Class

End Namespace
