' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System
Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting
Imports Xunit

''' <summary>
''' Reproduction evidence (in-memory only, no side effects) for the question: can an uncaught
''' <see cref="GlobalImport.Parse(IEnumerable(Of String))"/> ArgumentException be triggered by real user
''' input (REPL submission / .vbx script / previous-submission replay)?
''' <para>
''' The throw site is <c>GlobalImport.Parse</c> (<c>Compilers\VisualBasic\Portable\GlobalImport.vb:77-86</c>),
''' reached from <c>VisualBasicScriptCompiler.GetGlobalImportsForCompilation</c>
''' (<c>Scripting\VisualBasic\VisualBasicScriptCompiler.vb:119</c>) with the union of
''' <c>ScriptOptions.Imports</c> and the replayed clause texts of previous submissions.
''' </para>
''' <para>
''' Verdict (asserted below): not reachable from real user input. The direct path only ever receives names
''' the command-line parser already accepted (and XML clauses are filtered out before they reach
''' ScriptOptions.Imports); the replay path only ever receives clause texts from submissions that passed the
''' host's <c>HasAnyErrors</c> gate, and every such clause round-trips through the project-import parser
''' without an error. The throw itself is real and is reachable through the Script library API, which is why
''' the compiler-side behavior is pinned here too.
''' </para>
''' <para>
''' Adjacent defect found while probing the "excluded" /imports: path (fixed): a syntactically bad
''' /imports: value makes <c>GlobalImport.Parse(String, ByRef)</c> return Nothing, and the command-line
''' parser used to store that Nothing in <c>GlobalImports</c>, which made <c>GetScriptOptions</c> NRE in
''' <c>GetImports</c> before the Arguments.Errors gate. The parser now keeps only successfully parsed
''' clauses; the diagnostic still lands in <c>Arguments.Errors</c> and the host reports it at the gate.
''' </para>
''' </summary>
Public Class ImportsAccumulationFailureTests

    Private Shared ReadOnly s_options As ScriptOptions = ScriptOptions.Default

    ''' <summary>Submissions whose Imports clause is accepted (at most warnings), i.e. they pass the host gate.</summary>
    Private Shared Iterator Function AcceptedImportStatements() As IEnumerable(Of String)
        Yield "Imports System.Text"
        Yield "Imports R = System.Text"
        Yield "Imports <xmlns:p=""urn:test"">"
        Yield "Imports <xmlns:q='urn:test'>"
        Yield "Imports <xmlns=""urn:test"">"
        Yield "Imports System.Text, System.IO"
        Yield "Imports System.Text ' comment"
        Yield "Imports System. _" & vbCrLf & "        Text"
        Yield "Imports L = System.Collections.Generic.List(Of Integer)"
        Yield "Imports [X] = System.Text"
        Yield "Imports No.Such.Namespace"
        Yield "Imports <xmlns:p-q=""urn:test"">"
        Yield "Imports <xmlns:If=""urn:test"">"
        Yield "Imports System.Text, R = System.IO"
        Yield "Imports System.Text.StringBuilder"
        Yield "Imports <xmlns:p=""urn:test"" >"
        Yield "Imports X = System.Collections.Generic.Dictionary(Of String, System.Collections.Generic.List(Of Integer))"
    End Function

    ''' <summary>Submissions the host gate rejects, so their clauses never enter the replay chain.</summary>
    Private Shared Iterator Function RejectedImportStatements() As IEnumerable(Of String)
        Yield "Imports Foo."
        Yield "Imports 123"
        Yield "Imports Foo Bar"
        Yield "Imports global"
        Yield "Imports Global.System.Text"
        Yield "Imports <xmlns = ""http://xml"">"
        Yield "Imports Foo ="
        Yield "Imports ="
        Yield "Imports"
        Yield "Imports A = Integer()"
        Yield "Imports T = (Integer, String)"
        Yield "Imports N = Integer?"
        Yield "Imports System = System.Text"
    End Function

    Private Shared Function TryGetCompilationException(script As Script) As Exception
        Try
            script.GetCompilation()
            Return Nothing
        Catch ex As Exception
            Return ex
        End Try
    End Function

    ''' <summary>Submission 1 carries the statement; submission 2 forces the replay of its clause texts.</summary>
    Private Shared Function Replay(statement As String) As (HasErrors As Boolean, ReplayException As Exception)
        Dim sub1 = VisualBasicScript.Create(statement, s_options)
        Dim hasErrors = sub1.Compile().HasAnyErrors()
        Dim ex = TryGetCompilationException(sub1.ContinueWith("? 1", s_options))
        Return (hasErrors, ex)
    End Function

#Region "L1 - compiler-side facts"

    <Fact>
    Public Sub GlobalImportParse_ThrowingOverload_ThrowsArgumentException()
        ' The overload used by the host (VisualBasicScriptCompiler.vb:119) throws on an Error-severity parse
        ' diagnostic (GlobalImport.vb:80-84).
        Dim ex = Assert.Throws(Of ArgumentException)(Sub() GlobalImport.Parse(New String() {"Foo."}))

        Assert.Contains("Foo.", ex.Message)
    End Sub

    <Fact>
    Public Sub GlobalImportParse_DiagnosticOverload_ReportsInsteadOfThrowing()
        ' The overload the command-line parser uses (VisualBasicCommandLineParser.vb:1870) surfaces the same
        ' error as a diagnostic and does not throw.
        Dim diagnostics As ImmutableArray(Of Diagnostic) = Nothing
        Dim parsedImports = GlobalImport.Parse(New String() {"Foo."}, diagnostics)

        Assert.Contains(diagnostics, Function(d) d.Severity = DiagnosticSeverity.Error)
        Assert.Empty(parsedImports)
    End Sub

#End Region

#Region "A - direct path: ScriptOptions.Imports"

    <Fact>
    Public Sub DirectOptionsImports_BadNames_ThrowArgumentException()
        ' Library API only: a bad name handed straight to ScriptOptions.Imports throws. No vbi input
        ' reaches this (see the two tests below).
        For Each name In {"", "Foo Bar", "123", "Foo =", "global", "<xmlns = ""http://xml"">", "A,B", "Foo."}
            Dim ex = TryGetCompilationException(VisualBasicScript.Create("? 1", s_options.AddImports(name)))

            Assert.IsType(Of ArgumentException)(ex)
        Next
    End Sub

    <Fact>
    Public Sub DirectOptionsImports_AcceptedNames_DoNotThrow()
        For Each name In {"System.Text", "<xmlns:p='urn:test'>", "System.Text _"}
            Assert.Null(TryGetCompilationException(VisualBasicScript.Create("? 1", s_options.AddImports(name))))
        Next
    End Sub

    <Fact>
    Public Sub DirectOptionsImports_CommandLineReachableCombinations_DoNotThrow()
        ' The host parses all names in one GlobalImport.Parse call; two individually valid names must not
        ' interact. ("System.Text," alone is a bad name and is never produced by /imports: - it is comma-split.)
        For Each combo In {
            New String() {"System.Text", "System.IO"},
            New String() {"System.Text _", "System.IO"},
            New String() {"<xmlns:p='urn:test'>", "System.IO"},
            New String() {"System.Text", "System.IO _"}
        }
            Assert.Null(TryGetCompilationException(VisualBasicScript.Create("? 1", s_options.AddImports(combo))))
        Next
    End Sub

    <Fact>
    Public Sub CommandLineImportsSwitch_BadValue_IsReportedAsArgumentsErrors()
        ' vbi's /imports: uses the diagnostic overload, so the error lands in Arguments.Errors instead of
        ' throwing. (The rejected clause is not stored in GlobalImports - see the next test.)
        Dim args = VisualBasicCommandLineParser.Script.Parse({"/imports:Foo."}, AppContext.BaseDirectory, RuntimeMetadataReferenceResolver.GetDesktopFrameworkDirectory())

        Assert.Contains(args.Errors, Function(d) d.Severity = DiagnosticSeverity.Error)
        Assert.Contains(args.Errors, Function(d) d.GetMessage().Contains("Foo."))
    End Sub

    <Fact>
    Public Sub GlobalImportParse_StringWithDiagnosticsOverload_ReturnsNothingOnError()
        ' GlobalImport.Parse(String, ByRef) indexes the (empty) result of the collection overload
        ' (GlobalImport.vb:68-70); VB resolves that index through ElementAtOrDefault, so a syntactically bad
        ' name yields Nothing rather than an exception.
        Dim diagnostics As ImmutableArray(Of Diagnostic) = Nothing
        Dim import = GlobalImport.Parse("Foo.", diagnostics)

        Assert.Null(import)
        Assert.Contains(diagnostics, Function(d) d.Severity = DiagnosticSeverity.Error)
    End Sub

    <Fact>
    Public Sub CommandLineImportsSwitch_BadValue_IsReportedAndLeavesGlobalImportsClean()
        ' VisualBasicCommandLineParser.ParseGlobalImports only stores clauses that parsed successfully
        ' (VisualBasicCommandLineParser.vb:1872), so a bad /imports: value lands in Arguments.Errors while
        ' GlobalImports stays free of Nothing. Every GlobalImports consumer - GetImports
        ' (VisualBasicCompilationOptions.vb:343-352), the deterministic key builder, the source symbols - can
        ' then run without an NRE. This is a separate path from the ArgumentException this file investigates
        ' (that throw needs the IEnumerable overload, which vbi never reaches).
        Dim args = VisualBasicCommandLineParser.Script.Parse({"/imports:Foo."}, AppContext.BaseDirectory, RuntimeMetadataReferenceResolver.GetDesktopFrameworkDirectory())
        Dim vbOptions = DirectCast(args.CompilationOptions, VisualBasicCompilationOptions)

        Assert.Contains(args.Errors, Function(d) d.Severity = DiagnosticSeverity.Error)
        Assert.Contains(args.Errors, Function(d) d.GetMessage().Contains("Foo."))
        Assert.DoesNotContain(vbOptions.GlobalImports, Function(g) g Is Nothing)
        Assert.Empty(vbOptions.GetImports())
    End Sub

    <Fact>
    Public Sub CommandLineImportsSwitch_MixedGoodAndBadValues_KeepsOnlyTheGoodClause()
        ' The guard drops only the rejected clause: sibling clauses split from the same switch value are still
        ' stored, so the diagnostic and the surviving import coexist.
        Dim args = VisualBasicCommandLineParser.Script.Parse({"/imports:Foo.,System.Text"}, AppContext.BaseDirectory, RuntimeMetadataReferenceResolver.GetDesktopFrameworkDirectory())
        Dim vbOptions = DirectCast(args.CompilationOptions, VisualBasicCompilationOptions)

        Assert.Contains(args.Errors, Function(d) d.Severity = DiagnosticSeverity.Error)
        Assert.Equal(1, vbOptions.GlobalImports.Length)
        Assert.Equal("System.Text", vbOptions.GlobalImports(0).Name)
        Assert.Equal(New String() {"System.Text"}, vbOptions.GetImports())
    End Sub

    <Fact>
    Public Sub CommandLineImportsSwitch_XmlClause_IsFilteredOutBeforeScriptOptionsImports()
        ' VisualBasicCompilationOptions.GetImports filters XML clauses (VisualBasicCompilationOptions.vb:343-352),
        ' so /imports:<xmlns:...> never even reaches the host's GlobalImport.Parse call.
        Dim args = VisualBasicCommandLineParser.Script.Parse({"/imports:<xmlns:p='urn:test'>"}, AppContext.BaseDirectory, RuntimeMetadataReferenceResolver.GetDesktopFrameworkDirectory())
        Dim vbOptions = DirectCast(args.CompilationOptions, VisualBasicCompilationOptions)

        Assert.Empty(args.Errors.Where(Function(d) d.Severity = DiagnosticSeverity.Error))
        Assert.Empty(vbOptions.GetImports())
    End Sub

#End Region

#Region "B - replay path: previous submissions"

    <Fact>
    Public Sub ReplayPath_AcceptedClauseForms_DoNotThrow()
        For Each statement In AcceptedImportStatements()
            Dim result = Replay(statement)

            Assert.False(result.HasErrors, $"Statement must pass the host gate: {statement}")
            Assert.True(result.ReplayException Is Nothing, $"Replay must not throw: {statement} -> {result.ReplayException}")
        Next
    End Sub

    <Fact>
    Public Sub ReplayPath_RejectedClauseForms_AreBlockedByTheSubmissionGate()
        ' Every form whose clause text makes GlobalImport.Parse throw is already an error-carrying submission,
        ' so CommandLineRunner never adopts it as the session's previous submission (CommandLineRunner.cs:370-375).
        For Each statement In RejectedImportStatements()
            Dim result = Replay(statement)

            Assert.True(result.HasErrors, $"Statement must be rejected before it can be replayed: {statement}")
        Next
    End Sub

    <Fact>
    Public Sub ReplayPath_WhenGateIsBypassed_TheThrowIsReal()
        ' Documents why the gate is load-bearing: bypassing it (Script.Create + ContinueWith without checking
        ' diagnostics) lets the host throw.
        ' 1. "Foo." round-trips as a bad project-import clause -> ArgumentException from GlobalImport.Parse.
        Dim badClause = Replay("Imports Foo.")
        Assert.IsType(Of ArgumentException)(badClause.ReplayException)

        ' 2. "Foo Bar" error-recovers to the clause text "Foo", which parses fine; the next line of defence is
        '    the compiler itself refusing to chain onto an error-carrying submission (Compilation.cs:274-277).
        Dim recoveredClause = Replay("Imports Foo Bar")
        Assert.IsType(Of InvalidOperationException)(recoveredClause.ReplayException)
    End Sub

    <Fact>
    Public Sub ReplayPath_ThreeSubmissionChain_AcceptedImportsAtEachLevel_DoNotThrow()
        Dim s1 = VisualBasicScript.Create("Imports System.Text", s_options)
        Dim s2 = s1.ContinueWith("Imports System.IO, R = System.Text.RegularExpressions", s_options)
        Dim s3 = s2.ContinueWith("? 1", s_options)

        Assert.Null(TryGetCompilationException(s3))
    End Sub

    <Fact>
    Public Sub ReplayPath_LoadTreeImports_AcceptedForm_DoNotThrow()
        ' #Load'ed trees are part of the submission's compilation, so their top-level Imports are replayed too.
        Dim files = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"C:\scripts\loaded.vbx", "Imports System.Text" & vbCrLf &
                                      "Function Loaded() As Integer" & vbCrLf &
                                      "    Return 42" & vbCrLf &
                                      "End Function"},
            {"C:\scripts\main.vbx", "#Load ""loaded.vbx""" & vbCrLf & "? Loaded()"}
        }
        Dim options = s_options.WithFilePath("C:\scripts\main.vbx").WithSourceResolver(New InMemorySourceResolver(files))
        Dim sub1 = VisualBasicScript.Create(files("C:\scripts\main.vbx"), options)

        Assert.False(sub1.Compile().HasAnyErrors())
        Assert.Null(TryGetCompilationException(sub1.ContinueWith("? 1")))
    End Sub

    <Fact>
    Public Sub ReplayPath_LoadTreeImports_RejectedForm_IsBlockedByTheSubmissionGate()
        Dim files = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"C:\scripts\loaded.vbx", "Imports Foo."},
            {"C:\scripts\main.vbx", "#Load ""loaded.vbx""" & vbCrLf & "? 1"}
        }
        Dim options = s_options.WithFilePath("C:\scripts\main.vbx").WithSourceResolver(New InMemorySourceResolver(files))
        Dim sub1 = VisualBasicScript.Create(files("C:\scripts\main.vbx"), options)

        Assert.True(sub1.Compile().HasAnyErrors())
    End Sub

#End Region

#Region "C - boundaries"

    <Fact>
    Public Async Function ReplGate_ErrorSubmissionIsDiscarded_SoBadClauseIsNeverReplayed() As Task
        ' Mirrors CommandLineRunner.cs:370-375: a submission whose Compile() has errors leaves (state, options)
        ' unchanged, so the next submission continues from the last good state.
        Dim lastGood = VisualBasicScript.Create("Dim a = 1", s_options)
        Await lastGood.RunAsync()

        Dim rejected = lastGood.ContinueWith("Imports Foo." & vbCrLf & "Dim b = 2", s_options)
        Assert.True(rejected.Compile().HasAnyErrors())

        Dim nextSubmission = lastGood.ContinueWith("? a", s_options)
        Assert.Equal(1, (Await nextSubmission.RunAsync()).ReturnValue)
    End Function

    <Fact>
    Public Sub ReplHost_BadImportsSubmission_IsReportedAndTheSessionContinues()
        ' End-to-end through CommandLineRunner (in-memory console; no file is created: an existing directory
        ' is reused as BuildPaths.TempDir).
        Dim runner = CreateRunner({"/R:System"}, "Imports 123" & vbCrLf & "? 1 + 2")

        runner.RunInteractive()

        Dim output = runner.Console.Out.ToString()
        Assert.Contains("error BC3", output)
        Assert.Contains(vbCrLf & "3" & vbCrLf, output)
    End Sub

    <Fact>
    Public Sub ReplHost_BadImportsSwitch_IsReportedAndFailsAtTheErrorsGate()
        ' End-to-end "vbi /imports:Foo.": the bad value is a parse-time diagnostic in Arguments.Errors, and the
        ' host reports it at the Errors gate (CommandLineRunner.cs:142-146) and returns Failed (= 1) before
        ' GetScriptOptions' GlobalImports consumers run - no NRE and no stack trace. The REPL never starts.
        Dim runner = CreateRunner({"/imports:Foo.", "/R:System"}, "? 1 + 2")

        Assert.Equal(1, runner.RunInteractive())
        Assert.Contains("Foo.", runner.Console.Error.ToString())
        Assert.DoesNotContain("> ? 1 + 2", runner.Console.Out.ToString())
    End Sub

    Private Shared Function CreateRunner(args As String(), input As String) As CommandLineRunner
        Dim buildPaths = New BuildPaths(
            clientDir:=AppContext.BaseDirectory,
            workingDir:=AppContext.BaseDirectory,
            sdkDir:=RuntimeMetadataReferenceResolver.GetDesktopFrameworkDirectory(),
            tempDir:=AppContext.BaseDirectory)
        Dim compiler = New VisualBasicInteractiveCompiler(
            Path.Combine(AppContext.BaseDirectory, "vbi.rsp"), buildPaths, args, New NotImplementedAnalyzerLoader())
        Return New CommandLineRunner(New TestConsoleIO(input), compiler, VisualBasicScriptCompiler.Instance, VisualBasicObjectFormatter.Instance)
    End Function


    <Fact>
    Public Sub VbxScriptFile_BadImports_ThrowsCompilationErrorException_NotArgumentException()
        ' A .vbx script is a single submission: its own Imports are file-level imports, never project-level
        ' ones, so a bad clause surfaces as a normal compile error.
        Dim ex = Assert.Throws(Of CompilationErrorException)(
            Function() VisualBasicScript.RunAsync("Imports Foo." & vbCrLf & "? 1", s_options).GetAwaiter().GetResult())

        Assert.Contains(ex.Diagnostics, Function(d) d.Severity = DiagnosticSeverity.Error)
    End Sub

#End Region

    Private NotInheritable Class InMemorySourceResolver
        Inherits SourceReferenceResolver

        Private ReadOnly _files As IReadOnlyDictionary(Of String, String)

        Public Sub New(files As IReadOnlyDictionary(Of String, String))
            _files = files
        End Sub

        Public Overrides Function NormalizePath(path As String, baseFilePath As String) As String
            Return If(ResolveReference(path, baseFilePath), path)
        End Function

        Public Overrides Function ResolveReference(path As String, baseFilePath As String) As String
            If _files.ContainsKey(path) Then
                Return path
            End If
            If baseFilePath IsNot Nothing Then
                Dim dir = IO.Path.GetDirectoryName(baseFilePath)
                Dim combined = IO.Path.Combine(If(dir, ""), path)
                If _files.ContainsKey(combined) Then
                    Return combined
                End If
            End If
            Return Nothing
        End Function

        Public Overrides Function OpenRead(resolvedPath As String) As Stream
            Return New MemoryStream(Encoding.UTF8.GetBytes(_files(resolvedPath)))
        End Function

        Public Overrides Function ReadText(resolvedPath As String) As SourceText
            Return SourceText.From(_files(resolvedPath))
        End Function

        Public Overrides Function Equals(other As Object) As Boolean
            Return ReferenceEquals(Me, other)
        End Function

        Public Overrides Function GetHashCode() As Integer
            Return _files.Count
        End Function
    End Class
End Class
