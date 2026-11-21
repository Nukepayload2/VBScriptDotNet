' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.IO
Imports System.Runtime.ExceptionServices
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' The single place where host options become compiler options and where compiler results become the
    ''' host-owned <see cref="VBScriptResult"/>. The static facade and the DI runner call these
    ''' helpers instead of each assembling their own pipeline, so options and diagnostics cannot drift between the
    ''' two entries.
    ''' </summary>
    Friend NotInheritable Class VBScriptHostInternals

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Copies the caller's options and applies the vbi default set to the copy, so the static facade never
        ''' mutates an object the caller owns and repeated calls stay idempotent.
        ''' </summary>
        Friend Shared Function CreateEffectiveOptions(options As VBScriptOptions) As VBScriptOptions
            Dim source = If(options, New VBScriptOptions())
            Dim clone As New VBScriptOptions() With {
                .WorkingDirectory = source.WorkingDirectory,
                .FileEncoding = source.FileEncoding,
                .EmitDebugInformation = source.EmitDebugInformation,
                .OptimizationLevel = source.OptimizationLevel,
                .Globals = source.Globals,
                .GlobalsType = source.GlobalsType,
                .UseDesktopReferences = source.UseDesktopReferences
            }

            If source.References IsNot Nothing Then
                For Each item In source.References
                    clone.References.Add(item)
                Next
            End If

            If source.[Imports] IsNot Nothing Then
                For Each item In source.[Imports]
                    clone.[Imports].Add(item)
                Next
            End If

            If source.ScriptArguments IsNot Nothing Then
                clone.ScriptArguments = New List(Of String)(source.ScriptArguments)
            End If

            ' The restore snapshot is shared, not copied: the default restore service records the SDK version it
            ' observed on this instance and the host must see it through the result/options it already holds.
            clone.SetRestoreContext(source.RestoreContext)

            VBScriptDefaults.ApplyDefaults(clone)
            Return clone
        End Function

        ''' <summary>Base directory for <c>#Load</c> and relative references; vbi's behaviour when unset.</summary>
        Friend Shared Function ResolveWorkingDirectory(options As VBScriptOptions) As String
            If options IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(options.WorkingDirectory) Then
                Return options.WorkingDirectory
            End If

            Return Directory.GetCurrentDirectory()
        End Function

        Friend Shared Function CreateSourceText(code As String, options As VBScriptOptions) As SourceText
            Dim encoding As System.Text.Encoding = Nothing
            If options IsNot Nothing Then
                encoding = options.FileEncoding
            End If

            Return SourceText.From(If(code, String.Empty), encoding)
        End Function

        ''' <summary>
        ''' Builds the one request shape both entries use. <paramref name="filePath"/> may be
        ''' <see langword="Nothing"/> for an in-memory snippet.
        ''' </summary>
        Friend Shared Function CreateRunRequest(
                options As VBScriptOptions,
                source As SourceText,
                filePath As String,
                globals As Object,
                assemblyLoader As InteractiveAssemblyLoader,
                compileOnly As Boolean,
                Optional restoreCoordinator As IScriptRestoreCoordinator = Nothing,
                Optional packageResolver As ScriptNuGetPackageResolver = Nothing) As ScriptRunRequest

            Dim baseDirectory = ResolveWorkingDirectory(options)

            Return New ScriptRunRequest With {
                .Compiler = VisualBasicScriptCompiler.Instance,
                .Source = source,
                .FilePath = filePath,
                .ScriptOptions = CreateScriptOptions(options, filePath, baseDirectory, packageResolver),
                .Globals = globals,
                .AssemblyLoader = assemblyLoader,
                .CompileOnly = compileOnly,
                .BaseDirectory = baseDirectory,
                .RestoreCoordinator = restoreCoordinator
            }
        End Function

        ''' <summary>
        ''' The options path moved out of the CLI: same resolvers vbi uses
        ''' (<c>RuntimeMetadataReferenceResolver.CreateCurrentPlatformResolver</c>), same script-dialect parse
        ''' defaults (the VB script compiler falls back to its own script parse options when none are passed).
        ''' The source resolver is the public <see cref="SourceFileResolver"/> rather than the CLI's internal
        ''' logging subclass: with no touched-file logger the two resolve <c>#Load</c> identically, and the base
        ''' directory is fixed here, so the session's re-base step has nothing to do for this request shape.
        ''' </summary>
        Friend Shared Function CreateScriptOptions(options As VBScriptOptions, filePath As String, baseDirectory As String, Optional packageResolver As ScriptNuGetPackageResolver = Nothing) As ScriptOptions
            Dim metadataResolver = ScriptHostResolvers.CreateMetadataResolver(baseDirectory, packageResolver)

            Dim sourceResolver As SourceReferenceResolver = New SourceFileResolver(
                ImmutableArray(Of String).Empty,
                baseDirectory,
                ImmutableArray(Of KeyValuePair(Of String, String)).Empty)

            Return ScriptOptions.Default.
                WithFilePath(If(filePath, String.Empty)).
                WithReferences(RemoveBlankEntries(options.References)).
                WithImports(RemoveBlankEntries(options.[Imports])).
                WithMetadataResolver(metadataResolver).
                WithSourceResolver(sourceResolver).
                WithEmitDebugInformation(options.EmitDebugInformation).
                WithOptimizationLevel(ToCompilerOptimizationLevel(options.OptimizationLevel)).
                WithFileEncoding(options.FileEncoding)
        End Function

        ''' <summary>
        ''' The compiler never sees blank <c>/imports:</c> or <c>/r:</c> entries: the options validator rejects them in
        ''' the DI path, and the container-less static facade drops them here instead of letting the compiler throw
        ''' an <see cref="ArgumentException"/> for an empty namespace. The options object itself keeps them, so the
        ''' validator can still report them.
        ''' </summary>
        Private Shared Function RemoveBlankEntries(items As Collections.Generic.IEnumerable(Of String)) As ImmutableArray(Of String)
            If items Is Nothing Then
                Return ImmutableArray(Of String).Empty
            End If

            Dim builder = ImmutableArray.CreateBuilder(Of String)()
            For Each item In items
                If Not String.IsNullOrWhiteSpace(item) Then
                    builder.Add(item)
                End If
            Next

            Return builder.ToImmutable()
        End Function

        ''' <summary>
        ''' Resolves the globals instance for one execution: explicit instance, then
        ''' <see cref="VBScriptOptions.Globals"/>, then <see cref="VBScriptOptions.GlobalsType"/>, then a default
        ''' <see cref="VBScriptGlobals"/> bound to <paramref name="defaultOutput"/>. A bad
        ''' <see cref="VBScriptOptions.GlobalsType"/> is host misuse and throws.
        ''' </summary>
        Friend Shared Function ResolveGlobals(options As VBScriptOptions, globals As Object, defaultOutput As IScriptOutput) As Object
            Dim resolved As Object

            If globals IsNot Nothing Then
                resolved = globals
            ElseIf options IsNot Nothing AndAlso options.Globals IsNot Nothing Then
                resolved = options.Globals
            ElseIf options IsNot Nothing AndAlso options.GlobalsType IsNot Nothing Then
                resolved = CreateGlobalsInstance(options.GlobalsType, defaultOutput)
            Else
                resolved = New VBScriptGlobals(defaultOutput)
            End If

            Dim baseGlobals = TryCast(resolved, VBScriptGlobals)
            If baseGlobals IsNot Nothing AndAlso options IsNot Nothing AndAlso options.ScriptArguments IsNot Nothing Then
                For Each argument In options.ScriptArguments
                    baseGlobals.Args.Add(argument)
                Next
            End If

            Return resolved
        End Function

        Private Shared Function CreateGlobalsInstance(globalsType As Type, defaultOutput As IScriptOutput) As Object
            If Not GetType(VBScriptGlobals).IsAssignableFrom(globalsType) Then
                Throw New VBScriptHostException(
                    "VBScriptOptions.GlobalsType must derive from " & GetType(VBScriptGlobals).FullName &
                    ", but '" & globalsType.FullName & "' does not. Derive the type from VBScriptGlobals, " &
                    "or set VBScriptOptions.Globals to an instance instead.")
            End If

            If globalsType.GetConstructor(New Type() {GetType(IScriptOutput)}) IsNot Nothing Then
                Return Activator.CreateInstance(globalsType, New Object() {defaultOutput})
            End If

            If globalsType.GetConstructor(Type.EmptyTypes) Is Nothing Then
                Throw New VBScriptHostException(
                    "VBScriptOptions.GlobalsType '" & globalsType.FullName & "' has no public parameterless constructor " &
                    "and no public constructor taking IScriptOutput. Add one of them, or set VBScriptOptions.Globals " &
                    "to an instance instead.")
            End If

            Return Activator.CreateInstance(globalsType)
        End Function

        Friend Shared Function MapRunOutcome(outcome As ScriptRunOutcome, output As String) As VBScriptResult
            Return New VBScriptResult(
                outcome.ExitCode,
                outcome.ReturnValue,
                MergeDiagnostics(MapDiagnostics(outcome.Diagnostics), MapDiagnostics(outcome.RestoreDiagnostics)),
                output,
                outcome.Exception)
        End Function

        ''' <summary>
        ''' Joins the restore diagnostics to the compile/run diagnostics without counting or printing a diagnostic
        ''' twice. The core can hand the same restore diagnostic back through both channels (when it blocked the
        ''' submission, it is both "the diagnostics to report" and "the pre-scan diagnostics"), so duplicates are
        ''' removed by identity, severity, message and position.
        ''' </summary>
        Friend Shared Function MergeDiagnostics(primary As ImmutableArray(Of VBScriptDiagnostic), secondary As ImmutableArray(Of VBScriptDiagnostic)) As ImmutableArray(Of VBScriptDiagnostic)
            If secondary.IsDefaultOrEmpty Then
                Return If(primary.IsDefault, ImmutableArray(Of VBScriptDiagnostic).Empty, primary)
            End If

            If primary.IsDefaultOrEmpty Then
                Return secondary
            End If

            Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
            Dim builder = ImmutableArray.CreateBuilder(Of VBScriptDiagnostic)(primary.Length + secondary.Length)

            For Each diagnostic In primary
                If seen.Add(GetDiagnosticKey(diagnostic)) Then
                    builder.Add(diagnostic)
                End If
            Next

            For Each diagnostic In secondary
                If seen.Add(GetDiagnosticKey(diagnostic)) Then
                    builder.Add(diagnostic)
                End If
            Next

            Return builder.ToImmutable()
        End Function

        Private Shared Function GetDiagnosticKey(diagnostic As VBScriptDiagnostic) As String
            If diagnostic Is Nothing Then
                Return String.Empty
            End If

            Return diagnostic.Id & ChrW(0) & CInt(diagnostic.Severity).ToString(Globalization.CultureInfo.InvariantCulture) &
                ChrW(0) & diagnostic.Message & ChrW(0) & If(diagnostic.FilePath, String.Empty) &
                ChrW(0) & diagnostic.Line.ToString(Globalization.CultureInfo.InvariantCulture) &
                ChrW(0) & diagnostic.Column.ToString(Globalization.CultureInfo.InvariantCulture)
        End Function

        ''' <summary>
        ''' The single implementation of the typed evaluation channel shared by the static facade and the DI runner:
        ''' an object-returning session (the same shape the vbi REPL uses, because in this dialect a bare expression
        ''' only produces a return value in an object-returning submission), compile errors as
        ''' <see cref="VBScriptScriptException"/>, a run-time exception rethrown untouched, and a conversion failure as
        ''' <see cref="VBScriptHostException"/>. <paramref name="assemblyLoader"/> is owned by the caller.
        ''' </summary>
        Friend Shared Async Function EvaluateAsync(Of T)(
                options As VBScriptOptions,
                source As SourceText,
                globals As Object,
                assemblyLoader As InteractiveAssemblyLoader,
                cancellationToken As CancellationToken,
                Optional restoreCoordinator As IScriptRestoreCoordinator = Nothing,
                Optional packageResolver As ScriptNuGetPackageResolver = Nothing) As Task(Of T)

            Dim request = CreateRunRequest(options, source, filePath:=Nothing, globals:=globals, assemblyLoader:=assemblyLoader, compileOnly:=False, restoreCoordinator:=restoreCoordinator, packageResolver:=packageResolver)
            Dim session = ScriptRunnerCore.Instance.CreateSession(Of Object)(request)
            Try
                Dim submission = Await session.SubmitAsync(source, cancellationToken).ConfigureAwait(False)

                If submission.RestoreBlocked OrElse HasErrors(submission.Diagnostics) Then
                    Throw New VBScriptScriptException(MapDiagnostics(submission.Diagnostics))
                End If

                ' A run-time exception is the script's failure, not host misuse: rethrow it untouched so the
                ' HResult, message and original stack survive (ExceptionDispatchInfo keeps the stack).
                If submission.Exception IsNot Nothing Then
                    ExceptionDispatchInfo.Capture(submission.Exception).Throw()
                End If

                ' No return value: a value-returning entry gives default(T), matching the CLR's default for an
                ' omitted Return.
                If submission.ReturnValue Is Nothing AndAlso GetType(T).IsValueType Then
                    Return Nothing
                End If

                Try
                    Return CType(submission.ReturnValue, T)
                Catch conversionFailure As Exception
                    Throw New VBScriptHostException(
                        "The script returned " & DescribeValue(submission.ReturnValue) &
                        ", which cannot be converted to " & GetType(T).FullName &
                        ". Return a value assignable to the requested type, or call RunAsync for the untyped result.",
                        conversionFailure)
                End Try
            Finally
                session.Dispose()
            End Try
        End Function

        Private Shared Function DescribeValue(value As Object) As String
            If value Is Nothing Then
                Return "Nothing"
            End If

            Return "a value of type " & value.GetType().FullName
        End Function

        Friend Shared Function MapDiagnostics(diagnostics As ImmutableArray(Of Diagnostic)) As ImmutableArray(Of VBScriptDiagnostic)
            If diagnostics.IsDefaultOrEmpty Then
                Return ImmutableArray(Of VBScriptDiagnostic).Empty
            End If

            Dim builder = ImmutableArray.CreateBuilder(Of VBScriptDiagnostic)(diagnostics.Length)
            For Each diagnostic In diagnostics
                builder.Add(MapDiagnostic(diagnostic))
            Next

            Return builder.ToImmutable()
        End Function

        ''' <summary>Roslyn diagnostic to host DTO: id, mapped severity, message, file, 1-based line and column.</summary>
        Friend Shared Function MapDiagnostic(diagnostic As Diagnostic) As VBScriptDiagnostic
            Dim severity = MapSeverity(diagnostic.Severity)
            If Not diagnostic.Location.IsInSource Then
                Return New VBScriptDiagnostic(diagnostic.Id, severity, diagnostic.GetMessage(), Nothing, 0, 0)
            End If

            Dim lineSpan = diagnostic.Location.GetLineSpan()
            Dim path = lineSpan.Path
            If String.IsNullOrEmpty(path) AndAlso diagnostic.Location.SourceTree IsNot Nothing Then
                path = diagnostic.Location.SourceTree.FilePath
            End If

            Return New VBScriptDiagnostic(
                diagnostic.Id,
                severity,
                diagnostic.GetMessage(),
                If(String.IsNullOrEmpty(path), Nothing, path),
                lineSpan.StartLinePosition.Line + 1,
                lineSpan.StartLinePosition.Character + 1)
        End Function

        Friend Shared Function HasErrors(diagnostics As ImmutableArray(Of Diagnostic)) As Boolean
            If diagnostics.IsDefaultOrEmpty Then
                Return False
            End If

            For Each diagnostic In diagnostics
                If diagnostic.Severity = DiagnosticSeverity.Error Then
                    Return True
                End If
            Next

            Return False
        End Function

        Private Shared Function MapSeverity(severity As DiagnosticSeverity) As VBScriptDiagnosticSeverity
            Select Case severity
                Case DiagnosticSeverity.Error
                    Return VBScriptDiagnosticSeverity.Error
                Case DiagnosticSeverity.Warning
                    Return VBScriptDiagnosticSeverity.Warning
                Case DiagnosticSeverity.Info
                    Return VBScriptDiagnosticSeverity.Info
                Case Else
                    Return VBScriptDiagnosticSeverity.Hidden
            End Select
        End Function

        Friend Shared Function ToCompilerOptimizationLevel(level As VBScriptOptimizationLevel) As OptimizationLevel
            Select Case level
                Case VBScriptOptimizationLevel.Release
                    Return OptimizationLevel.Release
                Case Else
                    Return OptimizationLevel.Debug
            End Select
        End Function

    End Class

End Namespace
