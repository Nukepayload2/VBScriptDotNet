' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Globalization
Imports System.IO
Imports System.Reflection
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting
Imports My.Resources
Imports Xunit

Public Class CommandLineRunnerTests

    Private Shared ReadOnly s_compilerVersion As String =
        CommonCompiler.GetProductVersion(GetType(VisualBasicInteractiveCompiler))

    Private Shared ReadOnly s_interactiveCompilerVersion As String =
        GetType(VisualBasicInteractiveCompiler).Assembly.
            GetCustomAttribute(Of AssemblyInformationalVersionAttribute).InformationalVersion

    Private Shared ReadOnly s_roslynVersion As String = FormatVersionWithoutRevision(
        GetType(VisualBasicCompiler).Assembly.GetName.Version)

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
        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_compilerVersion, runner.Console.Out.ToString())

        runner = CreateRunner({"/version", "/help"})
        Assert.Equal(0, runner.RunInteractive())
        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_compilerVersion, runner.Console.Out.ToString())

        runner = CreateRunner({"/version", "/r:somefile"})
        Assert.Equal(0, runner.RunInteractive())
        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_compilerVersion, runner.Console.Out.ToString())

        runner = CreateRunner({"/version", "/nologo"})
        Assert.Equal(0, runner.RunInteractive())
        AssertEx.AssertEqualToleratingWhitespaceDifferences(s_compilerVersion, runner.Console.Out.ToString())
    End Sub

End Class

