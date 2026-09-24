' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' U6 (design-detailed.md section U6): command line Args and the source/reference search paths.
' Twelve cells, one Fact each. This file is separate from CommandLineRunnerTests.vb because that file's
' CreateRunner creates a temp directory per test (CommandLineRunnerTests.vb CreateIsolatedTempDirectory) and
' the U6 cells must not create directories or write files (test-plan section 2.1).
'
' C# baseline: {{Roslyn}}\src\Scripting\CSharpTest\CommandLineRunnerTests.cs. The method names below are the
' greppable anchors; the line numbers in the plan drift:
'   Args_Interactive1, Args_Interactive2, Args_InteractiveWithScript1, Args_Script1, Args_Script2,
'   Args_Script3, Args_Script4, Args_Script5, SourceSearchPaths1, ReferenceSearchPaths1,
'   SourceSearchPaths_Change1, ReferenceSearchPaths_Change1.
'
' Production anchors:
'   Scripting\Core\Hosting\CommandLine\CommandLineScriptGlobals.cs - `Args` member (script-file globals type)
'   Scripting\Core\Hosting\InteractiveScriptGlobals.cs - `Args`, `ReferencePaths`, `SourcePaths`
'   Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs - RunScriptAsync fills globals.Args from
'     CommandLineArguments.ScriptArguments and passes globals.GetType() as the script globals type;
'     RunInteractiveLoopAsync does the same for the REPL; UpdateOptions re-derives both resolvers from the
'     globals collections after every successful submission
'   Scripting\VisualBasic\VisualBasicScriptCompiler.vb - script.GlobalsType reaches
'     VisualBasicCompilation.CreateScriptCompilation
'   Compilers\VisualBasic\Portable\CommandLine\VisualBasicCommandLineParser.vb - the script switch table
'   Compilers\Core\Portable\CommandLine\CommandLineParser.cs - FlattenArgs, which decides what is an option,
'     what is a source file and what is a script argument
'
' Two instruments differ from the C# cells, and both are forced by the no-side-effect rule (test-plan
' section 2 / 2.1.1): every C# cell in the Args family builds a temp .csx/.rsp fixture, which no new case
' here may do. So instead of a temp script file:
'   1. VisualBasicCommandLineParser.Script.Parse - the same parser the vbi host runs (CommonCompiler.cs
'      hands it BuildPaths.WorkingDirectory as the base directory). It pins the argument vector exactly
'      (SourceFiles / ScriptArguments) with no file involved.
'   2. CommandLineScriptGlobals + VisualBasicScript.Create(..., globalsType) + RunAsync - the same wiring
'      CommandLineRunner.RunScriptAsync uses (globals.Args gets the ScriptArguments, the globals type goes
'      to the script), which is the only path that puts parsed command line arguments in front of a script.
' Cells 1, 2, 7, 9, 10, 11 and 12 run the real host end to end through CommandLineRunner over an in-memory
' console, so the wiring in CommandLineRunner.cs is exercised for real there. Cells 4 and 5 stop one step
' short of that: they replicate the globals wiring RunScriptAsync performs (the globals type is handed to the
' script, globals.Args is filled from ScriptArguments) instead of calling RunScriptAsync, which only ever runs
' a script read from a file (CommandLineRunner.cs, RunAsync) and no case here may create one.

Imports System
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting
Imports My.Resources
Imports Xunit

Public Class ScriptModeArgsTests

#Region "in memory host"

    ''' <summary>The build output directory. It already exists, so it is the only directory these cases use.</summary>
    Private Shared ReadOnly BaseDirectory As String =
        AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)

    ''' <summary>
    ''' The directory the build output directory lives in. Used as <c>BuildPaths.WorkingDirectory</c> whenever
    ''' a case has to show that a search path - and not the base directory - is what resolved a file.
    ''' </summary>
    Private Shared ReadOnly ParentOfBaseDirectory As String = Path.GetDirectoryName(BaseDirectory)

    ''' <summary>The compiler's own response file, copied next to the test assembly by the project file.</summary>
    Private Shared ReadOnly ResponseFile As String = Path.Combine(BaseDirectory, "vbi.rsp")

    ''' <summary>The script host binary. Used as the reference a search path has to resolve.</summary>
    Private Shared ReadOnly HostAssembly As String = Path.Combine(BaseDirectory, "vbi.dll")

    Private Shared ReadOnly InteractiveCompilerVersion As String =
        GetType(VisualBasicInteractiveCompiler).Assembly.
            GetCustomAttribute(Of AssemblyInformationalVersionAttribute).InformationalVersion

    Private Shared ReadOnly RoslynVersion As String =
        FormatVersionWithoutRevision(GetType(VisualBasicCompiler).Assembly.GetName.Version)

    ''' <summary>
    ''' The banner and the help prompt the REPL prints before the first submission, formatted from the same
    ''' resources CommandLineRunnerTests.vb does (its copy of this constant is private). Built from the
    ''' resources and not spelled out because LogoLine2 is localized. Newlines are the environment's, because
    ''' the host writes this text through TextWriter.WriteLine - the same choice CommandLineRunnerTests.vb:25-35
    ''' and NoNuGetZeroRegressionTests.vb:51-56 make for their copies.
    ''' </summary>
    Private Shared ReadOnly LogoAndHelpPrompt As String =
        String.Format(VBScriptingResources.LogoLine1, InteractiveCompilerVersion) & Environment.NewLine &
        String.Format(VBScriptingResources.LogoLine2, RoslynVersion) & Environment.NewLine &
        VBScriptingResources.LogoLine3 & Environment.NewLine &
        VBScriptingResources.LogoLine4 & Environment.NewLine & Environment.NewLine & ScriptingResources.HelpPrompt

    Private Shared Function FormatVersionWithoutRevision(version As Version) As String
        Return New Version(version.Major, version.Minor, version.Build).ToString()
    End Function

    Private Shared ReadOnly ReplMarker As String = "? 13 * 29"

    Private Shared ReadOnly ReplMarkerResult As String = "377"

    ''' <summary>
    ''' Builds the interactive host over an in memory console. <c>tempDir</c> is the existing build directory,
    ''' so nothing is created; <c>workingDir</c> is the base directory the parser and the resolvers use.
    ''' </summary>
    Private Shared Function CreateRunner(args As String(), input As String, Optional workingDirectory As String = Nothing) As CommandLineRunner
        Dim buildPaths = New BuildPaths(
            clientDir:=BaseDirectory,
            workingDir:=If(workingDirectory, BaseDirectory),
            sdkDir:=RuntimeMetadataReferenceResolver.GetDesktopFrameworkDirectory(),
            tempDir:=BaseDirectory)
        Dim compiler = New VisualBasicInteractiveCompiler(
            ResponseFile, buildPaths, args, New NotImplementedAnalyzerLoader())

        Return New CommandLineRunner(New TestConsoleIO(input), compiler, VisualBasicScriptCompiler.Instance, VisualBasicObjectFormatter.Instance)
    End Function

    ''' <summary>
    ''' Parses an argument vector with the script mode parser. <paramref name="baseDirectory"/> is passed
    ''' explicitly: <c>Nothing</c> isolates resolution to the search paths under test, so that a hit can only
    ''' come from the search path and not from the base directory.
    ''' </summary>
    Private Shared Function ParseScriptArguments(args As String(), Optional baseDirectory As String = Nothing) As VisualBasicCommandLineArguments
        Return VisualBasicCommandLineParser.Script.Parse(
            args, baseDirectory, RuntimeMetadataReferenceResolver.GetDesktopFrameworkDirectory())
    End Function

    ''' <summary>
    ''' Runs a script against <see cref="CommandLineScriptGlobals"/> whose <c>Args</c> holds the given values.
    ''' This replicates the globals wiring of CommandLineRunner.RunScriptAsync - the globals type handed to the
    ''' script, <c>Args</c> filled from CommandLineArguments.ScriptArguments - but it is not that method: the
    ''' <see cref="ScriptOptions"/> and the submission are built here, and RunScriptAsync itself is reachable
    ''' only with a script read from a file, which no case here may create. Returns the submission's value.
    ''' </summary>
    Private Shared Function RunWithScriptArguments(args As String(), source As String) As Object
        Dim globals = New CommandLineScriptGlobals(New TestConsoleIO("").Out, VisualBasicObjectFormatter.Instance)
        For Each argument In args
            globals.Args.Add(argument)
        Next

        Dim script = VisualBasicScript.Create(source, ScriptModeConformance.DefaultOptions, GetType(CommandLineScriptGlobals))
        Return script.RunAsync(globals).GetAwaiter().GetResult().ReturnValue
    End Function

    Private Shared Function JoinedLines(ParamArray parts As String()) As String
        Return String.Join(vbCrLf, parts)
    End Function

    ''' <summary>One REPL output line carrying a value, as the transcript shows it.</summary>
    Private Shared Function OutputLine(value As String) As String
        Return vbCrLf & value & vbCrLf
    End Function

    ''' <summary>The REPL formats a submission's value with ObjectFormatter, which quotes strings.</summary>
    Private Shared Function QuotedOutputLine(value As String) As String
        Return OutputLine("""" & value & """")
    End Function

    ''' <summary>
    ''' Transcript assertions carry the whole transcript in the failure message: xunit truncates the subject of
    ''' its own Assert.Contains to a couple of dozen characters, which hides what the session actually printed.
    ''' </summary>
    Private Shared Sub AssertInTranscript(expected As String, transcript As String)
        Assert.True(transcript.Contains(expected), "missing '" & expected & "' in:" & vbCrLf & transcript)
    End Sub

    Private Shared Sub AssertNotInTranscript(unexpected As String, transcript As String)
        Assert.True(Not transcript.Contains(unexpected), "unexpected '" & unexpected & "' in:" & vbCrLf & transcript)
    End Sub

    Private Shared Function FileNotFoundMessage(fileName As String) As String
        Return String.Format(VBResources.ERR_FileNotFound, fileName)
    End Function

    Private Shared Function LibraryNotFoundMessage(reference As String) As String
        Return String.Format(VBResources.ERR_LibNotFound, reference)
    End Function

#End Region

#Region "cells 1-8: Args"

    ''' <summary>
    ''' Cell 1 of 12 - C# `Args_Interactive1`: <c>/i</c> alone starts a session whose globals carry no script
    ''' arguments, and the session runs. `Args` is InteractiveScriptGlobals.Args
    ''' (Scripting\Core\Hosting\InteractiveScriptGlobals.cs, the `Args` member); the runner fills it from
    ''' CommandLineArguments.ScriptArguments and hands the globals type to the submission
    ''' (CommandLineRunner.cs, RunInteractiveLoopAsync).
    ''' Discrimination: the pinned value is the argument list itself ("0[]"), so the case fails if `/i` is
    ''' misread as a script argument, if the globals type is not passed to the submission, or if the
    ''' collection is pre-populated. The comparison is the <em>whitespace-normalized</em> whole transcript,
    ''' banner included (the shape the C# cell asserts). The normalization is
    ''' AssertEx.AssertEqualToleratingWhitespaceDifferences (Helpers\AssertEx.vb, NormalizeWhitespace): both
    ''' sides are split on CR and on LF, every line is trimmed, and empty lines are dropped, so the surviving
    ''' non-empty lines are what gets compared. That means an added or changed non-empty line - a diagnostic,
    ''' a second prompt, a different argument list - fails the case, while an added blank line or a line's
    ''' leading/trailing spaces do not. The raw strings are not equal as they stand (367 vs 364 characters,
    ''' first difference at index 331: the expected side carries a blank line the host does not print), so
    ''' normalization is what the case actually rests on.
    ''' </summary>
    <Fact>
    Public Sub ArgsInteractiveMode_StartsSessionWithNoScriptArguments()
        Dim input = "? ""count="" & Args.Count & ""["" & String.Join(""|"", Args) & ""]"""
        Dim runner = CreateRunner({"/i"}, JoinedLines(input, ReplMarker))

        Assert.Equal(0, runner.RunInteractive())

        ' The whitespace-normalized whole transcript, banner included - the shape the C# cell asserts.
        ' AssertEqualToleratingWhitespaceDifferences (Helpers\AssertEx.vb, NormalizeWhitespace) splits both
        ' sides on CR and LF, trims each line and drops empty lines before comparing. `Transcript` assertions
        ' elsewhere in this file only test for a substring, so a session that printed an extra non-empty line
        ' (a diagnostic, a second prompt, a different argument list) would still pass there; here it cannot.
        ' A blank line or a line's leading/trailing spaces are absorbed by the normalization - and the raw
        ' strings are already unequal today (367 vs 364 characters, first difference at index 331).
        AssertEx.AssertEqualToleratingWhitespaceDifferences(
            LogoAndHelpPrompt & vbCrLf &
            "> " & input & vbCrLf &
            QuotedOutputLine("count=0[]") &
            "> " & ReplMarker & vbCrLf &
            OutputLine(ReplMarkerResult) &
            ">",
            runner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' Cell 2 of 12 - C# `Args_Interactive2`, the `--` cell of interactive mode
    ''' (`args: ["/u:System", "/i", "--", "@arg1", "/arg2", "-arg3", "--arg4"]`). VB spells the import switch
    ''' `/imports`, so this case drives the same *argument vector shape* with `/i` only - the cell is about the
    ''' shape, not the import.
    ''' What the shared flattener guarantees (CommandLineParser.cs, FlattenArgs): a token after `--` is never
    ''' expanded as a response file, and from the first source file on every token is a script argument
    ''' (`parsingScriptArgs`). So with the source-file slot filled, `/arg2`, `-arg3` and `--arg4` reach the
    ''' script as arguments when they follow that slot.
    ''' Discrimination: `ScriptArguments` is pinned exactly, and the source file is pinned as the token in that
    ''' slot. A vector that lost the source-file detection would put `/arg2` in a different collection or reject
    ''' it as a switch.
    ''' VB's main switch loop now carries the same `optionsEnded` gate C# has (issue 23, aligned to
    ''' CSharpCommandLineParser.cs:169 and :174 via VisualBasicCommandLineParser.vb): an option-shaped token that
    ''' follows `--` before any source file is taken as the source file rather than rejected as BC2007, exactly as
    ''' C# does. The second parse below pins that aligned behavior; the first parse is its control. The full
    ''' separator contract (bare `@` tokens, the exact "--" boundary, and vbc immunity) is locked by
    ''' ScriptOptionsEndedTests.
    ''' </summary>
    <Fact>
    Public Sub DoubleDashInteractiveMode_SeparatesTheSourceFileFromTheScriptArguments()
        Dim parsed = ParseScriptArguments({"/i", "--", "script.vbx", "/arg2", "-arg3", "--arg4"}, BaseDirectory)

        Assert.Equal({"/arg2", "-arg3", "--arg4"}, parsed.ScriptArguments.ToArray())
        Assert.Contains(parsed.SourceFiles, Function(f) Path.GetFileName(f.Path) = "script.vbx")
        Assert.DoesNotContain(parsed.Errors, Function(d) d.Id = "BC2007")

        ' After the optionsEnded gate landed (issue 23), VB classifies the same way C# does: an option-shaped
        ' token that follows "--" before any source file is no longer read as a switch (no BC2007) - it becomes
        ' the source file, and the token after it is a script argument. The first parse above is the control
        ' (same token in the script-argument position: not an option, no BC2007). The exact separator contract is
        ' locked in ScriptOptionsEndedTests (DashDash_NextSlashSwitchIsSourceFile and its siblings).
        Dim optionShapedSlot = ParseScriptArguments({"/i", "--", "/arg2", "script.vbx"}, BaseDirectory)
        Assert.DoesNotContain(optionShapedSlot.Errors, Function(d) d.Id = "BC2007")
        Assert.Contains(optionShapedSlot.SourceFiles, Function(f) Path.GetFileName(f.Path) = "arg2")
        Assert.Equal({"script.vbx"}, optionShapedSlot.ScriptArguments.ToArray())

        ' End to end the run cannot start: the source file does not exist, so the marker submission is never
        ' echoed (the REPL is never reached).
        Dim runner = CreateRunner({"/i", "--", "u6-missing.vbx", "-arg3"}, JoinedLines("? 1", ReplMarker))

        Assert.Equal(1, runner.RunInteractive())
        AssertNotInTranscript(OutputLine(ReplMarkerResult), runner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' Cell 3 of 12 - C# `Args_InteractiveWithScript1`: a response file in script mode.
    ''' The response file used here is the compiler's own (vbi.rsp, copied next to the test assembly by the
    ''' project file), because no case may write an .rsp fixture. What the cell pins is the same contract:
    ''' `@file` is expanded before source file and script argument detection, expansion does not swallow the
    ''' tokens after it, and past the first source file every token is a script argument - the `--` included
    ''' (which is exactly the argument list the C# cell prints).
    ''' Discrimination: the response file's `/imports:` lines have to land on the parsed compilation options, so
    ''' the case fails if `@file` were only skipped rather than read; and the response file path itself must not
    ''' appear among the source files.
    ''' </summary>
    <Fact>
    Public Sub ResponseFileInScriptMode_IsExpandedAndTrailingTokensBecomeScriptArguments()
        Dim parsed = ParseScriptArguments({"/i", "@" & ResponseFile, "main.vbx", "arg1", "--", "/arg7"}, BaseDirectory)

        Assert.Equal(1, parsed.SourceFiles.Length)
        Assert.Equal(Path.Combine(BaseDirectory, "main.vbx"), parsed.SourceFiles(0).Path)
        Assert.Equal({"arg1", "--", "/arg7"}, parsed.ScriptArguments.ToArray())
        Assert.Contains(parsed.CompilationOptions.GlobalImports, Function(imported) imported.Clause.ToString() = "Microsoft.VisualBasic")
    End Sub

    ''' <summary>
    ''' Cell 4 of 12 - C# `Args_Script1` (`args: [script.Path, "arg1", "arg2", "arg3"]`): the file named first is
    ''' the script and every token after it is a script argument, which the script then reads through `Args`.
    ''' Discrimination: the parsed vector is pinned both in shape (one source file, script kind, three script
    ''' arguments) and in effect - the arguments reach a real submission through the same globals wiring
    ''' RunScriptAsync applies (globals.Args filled from ScriptArguments, the globals type handed to the
    ''' script), replicated rather than called (see RunWithScriptArguments) - and the script has to hand back
    ''' "arg1|arg2|arg3". A parser that dropped, reordered or option-ized a trailing token shows up as a
    ''' different joined value or as a parse error.
    ''' </summary>
    <Fact>
    Public Sub ScriptFileWithTrailingArguments_ParsesThemAsScriptArguments()
        Dim parsed = ParseScriptArguments({"main.vbx", "arg1", "arg2", "arg3"}, BaseDirectory)

        Assert.Equal(1, parsed.SourceFiles.Length)
        Assert.True(parsed.SourceFiles(0).IsScript)
        Assert.Equal(Path.Combine(BaseDirectory, "main.vbx"), parsed.SourceFiles(0).Path)
        Assert.Equal({"arg1", "arg2", "arg3"}, parsed.ScriptArguments.ToArray())

        Assert.Equal("arg1|arg2|arg3", RunWithScriptArguments(parsed.ScriptArguments.ToArray(), "String.Join(""|"", Args)"))
    End Sub

    ''' <summary>
    ''' Cell 5 of 12 - C# `Args_Script2` (`args: [script.Path, "@arg1", "@arg2", "@arg3"]`): once the script file
    ''' is seen, an `@`-prefixed token is an argument, never a response file.
    ''' Discrimination: if the `@` branch of FlattenArgs ran for those tokens the parser would report a response
    ''' file diagnostic (and, for a token naming a file that exists, would expand it); the pinned argument list
    ''' and empty error list both fail in that case.
    ''' </summary>
    <Fact>
    Public Sub AtPrefixedTokensAfterTheScriptFile_AreScriptArgumentsNotResponseFiles()
        Dim parsed = ParseScriptArguments({"main.vbx", "@arg1", "@arg2", "@arg3"}, BaseDirectory)

        Assert.Equal({"@arg1", "@arg2", "@arg3"}, parsed.ScriptArguments.ToArray())
        Assert.Empty(parsed.Errors)

        Assert.Equal("@arg1|@arg2|@arg3", RunWithScriptArguments(parsed.ScriptArguments.ToArray(), "String.Join(""|"", Args)"))
    End Sub

    ''' <summary>
    ''' Cell 6 of 12 - C# `Args_Script3`: the `--` inside the tail is an argument of the script, not something
    ''' that ends the argument list. The vector below reproduces the argument list that C# cell prints
    ''' ("--", "@arg1", "/arg2", "-arg3", "--arg4", "/arg5", "--", "/arg7") without its temp fixtures.
    ''' Discrimination: a parser that consumed the tail `--` as an option terminator would return a shorter list
    ''' with the leading "--" missing, which the exact comparison catches.
    ''' </summary>
    <Fact>
    Public Sub DoubleDashAfterTheScriptFile_IsPreservedAsAScriptArgument()
        Dim parsed = ParseScriptArguments(
            {"@" & ResponseFile, "main.vbx", "--", "@arg1", "/arg2", "-arg3", "--arg4", "/arg5", "--", "/arg7"},
            BaseDirectory)

        Assert.Equal(1, parsed.SourceFiles.Length)
        Assert.Equal(Path.Combine(BaseDirectory, "main.vbx"), parsed.SourceFiles(0).Path)
        Assert.Equal({"--", "@arg1", "/arg2", "-arg3", "--arg4", "/arg5", "--", "/arg7"}, parsed.ScriptArguments.ToArray())
    End Sub

    ''' <summary>
    ''' Cell 7 of 12 - C# `Args_Script4` (`args: ["--", script.Path, "@arg1", "@arg2", "@arg3"]`): after the
    ''' leading `--` the script path is the source file and the `@`-prefixed tokens that follow it are script
    ''' arguments, not response files.
    ''' Discrimination: the token after `--` is pinned as a *source file of script kind* while the script
    ''' argument list stays exactly the three `@` tokens - i.e. the source file is the path, not `--`, and the
    ''' `@` tokens were not expanded. A parse that consumed `--` as the source file, or expanded `@arg1` as a
    ''' response file, changes one of the two collections.
    ''' Not asserted: the source file count and the error list, because the leading `--` reaches the VB main
    ''' switch loop as the standard input switch (see cell 2) - that outcome depends on
    ''' Console.IsInputRedirected and is reported rather than pinned.
    ''' Deviation from the C# vector: the C# cell's script file carries a leading `@` in its *directory entry*
    ''' name (an absolute path, so no command line token starts with `@`). A token that starts with `@` and
    ''' follows `--` cannot be driven here: FlattenArgs passes it on to the parser, whose main loop asserts that
    ''' no such token reaches it (VisualBasicCommandLineParser.vb, `Debug.Assert` at the head of the switch loop).
    ''' </summary>
    <Fact>
    Public Sub DoubleDashBeforeTheScriptPath_KeepsTheTrailingAtTokensAsArguments()
        Dim scriptPath = Path.Combine(BaseDirectory, "main.vbx")
        Dim parsed = ParseScriptArguments({"--", scriptPath, "@arg1", "@arg2", "@arg3"}, BaseDirectory)

        Assert.Equal({"@arg1", "@arg2", "@arg3"}, parsed.ScriptArguments.ToArray())
        Assert.Contains(parsed.SourceFiles, Function(f) f.Path = scriptPath AndAlso f.IsScript)

        ' End to end the run cannot start: the script path does not exist, so no marker result is reached.
        Dim runner = CreateRunner({"--", Path.Combine(BaseDirectory, "u6-missing-script.vbx"), "arg1"},
                                  JoinedLines("? 1", ReplMarker))

        Assert.Equal(1, runner.RunInteractive())
        AssertNotInTranscript(OutputLine(ReplMarkerResult), runner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' Cell 8 of 12 - C# `Args_Script5` (`args: ["--", "--", "-", "--", "-"]`, a script file literally named
    ''' `--`): the arguments the script sees are "-", "--", "-" - the two `--` tokens of the command line are
    ''' consumed before the argument list starts.
    ''' Discrimination: the pinned list is what makes the two leading `--` tokens observable. A parser that
    ''' passed either of them through as a script argument returns a different list (or a longer one).
    ''' Not asserted: which token ends up as the script file. In VB the main switch loop never sees a source file
    ''' named `--`: any token whose option name is "-" is read as the standard input switch
    ''' (VisualBasicCommandLineParser.vb, the script-only `Case "-"`), so that half of the C# cell - whose
    ''' parser only honours the literal "-" (CSharpCommandLineParser.cs, `case "-"`) - has no VB counterpart.
    ''' </summary>
    <Fact>
    Public Sub DoubleDashScriptNameAndDashArguments_ParseTheScriptArguments()
        Dim parsed = ParseScriptArguments({"--", "--", "-", "--", "-"}, BaseDirectory)

        Assert.Equal({"-", "--", "-"}, parsed.ScriptArguments.ToArray())
    End Sub

#End Region

#Region "cells 9-12: search paths"

    ''' <summary>
    ''' Cell 9 of 12 - C# `SourceSearchPaths1`: `/loadpath:` and `/loadpaths:` feed the source search path list,
    ''' and a `#Load` target that only the search path can supply is resolved.
    ''' Discrimination: the parsed list pins the `;` separated form, and the resolver pair is the real
    ''' discrimination - the same file name resolves to the exact path under the search path and does not
    ''' resolve at all without it (the base directory is left empty so nothing else can supply it). The
    ''' end-to-end pair closes the loop on the host: with the switch the loaded tree's own diagnostics are
    ''' anchored at the resolved file, without it the host reports the file as missing.
    ''' </summary>
    <Fact>
    Public Sub SourceSearchPaths_SwitchFormsReachTheSourceResolver()
        Dim first = BaseDirectory
        Dim second = Path.Combine(BaseDirectory, "u6-source-store-a")
        Dim third = Path.Combine(BaseDirectory, "u6-source-store-b")

        Dim parsed = ParseScriptArguments({"/loadpath:" & first, "/loadpaths:" & second & ";" & third})
        Assert.Equal({first, second, third}, parsed.SourcePaths.ToArray())

        Dim resolved = DirectCast(CommandLineRunner.GetSourceReferenceResolver(ParseScriptArguments({"/loadpath:" & first}), Nothing), SourceFileResolver)
        Assert.Equal({first}, resolved.SearchPaths.ToArray())
        Assert.Equal(Path.Combine(first, "vbi.rsp"), resolved.ResolveReference("vbi.rsp", Nothing))

        Dim unresolved = DirectCast(CommandLineRunner.GetSourceReferenceResolver(ParseScriptArguments({}), Nothing), SourceFileResolver)
        Assert.Empty(unresolved.SearchPaths)
        Assert.Null(unresolved.ResolveReference("vbi.rsp", Nothing))

        Dim nowhere = ParseScriptArguments({"/loadpath:" & Path.Combine(second, "nowhere")})
        Assert.Null(CommandLineRunner.GetSourceReferenceResolver(nowhere, Nothing).ResolveReference("vbi.rsp", Nothing))

        ' End to end, first submission: the working directory is the parent of the output directory, so only
        ' the search path can resolve "vbi.rsp" - and the loaded tree then reports its own syntax errors
        ' against the resolved path.
        Dim withPath = CreateRunner({"/loadpath:" & BaseDirectory}, JoinedLines("#Load ""vbi.rsp""", ReplMarker), ParentOfBaseDirectory)
        Assert.Equal(0, withPath.RunInteractive())
        Dim withPathOut = withPath.Console.Out.ToString()
        AssertNotInTranscript(FileNotFoundMessage("vbi.rsp"), withPathOut)
        AssertInTranscript(Path.Combine(BaseDirectory, "vbi.rsp"), withPathOut)
        AssertInTranscript(OutputLine(ReplMarkerResult), withPathOut)

        Dim withoutPath = CreateRunner({}, JoinedLines("#Load ""vbi.rsp""", ReplMarker), ParentOfBaseDirectory)
        Assert.Equal(0, withoutPath.RunInteractive())
        Dim withoutPathOut = withoutPath.Console.Out.ToString()
        AssertInTranscript("BC2001", withoutPathOut)
        AssertInTranscript(FileNotFoundMessage("vbi.rsp"), withoutPathOut)
    End Sub

    ''' <summary>
    ''' Cell 10 of 12 - C# `SourceSearchPaths_Change1`: the session starts with no source search paths, the
    ''' script mutates the collection the globals expose, and a `#Load` that could not resolve before now
    ''' resolves.
    ''' Discrimination: the two counts and the reported entry pin the collection's live state, the first `#Load`
    ''' pins the not-found diagnostic, and the second `#Load` is the discriminating one - it names a path that
    ''' exists only under the directory the script added (reachable through the output directory's own name, so
    ''' no fixture has to be written), so it fails if the change never reaches the resolver. The change reaches
    ''' it through CommandLineRunner.UpdateOptions, which re-derives both resolvers from globals.SourcePaths
    ''' after every successful submission; note that the same method also re-derives the resolver's *base*
    ''' directory from the process working directory, which is why the second `#Load` is given a path no base
    ''' directory can supply.
    ''' </summary>
    <Fact>
    Public Sub SourceSearchPathsChange_ReplCollectionAndResolvedLoad()
        Dim outputDirectoryName = Path.GetFileName(BaseDirectory)
        Dim resolvedTarget = Path.Combine(outputDirectoryName, "vbi.rsp")

        Dim runner = CreateRunner({}, JoinedLines(
            "? ""n="" & SourcePaths.Count",
            "#Load ""u6-missing-source.vbx""",
            "SourcePaths.Add(""" & ParentOfBaseDirectory & """)",
            "? ""n="" & SourcePaths.Count",
            "? SourcePaths(0)",
            "#Load """ & resolvedTarget & """",
            ReplMarker))

        Assert.Equal(0, runner.RunInteractive())
        Dim [out] = runner.Console.Out.ToString()
        AssertInTranscript(QuotedOutputLine("n=0"), [out])
        AssertInTranscript("BC2001", [out])
        AssertInTranscript(FileNotFoundMessage("u6-missing-source.vbx"), [out])
        AssertInTranscript(QuotedOutputLine("n=1"), [out])
        AssertInTranscript(QuotedOutputLine(ParentOfBaseDirectory), [out])
        AssertNotInTranscript(FileNotFoundMessage(resolvedTarget), [out])
        AssertInTranscript(Path.Combine(BaseDirectory, "vbi.rsp"), [out])
        AssertInTranscript(OutputLine(ReplMarkerResult), [out])
    End Sub

    ''' <summary>
    ''' Cell 11 of 12 - C# `ReferenceSearchPaths1` (`/r:4.dll` plus `/lib:`, `/libpath:`, `/libpaths:`).
    ''' Discrimination: the parsed reference path list ends with exactly the four directories in switch order,
    ''' which pins all three switch spellings and the `;` separated form (the list's leading entries are the
    ''' SDK paths that BuildSearchPaths always prepends - VisualBasicCommandLineParser.vb, BuildSearchPaths -
    ''' so the tail is what the switches contribute). The resolver pair is the real discrimination: the same
    ''' reference resolves to the exact path under the search path and to nothing without it, with the base
    ''' directory left empty so only the search path can supply it. The end-to-end pair adds the host's own
    ''' diagnostics: VB reports an unresolvable `#r` target as BC2017 (VBResources.ERR_LibNotFound - the
    ''' message provider maps ERR_MetadataFileNotFound onto ERR_LibNotFound, where C# has a dedicated CS0006),
    ''' and the same reference resolves once the switch is present.
    ''' The reference spells a relative path that only the search path can complete - "<output directory>\vbi.dll",
    ''' i.e. a hit under the search path root - and it is written with a directory separator on purpose: for a
    ''' bare file name the runtime resolver consults the host's platform assembly list first
    ''' (RuntimeMetadataReferenceResolver.ResolveReference, the ResolveTrustedPlatformAssembly shortcut), and
    ''' vbi.dll is in that list because this test assembly references it, which would make the negative below
    ''' succeed for the wrong reason. A reference that contains a directory separator skips that shortcut, and
    ''' the search path is then the only thing that can supply it. (A leading ".\" would be wrong in the other
    ''' direction: FileUtilities.ResolveRelativePath treats that kind as current-directory-relative and never
    ''' walks the search paths at all.)
    ''' </summary>
    <Fact>
    Public Sub ReferenceSearchPaths_SwitchFormsReachTheMetadataResolver()
        Dim first = BaseDirectory
        Dim second = Path.Combine(BaseDirectory, "u6-reference-store-a")
        Dim third = Path.Combine(BaseDirectory, "u6-reference-store-b")
        Dim fourth = Path.Combine(BaseDirectory, "u6-reference-store-c")

        Dim parsed = ParseScriptArguments(
            {"/nostdlib", "/lib:" & first, "/libpath:" & second, "/libpaths:" & third & ";" & fourth})
        Dim switched = parsed.ReferencePaths.ToArray()
        Assert.Equal({first, second, third, fourth}, switched.Skip(switched.Length - 4).ToArray())
        Assert.Empty(parsed.Errors)

        Dim relativeReference = Path.Combine(Path.GetFileName(BaseDirectory), "vbi.dll")

        Dim resolved = CommandLineRunner.GetMetadataReferenceResolver(
            ParseScriptArguments({"/nostdlib", "/lib:" & ParentOfBaseDirectory}), Nothing).
            ResolveReference(relativeReference, Nothing, MetadataReferenceProperties.Assembly)
        Assert.Equal(1, resolved.Length)
        Assert.Equal(HostAssembly, resolved(0).FilePath)

        Dim unresolved = CommandLineRunner.GetMetadataReferenceResolver(
            ParseScriptArguments({"/nostdlib"}), Nothing).
            ResolveReference(relativeReference, Nothing, MetadataReferenceProperties.Assembly)
        Assert.Empty(unresolved)

        ' End to end: the working directory is the output directory itself, so the reference "net10.0\vbi.dll"
        ' is one level above and only the search path can complete it.
        Dim runner = CreateRunner({"/lib:" & ParentOfBaseDirectory},
                                  JoinedLines("#r """ & relativeReference & """", ReplMarker))
        Assert.Equal(0, runner.RunInteractive())
        Dim [out] = runner.Console.Out.ToString()
        AssertNotInTranscript(LibraryNotFoundMessage(relativeReference), [out])
        AssertInTranscript(OutputLine(ReplMarkerResult), [out])

        ' The control: the same session and the same directive without the switch cannot resolve it.
        Dim control = CreateRunner({}, JoinedLines("#r """ & relativeReference & """", ReplMarker))
        Assert.Equal(0, control.RunInteractive())
        Dim controlOut = control.Console.Out.ToString()
        AssertInTranscript("BC2017", controlOut)
        AssertInTranscript(LibraryNotFoundMessage(relativeReference), controlOut)
    End Sub

    ''' <summary>
    ''' Cell 12 of 12 - C# `ReferenceSearchPaths_Change1`: the session starts with no reference search paths, an
    ''' unresolvable `#r` target is reported as such, the script adds the directory, and the same target
    ''' resolves.
    ''' Discrimination: the C# cell's own negative comes first and is pinned by message text, so the case fails
    ''' if the host silently ignored the directive; the second `#r` names a path that exists only under the
    ''' directory the script added (reachable through the output directory's own name), so it fails if the
    ''' change never reaches the metadata resolver - which CommandLineRunner.UpdateOptions performs from
    ''' globals.ReferencePaths after every successful submission. The submission after that `#r` *uses* a type
    ''' of the referenced assembly, so "the directive resolved" and "the assembly became usable" are pinned
    ''' separately, the way cell 11 pins the resolver and the host diagnostics separately.
    ''' The target is Microsoft.CodeAnalysis.VisualBasic.dll rather than cell 11's vbi.dll because every type
    ''' vbi.dll declares is Friend, so no script can name one; nothing else about the resolution changes - the
    ''' path still carries a directory separator, so the runtime resolver's trusted-platform-assembly shortcut
    ''' is skipped and the search path is the only thing that can complete it.
    ''' </summary>
    <Fact>
    Public Sub ReferenceSearchPathsChange_ReplCollectionAndResolvedReference()
        Dim outputDirectoryName = Path.GetFileName(BaseDirectory)
        Dim resolvedTarget = Path.Combine(outputDirectoryName, "Microsoft.CodeAnalysis.VisualBasic.dll")

        Dim runner = CreateRunner({}, JoinedLines(
            "? ""n="" & ReferencePaths.Count",
            "#r ""u6-missing-library.dll""",
            "ReferencePaths.Add(""" & ParentOfBaseDirectory & """)",
            "? ""n="" & ReferencePaths.Count",
            "? ReferencePaths(0)",
            "#r """ & resolvedTarget & """",
            "? GetType(Microsoft.CodeAnalysis.VisualBasic.LanguageVersion).Assembly.GetName().Name",
            ReplMarker))

        Assert.Equal(0, runner.RunInteractive())
        Dim [out] = runner.Console.Out.ToString()
        AssertInTranscript(QuotedOutputLine("n=0"), [out])
        AssertInTranscript("BC2017", [out])
        AssertInTranscript(LibraryNotFoundMessage("u6-missing-library.dll"), [out])
        AssertInTranscript(QuotedOutputLine("n=1"), [out])
        AssertInTranscript(QuotedOutputLine(ParentOfBaseDirectory), [out])
        AssertNotInTranscript(LibraryNotFoundMessage(resolvedTarget), [out])
        AssertInTranscript(QuotedOutputLine("Microsoft.CodeAnalysis.VisualBasic"), [out])
        AssertInTranscript(OutputLine(ReplMarkerResult), [out])
    End Sub

#End Region

End Class
