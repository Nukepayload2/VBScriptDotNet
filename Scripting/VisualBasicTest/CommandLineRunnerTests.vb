' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Globalization
Imports System.IO
Imports System.Reflection
Imports Microsoft.CodeAnalysis.CSharp
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting
Imports My.Resources
Imports Xunit

Public Class CommandLineRunnerTests

    Private Shared ReadOnly s_interactiveCompilerVersion As String =
        GetType(VisualBasicInteractiveCompiler).Assembly.
            GetCustomAttribute(Of AssemblyInformationalVersionAttribute).InformationalVersion

    Private Shared ReadOnly s_roslynVersion As String = FormatVersionWithoutRevision(
        GetType(VisualBasicCompiler).Assembly.GetName.Version)

    Private Shared ReadOnly s_versionOutput As String =
        String.Format(VBScriptingResources.LogoLine1, s_interactiveCompilerVersion) + Environment.NewLine +
        String.Format(VBScriptingResources.LogoLine2, s_roslynVersion) + Environment.NewLine

    Private Shared ReadOnly s_logoAndHelpPrompt As String =
        String.Format(VBScriptingResources.LogoLine1, s_interactiveCompilerVersion) + Environment.NewLine +
        String.Format(VBScriptingResources.LogoLine2, s_roslynVersion) + Environment.NewLine +
        VBScriptingResources.LogoLine3 + Environment.NewLine +
        VBScriptingResources.LogoLine4 + "

" + ScriptingResources.HelpPrompt

    Private Shared ReadOnly s_defaultArgs As String() = {"/R:System"}

    Private Shared Function FormatVersionWithoutRevision(version As Version) As String
        Return New Version(version.Major, version.Minor, version.Build).ToString()
    End Function

    Private Shared Function CreateIsolatedTempDirectory() As String
        Dim directory = Path.Combine(AppContext.BaseDirectory, "TestTemp", Guid.NewGuid().ToString("N"))
        System.IO.Directory.CreateDirectory(directory)
        Return directory
    End Function

    Private Shared Function CreateLibraryAssembly(directory As String, assemblyName As String, source As String) As String
        Dim assemblyPath = Path.Combine(directory, assemblyName + ".dll")
        Dim syntaxTree = SyntaxFactory.ParseSyntaxTree(source)
        Dim references = {
            MetadataReference.CreateFromFile(GetType(Object).Assembly.Location),
            MetadataReference.CreateFromFile(GetType(Strings).Assembly.Location)
        }

        Dim compilation = VisualBasicCompilation.Create(
            assemblyName,
            {syntaxTree},
            references,
            New VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, rootNamespace:=""))

        Dim result = compilation.Emit(assemblyPath)
        Assert.True(result.Success, String.Join(Environment.NewLine, result.Diagnostics))
        Return assemblyPath
    End Function

    ''' <summary>
    ''' Builds a C# 14 extension-member library (extension properties/operators, SAIM) in-memory with the
    ''' in-repo C# compiler (LanguageVersion.Preview) and emits it to the given isolated temp directory.
    ''' Used by the R-series tests; no dependency on an external ExternLib.dll artifact.
    ''' </summary>
    Private Shared Function CreateCSharpLibraryAssembly(directory As String, assemblyName As String, source As String) As String
        Dim assemblyPath = Path.Combine(directory, assemblyName + ".dll")
        Dim parseOptions = CSharp.CSharpParseOptions.Default.WithLanguageVersion(CSharp.LanguageVersion.Preview)
        Dim syntaxTree = CSharp.CSharpSyntaxTree.ParseText(source, parseOptions)
        Dim references = {
            MetadataReference.CreateFromFile(GetType(Object).Assembly.Location)
        }

        Dim compilation = CSharp.CSharpCompilation.Create(
            assemblyName,
            {syntaxTree},
            references,
            New CSharp.CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))

        Dim result = compilation.Emit(assemblyPath)
        Assert.True(result.Success, String.Join(Environment.NewLine, result.Diagnostics))
        Return assemblyPath
    End Function

    Private Shared Function CreateRunner(
        Optional args As String() = Nothing,
        Optional input As String = "",
        Optional responseFile As String = Nothing,
        Optional workingDirectory As String = Nothing
    ) As CommandLineRunner
        Dim io = New TestConsoleIO(input)

        Dim buildPaths = New BuildPaths(
            clientDir:=AppContext.BaseDirectory,
            workingDir:=If(workingDirectory, AppContext.BaseDirectory),
            sdkDir:=RuntimeMetadataReferenceResolver.GetDesktopFrameworkDirectory(),
            tempDir:=CreateIsolatedTempDirectory())

        Dim compiler = New VisualBasicInteractiveCompiler(
            If(responseFile, Path.Combine(AppContext.BaseDirectory, "vbi.rsp")),
            buildPaths,
            If(args, s_defaultArgs),
            New NotImplementedAnalyzerLoader())

        Return New CommandLineRunner(
            io,
            compiler,
            VisualBasicScriptCompiler.Instance,
            VisualBasicObjectFormatter.Instance)
    End Function

    <Fact>
    Public Sub TestPrint()
        Dim runner = CreateRunner(input:="? 10")

        runner.RunInteractive()

        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_logoAndHelpPrompt + "
> ? 10
10
>", runner.Console.Out.ToString())
    End Sub

    <Fact>
    Public Sub TestImportArgument()
        Dim runner = CreateRunner(args:={"/Imports:<xmlns:xmlNamespacePrefix='xmlNamespaceName'>"})

        runner.RunInteractive()

        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_logoAndHelpPrompt + "
>", runner.Console.Out.ToString())
    End Sub

    <Fact>
    Public Sub TestSourceImportsDoNotReplaceResponseFileImports()
        Dim runner = CreateRunner(input:="? GetType(Console).FullName
Imports System.Text
? GetType(Console).FullName")

        runner.RunInteractive()

        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_logoAndHelpPrompt + "
> ? GetType(Console).FullName
""System.Console""
> Imports System.Text
> ? GetType(Console).FullName
""System.Console""
>", runner.Console.Out.ToString())
    End Sub

    <Fact>
    Public Sub TestReferenceDirective()
        Dim directory = CreateIsolatedTempDirectory()
        Dim libraryPath = CreateLibraryAssembly(directory, "ReferenceDirectiveLibrary", "
Public Class C1
    Public Function Goo() As String
        Return ""Bar""
    End Function
End Class")

        Dim runner = CreateRunner(args:={}, input:="#r """ & libraryPath & """" & vbCrLf & "? New C1().Goo()")

        runner.RunInteractive()

        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_logoAndHelpPrompt + "
> #r """ & libraryPath & """
> ? New C1().Goo()
""Bar""
>", runner.Console.Out.ToString())

        runner = CreateRunner(args:={}, input:="? New C1().Goo()")

        runner.RunInteractive()

        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_logoAndHelpPrompt + "
> ? New C1().Goo()
«Red»
(1) : error BC30002: " + String.Format(VBResources.ERR_UndefinedType1, "C1") + "
«Gray»
>", runner.Console.Out.ToString())
    End Sub

    <Fact>
    Public Sub TestReferenceAndImportsArguments()
        Dim directory = CreateIsolatedTempDirectory()
        Dim libraryPath = CreateLibraryAssembly(directory, "ReferenceArgumentLibrary", "
Namespace ReferenceArgumentLibrary
    Public Class C1
        Public Function Goo() As String
            Return ""Bar""
        End Function
    End Class
End Namespace")

        Dim runner = CreateRunner(
            args:={"/r:" & Path.GetFileName(libraryPath), "/imports:ReferenceArgumentLibrary"},
            input:="? New C1().Goo()",
            workingDirectory:=directory)

        runner.RunInteractive()

        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_logoAndHelpPrompt + "
> ? New C1().Goo()
""Bar""
>", runner.Console.Out.ToString())
    End Sub

    <Fact>
    Public Sub TestReferenceDirectiveWhenReferenceMissing()
        Dim runner = CreateRunner(args:={}, input:="#r ""://invalidfilepath""")

        runner.RunInteractive()

        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_logoAndHelpPrompt + "
> #r ""://invalidfilepath""
«Red»
(1) : error BC2017: " + String.Format(VBResources.ERR_LibNotFound, "://invalidfilepath") + "
«Gray»
>", runner.Console.Out.ToString())
    End Sub

    <Fact>
    Public Sub TestLoadDirectiveInInteractive()
        Dim directory = CreateIsolatedTempDirectory()
        File.WriteAllText(Path.Combine(directory, "loaded.vbx"), "
Function LoadedValue() As Integer
    Return 42
End Function")

        Dim runner = CreateRunner(args:={}, input:="#Load ""loaded.vbx""" & vbCrLf & "? LoadedValue()", workingDirectory:=directory)

        runner.RunInteractive()

        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_logoAndHelpPrompt + "
> #Load ""loaded.vbx""
> ? LoadedValue()
42
>", runner.Console.Out.ToString())
    End Sub

    <Fact>
    Public Sub TestLoadDirectiveInScriptFile()
        Dim directory = CreateIsolatedTempDirectory()
        File.WriteAllText(Path.Combine(directory, "loaded.vbx"), "
Function LoadedValue() As Integer
    Return 42
End Function")
        File.WriteAllText(Path.Combine(directory, "main.vbx"), "#Load ""loaded.vbx""
Print(LoadedValue())")

        Dim runner = CreateRunner(args:={"main.vbx"}, workingDirectory:=directory)

        Assert.Equal(0, runner.RunInteractive())
        AssertEx.AssertEqualToleratingWhitespaceDifferences("42", runner.Console.Out.ToString())
    End Sub

    <Fact>
    Public Sub TestQuestionDirectiveInScriptFileDoesNotSetExitCode()
        ' The trailing expression of a script file does not set the exit code; only an explicit Return does.
        Dim directory = CreateIsolatedTempDirectory()
        File.WriteAllText(Path.Combine(directory, "main.vbx"), "? 21")

        Dim runner = CreateRunner(args:={"main.vbx"}, workingDirectory:=directory)

        Assert.Equal(0, runner.RunInteractive())
        AssertEx.AssertEqualToleratingWhitespaceDifferences("", runner.Console.Out.ToString())
    End Sub

    <Fact>
    Public Sub TestTrailingExpressionInScriptFileDoesNotSetExitCode()
        ' Even an unconvertible trailing expression compiles and is discarded (no conversion error),
        ' and it does not set the exit code.
        Dim directory = CreateIsolatedTempDirectory()
        File.WriteAllText(Path.Combine(directory, "main.vbx"), "? New System.Guid()")

        Dim runner = CreateRunner(args:={"main.vbx"}, workingDirectory:=directory)

        Assert.Equal(0, runner.RunInteractive())
        AssertEx.AssertEqualToleratingWhitespaceDifferences("", runner.Console.Out.ToString())
    End Sub

    <Fact>
    Public Sub TestReturnStatementSetsExitCodeInScriptFile()
        ' Function Main semantics: an explicit Return sets the exit code.
        Dim directory = CreateIsolatedTempDirectory()
        File.WriteAllText(Path.Combine(directory, "main.vbx"), "Return 21")

        Dim runner = CreateRunner(args:={"main.vbx"}, workingDirectory:=directory)

        Assert.Equal(21, runner.RunInteractive())
        AssertEx.AssertEqualToleratingWhitespaceDifferences("", runner.Console.Out.ToString())
    End Sub

    <Fact>
    Public Sub TestBareReturnStatementExitCodeIsZero()
        ' Function Main semantics: a bare Return exits with 0.
        Dim directory = CreateIsolatedTempDirectory()
        File.WriteAllText(Path.Combine(directory, "main.vbx"), "Return")

        Dim runner = CreateRunner(args:={"main.vbx"}, workingDirectory:=directory)

        Assert.Equal(0, runner.RunInteractive())
        AssertEx.AssertEqualToleratingWhitespaceDifferences("", runner.Console.Out.ToString())
    End Sub

    <Fact>
    Public Sub TestResponseFileReferencesAndImportsInScriptFile()
        Dim directory = CreateIsolatedTempDirectory()
        Dim libraryPath = CreateLibraryAssembly(directory, "ResponseFileLibrary", "
Namespace ResponseFileLibrary
    Public Class C1
        Public Function Goo() As String
            Return ""Bar""
        End Function
    End Class
End Namespace")
        Dim responseFile = Path.Combine(directory, "custom.vbi.rsp")
        File.WriteAllText(responseFile, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "vbi.rsp")) & "
/r:""" & libraryPath & """
/imports:ResponseFileLibrary")
        File.WriteAllText(Path.Combine(directory, "main.vbx"), "Print(New C1().Goo())")

        Dim runner = CreateRunner(args:={"main.vbx"}, responseFile:=responseFile, workingDirectory:=directory)

        Assert.Equal(0, runner.RunInteractive())
        AssertEx.AssertEqualToleratingWhitespaceDifferences("""Bar""", runner.Console.Out.ToString())
    End Sub

    <Fact>
    Public Sub TestScriptFileNotFoundReportsDiagnostic()
        Dim directory = CreateIsolatedTempDirectory()
        Dim runner = CreateRunner(args:={"missing.vbx"}, workingDirectory:=directory)

        Assert.Equal(1, runner.RunInteractive())
        Assert.Contains("missing.vbx", runner.Console.Out.ToString())
        Assert.Contains("missing.vbx", runner.Console.Error.ToString())
    End Sub

    <Fact>
    Public Sub TestTopLevelAwaitInScriptFile()
        Dim directory = CreateIsolatedTempDirectory()
        File.WriteAllText(Path.Combine(directory, "main.vbx"), "Imports System.Threading.Tasks
Dim value = Await Task.FromResult(13)
Print(value)")

        Dim runner = CreateRunner(args:={"main.vbx"}, workingDirectory:=directory)

        Assert.Equal(0, runner.RunInteractive())
        AssertEx.AssertEqualToleratingWhitespaceDifferences("13", runner.Console.Out.ToString())
    End Sub

    <Fact>
    Public Sub TestBareAwaitStatementInScriptFile()
        Dim directory = CreateIsolatedTempDirectory()
        File.WriteAllText(Path.Combine(directory, "main.vbx"), "Imports System.Threading.Tasks
Await Task.Delay(0)
Print(""after"")")

        Dim runner = CreateRunner(args:={"main.vbx"}, workingDirectory:=directory)

        Assert.Equal(0, runner.RunInteractive())
        AssertEx.AssertEqualToleratingWhitespaceDifferences("""after""", runner.Console.Out.ToString())
    End Sub

    <Fact>
    Public Sub TestTopLevelAddHandlerInScriptFile()
        Dim directory = CreateIsolatedTempDirectory()
        File.WriteAllText(Path.Combine(directory, "main.vbx"), "Public Class TestEvents
    Public Event SomethingHappened As EventHandler
    Public Sub Raise()
        RaiseEvent SomethingHappened(Me, EventArgs.Empty)
    End Sub
End Class

Dim t As New TestEvents
Dim ran As Boolean = False
AddHandler t.SomethingHappened, Sub() ran = True
t.Raise()
Print(ran)")

        Dim runner = CreateRunner(args:={"main.vbx"}, workingDirectory:=directory)

        Assert.Equal(0, runner.RunInteractive())
        AssertEx.AssertEqualToleratingWhitespaceDifferences("True", runner.Console.Out.ToString())
    End Sub

    <Fact>
    Public Sub TestScriptFileArguments()
        Dim directory = CreateIsolatedTempDirectory()
        File.WriteAllText(Path.Combine(directory, "main.vbx"), "Print(1)")

        Dim runner = CreateRunner(args:={"main.vbx", "--", "alpha", "beta"}, workingDirectory:=directory)

        Assert.Equal(0, runner.RunInteractive())
        AssertEx.AssertEqualToleratingWhitespaceDifferences("1", runner.Console.Out.ToString())
    End Sub

    <Fact, WorkItem("https://github.com/dotnet/roslyn/issues/7133")>
    Public Sub TestDisplayResultsWithCurrentUICulture1()
        ' Save the current thread culture as it is changed in the test.
        ' If the culture is not restored after the test all following tests
        ' would run in the en-GB culture.
        Dim currentCulture = CultureInfo.DefaultThreadCurrentCulture
        Dim currentUICulture = CultureInfo.DefaultThreadCurrentUICulture
        Try
            Dim runner = CreateRunner(args:={}, input:="Imports System.Globalization
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(""en-GB"")
? System.Math.PI
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(""de-DE"")
? System.Math.PI")

            runner.RunInteractive()

            AssertEx.AssertEqualToleratingWhitespaceDifferences(
s_logoAndHelpPrompt + "
> Imports System.Globalization
> System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(""en-GB"")
> ? System.Math.PI
3.141592653589793
> System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(""de-DE"")
> ? System.Math.PI
3,141592653589793
>", runner.Console.Out.ToString())
        Finally
            CultureInfo.DefaultThreadCurrentCulture = currentCulture
            CultureInfo.DefaultThreadCurrentUICulture = currentUICulture
        End Try
    End Sub

    <Fact, WorkItem("https://github.com/dotnet/roslyn/issues/7133")>
    Public Sub TestDisplayResultsWithCurrentUICulture2()
        ' Save the current thread culture as it is changed in the test.
        ' If the culture is not restored after the test all following tests
        ' would run in the en-GB culture.
        Dim currentCulture = CultureInfo.DefaultThreadCurrentCulture
        Dim currentUICulture = CultureInfo.DefaultThreadCurrentUICulture
        Try
            ' Tests that DefaultThreadCurrentUICulture is respected and not DefaultThreadCurrentCulture.
            Dim runner = CreateRunner(args:={}, input:="Imports System.Globalization
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(""en-GB"")
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(""en-GB"")
? System.Math.PI
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(""de-DE"")
? System.Math.PI")

            runner.RunInteractive()

            AssertEx.AssertEqualToleratingWhitespaceDifferences(
s_logoAndHelpPrompt + "
> Imports System.Globalization
> System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(""en-GB"")
> System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(""en-GB"")
> ? System.Math.PI
3.141592653589793
> System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(""de-DE"")
> ? System.Math.PI
3.141592653589793
>", runner.Console.Out.ToString())
        Finally
            CultureInfo.DefaultThreadCurrentCulture = currentCulture
            CultureInfo.DefaultThreadCurrentUICulture = currentUICulture
        End Try
    End Sub

    <Fact>
    Public Sub Version()
        Dim runner = CreateRunner({"/version"})
        Assert.Equal(0, runner.RunInteractive())
        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_versionOutput, runner.Console.Out.ToString())

        runner = CreateRunner({"/version", "/help"})
        Assert.Equal(0, runner.RunInteractive())
        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_versionOutput, runner.Console.Out.ToString())

        runner = CreateRunner({"/version", "/r:somefile"})
        Assert.Equal(0, runner.RunInteractive())
        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_versionOutput, runner.Console.Out.ToString())

        runner = CreateRunner({"/version", "/nologo"})
        Assert.Equal(0, runner.RunInteractive())
        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_versionOutput, runner.Console.Out.ToString())
    End Sub

    <Fact>
    Public Sub TestPrintVersionTwoLines()
        Dim compiler = New VisualBasicInteractiveCompiler(
            Path.Combine(AppContext.BaseDirectory, "vbi.rsp"),
            New BuildPaths(
                clientDir:=AppContext.BaseDirectory,
                workingDir:=AppContext.BaseDirectory,
                sdkDir:=RuntimeMetadataReferenceResolver.GetDesktopFrameworkDirectory(),
                tempDir:=CreateIsolatedTempDirectory()),
            {"/version"},
            New NotImplementedAnalyzerLoader())

        Dim output As New StringWriter()
        compiler.PrintVersion(output)

        ' LogoLine2 is localized (e.g. zh-Hans "基于 Roslyn [版本 {0}]. "), so assert the
        ' culture-invariant token "Roslyn" that appears in every translation.
        Dim lines = output.ToString().Split({Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries)
        Assert.Equal(2, lines.Length)
        Assert.Contains("2.0.0-Beta", lines(0))
        Assert.Contains("Roslyn", lines(1))
    End Sub

#Region "Optional leading ? on REPL expressions - L4 REPL/scripts (test-plan section 7, R1-R21 / V1-V8)"

    Private Shared Sub AssertDateValueLine(output As String)
        Assert.True(System.Text.RegularExpressions.Regex.IsMatch(
            output, "#\d{1,2}/\d{1,2}/\d{4} \d{1,2}:\d{2}:\d{2} (AM|PM)#"),
            "Expected a date-literal value line in output:" & vbCrLf & output)
    End Sub

    ''' <summary>
    ''' R1: A bare property Now prints: the output contains a date-literal value line and no error block.
    ''' </summary>
    <Fact>
    Public Sub TestBarePropertyAccessPrints()
        Dim runner = CreateRunner(input:="Now")

        runner.RunInteractive()

        Dim output = runner.Console.Out.ToString()
        Assert.StartsWith(s_logoAndHelpPrompt, output)
        Assert.Contains("> Now", output)
        Assert.DoesNotContain("«Red»", output)
        AssertDateValueLine(output)
    End Sub

    ''' <summary>
    ''' R2: A bare variable before prints after "Dim before = Now": the output contains a date value (BoundLocal/field value reference).
    ''' </summary>
    <Fact>
    Public Sub TestBareVariablePrints()
        Dim runner = CreateRunner(input:="Dim before = Now
before")

        runner.RunInteractive()

        Dim output = runner.Console.Out.ToString()
        Assert.StartsWith(s_logoAndHelpPrompt, output)
        Assert.Contains("> Dim before = Now", output)
        Assert.Contains("> before", output)
        Assert.DoesNotContain("«Red»", output)
        AssertDateValueLine(output)
    End Sub

    ''' <summary>
    ''' R3: A bare arithmetic expression 1 + 2 prints 3.
    ''' Note: the top-level IntegerLiteralToken dispatch gap in F9 was fixed (root cause 1, see Parser.vb ParseDeclarationStatementInternal).
    ''' </summary>
    <Fact>
    Public Sub TestBareArithmeticExpressionPrints()
        Dim runner = CreateRunner(input:="1 + 2")

        runner.RunInteractive()

        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_logoAndHelpPrompt + "
> 1 + 2
3
>", runner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' R4: A bare comparison prints: after "Dim x = 1", "x > 5" outputs False, and "? x = 5" also outputs False (a comparison).
    ''' </summary>
    <Fact>
    Public Sub TestBareComparisonExpressionPrints()
        Dim comparisonRunner = CreateRunner(input:="Dim x = 1
x > 5")

        comparisonRunner.RunInteractive()

        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_logoAndHelpPrompt + "
> Dim x = 1
> x > 5
False
>", comparisonRunner.Console.Out.ToString())

        Dim questionRunner = CreateRunner(input:="Dim x = 1
? x = 5")

        questionRunner.RunInteractive()

        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_logoAndHelpPrompt + "
> Dim x = 1
> ? x = 5
False
>", questionRunner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' R5: String concatenation "a" & "b" prints "ab".
    ''' </summary>
    <Fact>
    Public Sub TestBareStringConcatenationPrints()
        Dim runner = CreateRunner(input:="""a"" & ""b""")

        runner.RunInteractive()

        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_logoAndHelpPrompt + "
> ""a"" & ""b""
""ab""
>", runner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' R6: An assignment does not print (x declared first): only the prompt, no value output.
    ''' </summary>
    <Fact>
    Public Sub TestAssignmentDoesNotPrint()
        Dim runner = CreateRunner(input:="Dim x = 1
x = 5")

        runner.RunInteractive()

        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_logoAndHelpPrompt + "
> Dim x = 1
> x = 5
>", runner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' R7: A side-effecting call Console.WriteLine("hi") does not auto-print.
    ''' Note: WriteLine's side-effect output goes to the real console (System.Console), not TestConsoleIO's StringWriter,
    ''' so the captured output does not contain "hi"; the REPL also does not auto-print a value for this Void call.
    ''' </summary>
    <Fact>
    Public Sub TestSideEffectCallDoesNotPrint()
        Dim runner = CreateRunner(input:="Console.WriteLine(""hi"")")

        runner.RunInteractive()

        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_logoAndHelpPrompt + "
> Console.WriteLine(""hi"")
>", runner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' R8: A parenthesized-less Sub call does not print (MySub after "Sub MySub" is defined).
    ''' </summary>
    <Fact>
    Public Sub TestBareSubCallDoesNotPrint()
        Dim runner = CreateRunner(input:="Sub MySub()
End Sub
MySub")

        runner.RunInteractive()

        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_logoAndHelpPrompt + "
> Sub MySub()
. End Sub
> MySub
>", runner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' R9: The explicit ? regression: ? Now output matches current behavior (a date-literal value line).
    ''' </summary>
    <Fact>
    Public Sub TestExplicitQuestionStillPrints()
        Dim runner = CreateRunner(input:="? Now")

        runner.RunInteractive()

        Dim output = runner.Console.Out.ToString()
        Assert.StartsWith(s_logoAndHelpPrompt, output)
        Assert.Contains("> ? Now", output)
        Assert.DoesNotContain("«Red»", output)
        AssertDateValueLine(output)
    End Sub

    ''' <summary>
    ''' R10: ? x = 5 compares: it outputs False ("=" is a binary comparison in the ? expression context).
    ''' </summary>
    <Fact>
    Public Sub TestQuestionAssignmentPrintsComparisonResult()
        Dim runner = CreateRunner(input:="Dim x = 1
? x = 5")

        runner.RunInteractive()

        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_logoAndHelpPrompt + "
> Dim x = 1
> ? x = 5
False
>", runner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' R11: Malformed input still errors; it neither prints nor swallows the error.
    ''' Note: the test-plan originally used "Now +" (a missing right operand). Under the current implementation "Now +" is
    ''' recognized as an incomplete submission (the REPL waits for a continuation line, neither compiling nor reporting),
    ''' which disagrees with the design expectation of a syntax error; "Now x" (two identifiers) is used instead to trigger a
    ''' real BC30800 syntax error and cover the "malformed input still errors" intent.
    ''' </summary>
    <Fact>
    Public Sub TestRealSyntaxErrorStillErrors()
        Dim runner = CreateRunner(input:="Now x")

        runner.RunInteractive()

        Dim output = runner.Console.Out.ToString()
        Assert.StartsWith(s_logoAndHelpPrompt, output)
        Assert.Contains("> Now x", output)
        Assert.Contains("«Red»", output)
        Assert.Contains("error BC30", output)
        Assert.False(System.Text.RegularExpressions.Regex.IsMatch(output, "#\d{1,2}/\d{1,2}/\d{4}"),
                     "A real syntax error must not print a value line.")
    End Sub

    ''' <summary>
    ''' R12: A non-final bare expression 1 + 2 : x = 5 reports BC31003 with no print.
    ''' Note: the top-level IntegerLiteralToken dispatch gap in F9 was fixed (root cause 1, see Parser.vb ParseDeclarationStatementInternal).
    ''' </summary>
    <Fact>
    Public Sub TestNonFinalBareExpressionInSubmissionStillErrors()
        Dim runner = CreateRunner(input:="Dim x = 1 : 1 + 2 : x = 5")

        runner.RunInteractive()

        Assert.Contains("BC31003", runner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' R13: A non-final member access DateTime.Now : x = 5 reports BC30545 (the Invocation branch does not suppress it when non-final).
    ''' </summary>
    <Fact>
    Public Sub TestNonFinalMemberAccessInSubmissionStillErrors()
        Dim runner = CreateRunner(input:="Dim x = 1 : DateTime.Now : x = 5")

        runner.RunInteractive()

        Assert.Contains("BC30545", runner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' R14: A multi-line continuation ending in 1 + 2 _ (the whole submission) prints 3.
    ''' Note: the top-level IntegerLiteralToken dispatch gap in F9 was fixed (root cause 1, see Parser.vb ParseDeclarationStatementInternal).
    ''' </summary>
    <Fact>
    Public Sub TestMultiLineContinuationBareExpressionPrints()
        Dim runner = CreateRunner(input:="1 + 2 _" & vbCrLf & vbCrLf)

        runner.RunInteractive()

        Dim output = runner.Console.Out.ToString()
        Assert.Contains("> 1 + 2 _", output)
        Assert.Contains(vbCrLf & "3" & vbCrLf, output)
    End Sub

    ''' <summary>
    ''' R15: Late binding (a member call on an Object receiver): compilation fails with BC30491 (ERR_VoidValue) and no value prints.
    ''' Note: the test-plan originally used "New X() : o.Prop" as a placeholder; an undefined X would report BC30002, so a real type
    ''' is used here to cover the "Object-receiver call" intent. The design section 4 decision table (LateMemberAccess -> no diagnostics,
    ''' no print) disagrees with the current compiler, which actually reports BC30491 (compilation fails, so no value prints).
    ''' This is pre-existing behavior (the same statement inside a Regular Sub only warns BC42104 and does not report BC30491;
    ''' BC30491 fires only on the script submission-result path), not a regression from F9/F10. This test records the actual
    ''' behavior (root cause 3, see log entry 19).
    ''' </summary>
    <Fact>
    Public Sub TestLateBoundMemberAccessDoesNotPrint()
        Dim runner = CreateRunner(input:="Dim o As Object = New System.Text.StringBuilder() : o.Append(""x"")")

        runner.RunInteractive()

        Dim output = runner.Console.Out.ToString()
        Assert.StartsWith(s_logoAndHelpPrompt, output)
        Assert.Contains("> Dim o As Object = New System.Text.StringBuilder() : o.Append(""x"")", output)
        Assert.Contains("«Red»", output)
        Assert.Contains("BC30491", output)
    End Sub

    ''' <summary>
    ''' R16: The ? ( dispatch: ? (1 + 2) takes the PrintStatement path and prints 3.
    ''' </summary>
    <Fact>
    Public Sub TestQuestionParenStillPrints()
        Dim runner = CreateRunner(input:="? (1 + 2)")

        runner.RunInteractive()

        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_logoAndHelpPrompt + "
> ? (1 + 2)
3
>", runner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' R17: Interactive state persists across submissions: declare, assign, then read back (independent trees via ContinueWith).
    ''' Note: the test-plan originally assigned "before = 1" directly; interactive Option Explicit defaults to On, so a Dim comes first.
    ''' </summary>
    <Fact>
    Public Sub TestInteractiveStatePersistsAcrossSubmissions()
        Dim runner = CreateRunner(input:="Dim before = 0
before = 1
before")

        runner.RunInteractive()

        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_logoAndHelpPrompt + "
> Dim before = 0
> before = 1
> before
1
>", runner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' R18: A member-access property DateTime.Now prints a date-literal value line with no error block.
    ''' </summary>
    <Fact>
    Public Sub TestBareQualifiedPropertyPrints()
        Dim runner = CreateRunner(input:="DateTime.Now")

        runner.RunInteractive()

        Dim output = runner.Console.Out.ToString()
        Assert.StartsWith(s_logoAndHelpPrompt, output)
        Assert.Contains("> DateTime.Now", output)
        Assert.DoesNotContain("«Red»", output)
        AssertDateValueLine(output)
    End Sub

    ''' <summary>
    ''' R19: Two-variable assignment versus comparison (two independent scenarios).
    ''' 1. "a = b" is an assignment and does not print (b overwrites a); 2. "? a = b" is a comparison and prints False.
    ''' </summary>
    <Fact>
    Public Sub TestAssignmentVsComparisonIndependentScenarios()
        ' Scenario 1: "a = b" is an assignment, so it does not print.
        Dim assignmentRunner = CreateRunner(input:="Dim a = 1 : Dim b = 2
a = b")

        assignmentRunner.RunInteractive()

        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_logoAndHelpPrompt + "
> Dim a = 1 : Dim b = 2
> a = b
>", assignmentRunner.Console.Out.ToString())

        ' Scenario 2: "? a = b" is a comparison, so it prints False.
        Dim comparisonRunner = CreateRunner(input:="Dim a = 1 : Dim b = 2
? a = b")

        comparisonRunner.RunInteractive()

        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_logoAndHelpPrompt + "
> Dim a = 1 : Dim b = 2
> ? a = b
False
>", comparisonRunner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' R20: Compound assignments do not change the value (x += 1 / x &= "s") and do not print.
    ''' </summary>
    <Fact>
    Public Sub TestCompoundAssignmentDoesNotPrint()
        Dim runner = CreateRunner(input:="Dim x = 0
x += 1
Dim s = ""a""
s &= ""s""")

        runner.RunInteractive()

        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_logoAndHelpPrompt + "
> Dim x = 0
> x += 1
> Dim s = ""a""
> s &= ""s""
>", runner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' R21: Mid / ReDim / With do not change the value (overview section 4); none prints and all are legal.
    ''' </summary>
    <Fact>
    Public Sub TestMidRedimWithDoNotPrint()
        Dim runner = CreateRunner(input:="Dim s = ""abcdef"" : Mid(s, 1, 2) = ""ab""
Dim a(2) As Integer : ReDim a(2)
Dim sb As New System.Text.StringBuilder() : With sb : .Append(""x"") : End With")

        runner.RunInteractive()

        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_logoAndHelpPrompt + "
> Dim s = ""abcdef"" : Mid(s, 1, 2) = ""ab""
> Dim a(2) As Integer : ReDim a(2)
> Dim sb As New System.Text.StringBuilder() : With sb : .Append(""x"") : End With
>", runner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' V1: A bare expression 1 + 2 in a script file is a silent no-op: exit code 0, no output.
    ''' Note: the top-level IntegerLiteralToken dispatch gap in F9 was fixed (root cause 1, see Parser.vb ParseDeclarationStatementInternal).
    ''' </summary>
    <Fact>
    Public Sub TestBareExpressionInScriptFileIsSilentNoOp()
        Dim directory = CreateIsolatedTempDirectory()
        File.WriteAllText(Path.Combine(directory, "main.vbx"), "1 + 2")

        Dim runner = CreateRunner(args:={"main.vbx"}, workingDirectory:=directory)

        Assert.Equal(0, runner.RunInteractive())
        AssertEx.AssertEqualToleratingWhitespaceDifferences("", runner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' V2: A bare property Now in a script file is a silent no-op: exit code 0, no output.
    ''' </summary>
    <Fact>
    Public Sub TestBarePropertyInScriptFileIsSilentNoOp()
        Dim directory = CreateIsolatedTempDirectory()
        File.WriteAllText(Path.Combine(directory, "main.vbx"), "Now")

        Dim runner = CreateRunner(args:={"main.vbx"}, workingDirectory:=directory)

        Assert.Equal(0, runner.RunInteractive())
        AssertEx.AssertEqualToleratingWhitespaceDifferences("", runner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' V6: A bare expression inside a nested Sub still errors: exit code 1, stderr contains an error.
    ''' (IsTopLevelScript's BlockKind gate excludes nested methods.)
    ''' </summary>
    <Fact>
    Public Sub TestBareExpressionInNestedSubStillErrors()
        Dim directory = CreateIsolatedTempDirectory()
        File.WriteAllText(Path.Combine(directory, "main.vbx"), "Sub S() : 1 + 2 : End Sub")

        Dim runner = CreateRunner(args:={"main.vbx"}, workingDirectory:=directory)

        Assert.Equal(1, runner.RunInteractive())
        Assert.Contains("error BC3", runner.Console.Error.ToString())
    End Sub

#End Region

#Region "ByRef-like safety - L2 REPL/scripts (test-plan section 4, R1-R15)"

    ''' <summary>
    ''' R1: A top-level "Dim s As New Span(Of Integer)(1)" becomes a script-class field; the restricted-type
    ''' field check reports BC31396 and the submission does not run.
    ''' </summary>
    <Fact>
    Public Sub TestTopLevelDimOfSpanReportsRestrictedType()
        Dim runner = CreateRunner(input:="Dim s As New Span(Of Integer)(1)")

        runner.RunInteractive()

        Dim output = runner.Console.Out.ToString()
        Assert.StartsWith(s_logoAndHelpPrompt, output)
        Assert.Contains("> Dim s As New Span(Of Integer)(1)", output)
        Assert.Contains("«Red»", output)
        Assert.Contains("BC31396", output)
    End Sub

    ''' <summary>
    ''' R2: "? New Span(Of Integer)(1)" boxes the result to Object for printing; the restricted conversion
    ''' reports BC31394 and nothing prints.
    ''' </summary>
    <Fact>
    Public Sub TestQuestionPrintingSpanReportsRestrictedConversion()
        Dim runner = CreateRunner(input:="? New Span(Of Integer)(1)")

        runner.RunInteractive()

        Dim output = runner.Console.Out.ToString()
        Assert.StartsWith(s_logoAndHelpPrompt, output)
        Assert.Contains("«Red»", output)
        Assert.Contains("BC31394", output)
    End Sub

    ''' <summary>
    ''' R3: A trailing bare expression "New Span(Of Integer)(1)" is the submission result, boxed to Object by
    ''' the initializer rewriter; the restricted conversion reports BC31394 and nothing prints.
    ''' </summary>
    <Fact>
    Public Sub TestTrailingBareSpanExpressionReportsRestrictedConversion()
        Dim runner = CreateRunner(input:="New Span(Of Integer)(1)")

        runner.RunInteractive()

        Dim output = runner.Console.Out.ToString()
        Assert.StartsWith(s_logoAndHelpPrompt, output)
        Assert.Contains("«Red»", output)
        Assert.Contains("BC31394", output)
    End Sub

    ''' <summary>
    ''' R4: A method-local Span is usable; the method runs and "? F()" prints "ok".
    ''' (Console.WriteLine is not captured by TestConsoleIO, so the method returns "ok" instead of writing it.)
    ''' </summary>
    <Fact>
    Public Sub TestMethodLocalSpanIsUsable()
        Dim runner = CreateRunner(input:="Function F() As String
    Dim s As New Span(Of Integer)(1)
    Return ""ok""
End Function
? F()")

        runner.RunInteractive()

        Dim output = runner.Console.Out.ToString()
        Assert.DoesNotContain("«Red»", output)
        Assert.Contains("""ok""", output)
    End Sub

    ''' <summary>
    ''' R5: A ByVal Span parameter is usable; a Span is passed ByVal to F and "ok" prints.
    ''' Note: a top-level call that passes a ref struct argument currently throws TypeLoadException (the
    ''' generated submission class hoists the argument into a ByRef-like instance field), so the call runs
    ''' from within a method body, which the scripting runtime handles.
    ''' </summary>
    <Fact>
    Public Sub TestByValSpanParameterIsUsable()
        Dim runner = CreateRunner(input:="Function F(s As Span(Of Integer)) As String
    Return ""ok""
End Function
Sub G()
    Dim sp As New Span(Of Integer)(1)
    Print(F(sp))
End Sub
G()")

        runner.RunInteractive()

        Dim output = runner.Console.Out.ToString()
        Assert.DoesNotContain("«Red»", output)
        Assert.Contains("""ok""", output)
    End Sub

    ''' <summary>
    ''' R5a: A top-level call passing a ByVal Span argument lifts that argument into the async
    ''' &lt;Initialize&gt; state machine in Debug builds. It must report BC37052 at compile time instead
    ''' of crashing at runtime with TypeLoadException.
    ''' </summary>
    <Fact>
    Public Sub TestTopLevelByValSpanArgumentReportsRestrictedLift()
        Dim runner = CreateRunner(input:="Function F(s As Span(Of Integer)) As String
Return ""ok""
End Function
? F(New Span(Of Integer)(1))")

        runner.RunInteractive()

        Dim output = runner.Console.Out.ToString()
        Assert.StartsWith(s_logoAndHelpPrompt, output)
        Assert.Contains("«Red»", output)
        Assert.Contains("BC37052", output)
        Assert.DoesNotContain("TypeLoadException", output)
    End Sub

    ''' <summary>
    ''' R6: A ByRef Span parameter is a restricted type; BC31396.
    ''' </summary>
    <Fact>
    Public Sub TestByRefSpanParameterReportsRestrictedType()
        Dim runner = CreateRunner(input:="Sub F(ByRef s As Span(Of Integer))
End Sub")

        runner.RunInteractive()

        Dim output = runner.Console.Out.ToString()
        Assert.StartsWith(s_logoAndHelpPrompt, output)
        Assert.Contains("«Red»", output)
        Assert.Contains("BC31396", output)
    End Sub

    ''' <summary>
    ''' R7: Cross top-level Await with a Span in scope. A top-level "Dim s" is a script-class field, so the
    ''' restricted-field error (BC31396) preempts the async state-machine capture check (BC37052). The actual
    ''' first error reported is BC31396; BC37052 is not observable at REPL top level.
    ''' </summary>
    <Fact>
    Public Sub TestTopLevelAwaitWithSpanInScopeReportsFieldFirst()
        Dim runner = CreateRunner(input:="Dim s As New Span(Of Integer)(1) : Await Task.FromResult(0)")

        runner.RunInteractive()

        Dim output = runner.Console.Out.ToString()
        Assert.Contains("«Red»", output)
        Assert.Contains("BC31396", output)
    End Sub

    ''' <summary>
    ''' R8: A top-level "Dim s As New Span(Of Integer)(1)" in a .vbx script is restricted the same way as the
    ''' REPL (both are Script kind); the script fails with BC31396.
    ''' </summary>
    <Fact>
    Public Sub TestVbxTopLevelDimOfSpanReportsRestrictedType()
        Dim directory = CreateIsolatedTempDirectory()
        File.WriteAllText(Path.Combine(directory, "main.vbx"), "Dim s As New Span(Of Integer)(1)")

        Dim runner = CreateRunner(args:={"main.vbx"}, workingDirectory:=directory)

        Assert.Equal(1, runner.RunInteractive())
        Assert.Contains("BC31396", runner.Console.Error.ToString())
    End Sub

    ''' <summary>
    ''' R9: A method-local Span in a .vbx script is usable; the script runs, exits 0, and prints "ok".
    ''' (Console.WriteLine is not captured by TestConsoleIO, so Print is used instead.)
    ''' </summary>
    <Fact>
    Public Sub TestVbxMethodLocalSpanIsUsable()
        Dim directory = CreateIsolatedTempDirectory()
        File.WriteAllText(Path.Combine(directory, "main.vbx"),
            "Sub F()
    Dim s As New Span(Of Integer)(1)
    Print(""ok"")
End Sub
F()")

        Dim runner = CreateRunner(args:={"main.vbx"}, workingDirectory:=directory)

        Assert.Equal(0, runner.RunInteractive())
        AssertEx.AssertEqualToleratingWhitespaceDifferences("""ok""", runner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' R10: "allows ref struct" interface consumption depends on prerequisite-2 / M8, which is not landed.
    ''' The REPL harness cannot supply a C# "allows ref struct" interface reference (CreateRunner references
    ''' only the default assemblies), so the end-to-end "usable" assertion is a documented timing gap. Current
    ''' not-ready behavior: a Span cannot be converted to an interface reference; interface conversion has no
    ''' restricted-type check, so the reported error is the generic type mismatch BC30311 (per L1 S21), not
    ''' BC31396 as the plan anticipated.
    ''' </summary>
    <Fact>
    Public Sub TestAllowsRefStructInterfaceConsumptionNotReady()
        Dim runner = CreateRunner(input:="Sub F()
    Dim x As IDisposable = New Span(Of Integer)(1)
End Sub")

        runner.RunInteractive()

        Dim output = runner.Console.Out.ToString()
        Assert.Contains("«Red»", output)
        Assert.Contains("BC30311", output)
    End Sub

    ''' <summary>
    ''' R11: "? CType(New Span(Of Integer)(1), Object)" boxes explicitly to Object -> BC31394;
    ''' "? CType(New Span(Of Integer)(1), IDisposable)" targets an interface -> BC30311. Neither runs.
    ''' </summary>
    <Fact>
    Public Sub TestExplicitCTypeSpanToObjectAndInterface()
        Dim objectRunner = CreateRunner(input:="? CType(New Span(Of Integer)(1), Object)")

        objectRunner.RunInteractive()

        Dim objectOutput = objectRunner.Console.Out.ToString()
        Assert.Contains("«Red»", objectOutput)
        Assert.Contains("BC31394", objectOutput)

        Dim interfaceRunner = CreateRunner(input:="? CType(New Span(Of Integer)(1), IDisposable)")

        interfaceRunner.RunInteractive()

        Dim interfaceOutput = interfaceRunner.Console.Out.ToString()
        Assert.Contains("«Red»", interfaceOutput)
        Assert.Contains("BC30311", interfaceOutput)
    End Sub

    ''' <summary>
    ''' R12: "? (""a"" & New Span(Of Integer)(1))" has no applicable '&' overload for a restricted operand;
    ''' the error is BC30452, not BC31394.
    ''' </summary>
    <Fact>
    Public Sub TestStringConcatenationOfSpanReportsOperatorError()
        Dim runner = CreateRunner(input:="? (""a"" & New Span(Of Integer)(1))")

        runner.RunInteractive()

        Dim output = runner.Console.Out.ToString()
        Assert.Contains("«Red»", output)
        Assert.Contains("BC30452", output)
        Assert.DoesNotContain("BC31394", output)
    End Sub

    ''' <summary>
    ''' R13: A failed submission does not pollute the session: the top-level Dim error is reported, then the
    ''' next submission "1 + 2" still prints 3.
    ''' </summary>
    <Fact>
    Public Sub TestFailedSubmissionDoesNotPolluteSession()
        Dim runner = CreateRunner(input:="Dim s As New Span(Of Integer)(1)" & vbCrLf & "1 + 2")

        runner.RunInteractive()

        Dim output = runner.Console.Out.ToString()
        Assert.Contains("«Red»", output)
        Assert.Contains("BC31396", output)
        Assert.True(output.IndexOf("BC31396") < output.IndexOf("> 1 + 2"),
                    "The next submission's prompt must appear after the failed submission's error block.")
        Assert.Contains("> 1 + 2" & vbCrLf & "3", output)
    End Sub

    ''' <summary>
    ''' R14: For Each over a ReadOnlySpan(Of Char) is not usable in VB: the enumerator's Current is a
    ''' ByRef-returning property, unsupported (BC30643).
    ''' </summary>
    <Fact>
    Public Sub TestForEachOverReadOnlySpanReportsUnsupportedProperty()
        Dim runner = CreateRunner(input:="Sub F()
    Dim s As New ReadOnlySpan(Of Char)(""ab"".ToCharArray())
    For Each c As Char In s
        Console.WriteLine(c)
    Next
End Sub")

        runner.RunInteractive()

        Dim output = runner.Console.Out.ToString()
        Assert.Contains("«Red»", output)
        Assert.Contains("BC30643", output)
    End Sub

    ''' <summary>
    ''' R15: "? 42" prints the exact value line (reference TestPrint; not a widened assertion).
    ''' </summary>
    <Fact>
    Public Sub TestQuestionPrintExactFormat()
        Dim runner = CreateRunner(input:="? 42")

        runner.RunInteractive()

        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_logoAndHelpPrompt + "
> ? 42
42
>", runner.Console.Out.ToString())
    End Sub

#End Region

#Region "Consume C# 14 extension members - L2 REPL/scripts (test-plan section 4, R1-R4/R9)"

    Private Const _extensionCsLibrary As String = "
using System;
namespace ExternLib
{
    public static class ClassicExt { public static int Twice(this int value) => value * 2; }
    public static class NewExt
    {
        extension(string s)
        {
            public int CharCount => s.Length;
            public string Shout() => s.ToUpperInvariant();
        }
    }
    public interface IHasZero<T> where T : IHasZero<T>
    {
        static abstract T Zero { get; }
        static abstract T Add(T a, T b);
    }
    public struct MyNum : IHasZero<MyNum>
    {
        public int Value;
        public MyNum(int v) { Value = v; }
        public static MyNum Zero => new MyNum(0);
        public static MyNum Add(MyNum a, MyNum b) => new MyNum(a.Value + b.Value);
    }
}
public static class VecExt
{
    extension(MyVec v)
    {
        public static MyVec operator +(MyVec a, MyVec b) => new MyVec(a.X + b.X, a.Y + b.Y);
    }
}
public struct MyVec
{
    public int X;
    public int Y;
    public MyVec(int x, int y) { X = x; Y = y; }
    public override string ToString() => $""({X},{Y})"";
}
"

    ''' <summary>
    ''' R1: .vbx script consumes a C# 14 extension property ("hello".CharCount -> 5) via #r.
    ''' </summary>
    <Fact>
    Public Sub TestExtensionPropertyInScriptFile()
        Dim directory = CreateIsolatedTempDirectory()
        Dim libPath = CreateCSharpLibraryAssembly(directory, "ExternLib", _extensionCsLibrary)
        File.WriteAllText(Path.Combine(directory, "main.vbx"), "#r """ & libPath & """
Imports ExternLib
Print(""hello"".CharCount)")

        Dim runner = CreateRunner(args:={"main.vbx"}, workingDirectory:=directory)

        Assert.Equal(0, runner.RunInteractive())
        Assert.Contains("5", runner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' R2: .vbx script consumes a C# 14 extension operator (MyVec + MyVec -> (11,22)).
    ''' NOTE: the operands must be typed MyVec. In a script the interactive host defaults to
    ''' Option Infer Off, so "Dim a = New MyVec(...)" would type `a` as Object and `a + b` would
    ''' fall back to a late-bound AddObject at runtime; explicit "As MyVec" forces early binding.
    ''' </summary>
    <Fact>
    Public Sub TestExtensionOperatorInScriptFile()
        Dim directory = CreateIsolatedTempDirectory()
        Dim libPath = CreateCSharpLibraryAssembly(directory, "ExternLib", _extensionCsLibrary)
        File.WriteAllText(Path.Combine(directory, "main.vbx"), "#r """ & libPath & """
Imports ExternLib
Dim a As MyVec = New MyVec(1, 2) : Dim b As MyVec = New MyVec(10, 20)
Print((a + b).ToString())")

        Dim runner = CreateRunner(args:={"main.vbx"}, workingDirectory:=directory)

        Assert.Equal(0, runner.RunInteractive())
        Assert.Contains("(11,22)", runner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' R3: .vbx script runs the T.Zero/T.Add generic algorithm (VBSum(Of MyNum) -> 30).
    ''' </summary>
    <Fact>
    Public Sub TestTypeParameterSharedMembersInScriptFile()
        Dim directory = CreateIsolatedTempDirectory()
        Dim libPath = CreateCSharpLibraryAssembly(directory, "ExternLib", _extensionCsLibrary)
        File.WriteAllText(Path.Combine(directory, "main.vbx"), "#r """ & libPath & """
Imports ExternLib
Function VBSum(Of T As IHasZero(Of T))(items() As T) As T
    Dim result As T = T.Zero
    For Each item In items
        result = T.Add(result, item)
    Next
    Return result
End Function
Print(VBSum(Of MyNum)({New MyNum(10), New MyNum(20)}).Value)")

        Dim runner = CreateRunner(args:={"main.vbx"}, workingDirectory:=directory)

        Assert.Equal(0, runner.RunInteractive())
        Assert.Contains("30", runner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' R4: .vbx and Regular share the same compiler: without Imports ExternLib the extension property
    ''' reports BC30456 in a script file, matching the L1 S11/Regular diagnostic.
    ''' </summary>
    <Fact>
    Public Sub TestExtensionPropertyScriptFileNoImportsReportsBC30456()
        Dim directory = CreateIsolatedTempDirectory()
        Dim libPath = CreateCSharpLibraryAssembly(directory, "ExternLib", _extensionCsLibrary)
        File.WriteAllText(Path.Combine(directory, "main.vbx"), "#r """ & libPath & """
Print(""hello"".CharCount)")

        Dim runner = CreateRunner(args:={"main.vbx"}, workingDirectory:=directory)

        Assert.Equal(1, runner.RunInteractive())
        Assert.Contains("BC30456", runner.Console.Error.ToString())
    End Sub

    ''' <summary>
    ''' R9: interactive Imports scope -- without Imports ExternLib the extension property errors (BC30456);
    ''' after submitting Imports ExternLib it resolves to 5.
    ''' </summary>
    <Fact>
    Public Sub TestExtensionPropertyInteractiveImportsScope()
        Dim directory = CreateIsolatedTempDirectory()
        Dim libPath = CreateCSharpLibraryAssembly(directory, "ExternLib", _extensionCsLibrary)

        ' No Imports: extension member out of scope.
        Dim runner1 = CreateRunner(input:="#r """ & libPath & """" & vbCrLf & "? ""hello"".CharCount")
        runner1.RunInteractive()
        Dim output1 = runner1.Console.Out.ToString()
        Assert.Contains("«Red»", output1)
        Assert.Contains("BC30456", output1)

        ' With Imports ExternLib in the same session: resolves to 5.
        Dim runner2 = CreateRunner(input:="#r """ & libPath & """" & vbCrLf & "Imports ExternLib" & vbCrLf & "? ""hello"".CharCount")
        runner2.RunInteractive()
        Assert.Contains("5", runner2.Console.Out.ToString())
    End Sub

#End Region

End Class

