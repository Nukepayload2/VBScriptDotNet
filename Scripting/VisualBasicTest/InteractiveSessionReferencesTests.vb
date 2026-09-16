' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.IO
Imports System.Linq
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Xunit

Public Class InteractiveSessionReferencesTests

    Private Shared ReadOnly s_options As ScriptOptions = ScriptModeConformance.DefaultOptions

    Private Shared Function CreateIsolatedTempDirectory() As String
        Dim directory = Path.Combine(AppContext.BaseDirectory, "TestTemp", Guid.NewGuid().ToString("N"))
        System.IO.Directory.CreateDirectory(directory)
        Return directory
    End Function

    Private Shared Function CreateLibraryAssembly(directory As String, assemblyName As String, source As String, Optional references As IEnumerable(Of MetadataReference) = Nothing) As String
        Dim assemblyPath = Path.Combine(directory, assemblyName + ".dll")
        Dim syntaxTree = SyntaxFactory.ParseSyntaxTree(source)
        Dim metadataReferences = New List(Of MetadataReference) From {
            MetadataReference.CreateFromFile(GetType(Object).Assembly.Location),
            MetadataReference.CreateFromFile(GetType(Strings).Assembly.Location)
        }

        If references IsNot Nothing Then
            metadataReferences.AddRange(references)
        End If

        Dim compilation = VisualBasicCompilation.Create(
            assemblyName,
            {syntaxTree},
            metadataReferences,
            New VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, rootNamespace:=""))

        Dim result = compilation.Emit(assemblyPath)
        Assert.True(result.Success, String.Join(Environment.NewLine, result.Diagnostics))
        Return assemblyPath
    End Function

    <Fact>
    Public Async Function ReferenceDirective_IsVisibleInSubsequentSubmissions() As Task
        Dim directory = CreateIsolatedTempDirectory()
        Dim libraryPath = CreateLibraryAssembly(directory, "InteractiveReferenceLibrary", "
Public Class C
    Public ReadOnly X As Integer = 1
End Class")

        Dim script = VisualBasicScript.Create("
#r """ & libraryPath & """
Function F(c As C) As Integer
    Return c.X
End Function").ContinueWith("? F(New C())")

        Assert.DoesNotContain(script.GetCompilation().GetDiagnostics(), Function(d) d.Severity = DiagnosticSeverity.Error)
        Assert.Equal(1, Await script.EvaluateAsync())
    End Function

    <Fact>
    Public Async Function ReferenceDirective_DuplicateReferenceIsAllowed() As Task
        Dim directory = CreateIsolatedTempDirectory()
        Dim libraryPath = CreateLibraryAssembly(directory, "InteractiveDuplicateReferenceLibrary", "
Public Class C
    Public ReadOnly X As Integer = 2
End Class")

        Dim script = VisualBasicScript.Create("
#r """ & libraryPath & """
#r """ & libraryPath & """
? New C().X")

        Assert.DoesNotContain(script.GetCompilation().GetDiagnostics(), Function(d) d.Severity = DiagnosticSeverity.Error)
        Assert.Equal(2, Await script.EvaluateAsync())
    End Function

    <Fact>
    Public Sub ReferenceDirective_MissingReferenceReportsDiagnostic()
        Dim directory = CreateIsolatedTempDirectory()
        Dim missingPath = Path.Combine(directory, "MissingInteractiveReference.dll")
        Dim diagnostics = VisualBasicScript.Create("#r """ & missingPath & """").GetCompilation().GetDiagnostics()

        Assert.Contains(diagnostics, Function(d) d.Id = "BC2017" AndAlso d.Severity = DiagnosticSeverity.Error)
    End Sub

    <Fact>
    Public Async Function ReferenceDirective_ResolvesDependencyFromReferencedAssemblyDirectory() As Task
        Dim directory = CreateIsolatedTempDirectory()
        Dim dependencyPath = CreateLibraryAssembly(directory, "InteractiveReferenceDependency", "
Public Class D
    Public Shared ReadOnly Y As Integer = 3
End Class")
        Dim libraryPath = CreateLibraryAssembly(directory, "InteractiveReferenceWithDependency", "
Public Class C
    Public ReadOnly X As Integer = D.Y
End Class", {MetadataReference.CreateFromFile(dependencyPath)})

        Dim script = VisualBasicScript.Create("
#r """ & libraryPath & """
? New C().X")

        Assert.DoesNotContain(script.GetCompilation().GetDiagnostics(), Function(d) d.Severity = DiagnosticSeverity.Error)
        Assert.Equal(3, Await script.EvaluateAsync())
    End Function

    ' ---- U9 #7 and #8 (design-detailed.md §U9; C# baseline CSharpTest\InteractiveSessionTests.cs:1233,1251,1271,
    '      1301 and CSharpTest\InteractiveSessionReferencesTests.cs:26,56,110) ----
    '
    ' None of the cells below writes a file, so none of them uses CreateIsolatedTempDirectory / CreateLibraryAssembly
    ' above: the relative reference cases point at assemblies that are already on disk next to the test assembly (the
    ' framework directory and AppContext.BaseDirectory), and the library resolution cases use libraries emitted to
    ' memory whose metadata references carry a virtual but *real directory* file path, which is the only thing
    ' RuntimeMetadataReferenceResolver.ResolveMissingAssembly needs in order to probe
    ' (RuntimeMetadataReferenceResolver.cs:122-135).

    ''' <summary>
    ''' U9 #7 cell one (C# <c>ReferenceDirective_RelativeToBaseParent</c>, IS:1233, issue 15860): a '#r' path is
    ''' resolved against the directory of the script that contains it, so <c>..\lib.dll</c> from a script one level
    ''' down reaches the real framework assembly. The script never runs the reference for its own sake: it asks for a
    ''' type that only that assembly declares, so a resolution that landed anywhere else would be BC2017 or BC30002
    ''' rather than a name. The C# baseline asserts only "no diagnostics"; the case here pins the resolved path as
    ''' well.
    ''' </summary>
    <Fact>
    Public Async Function ReferenceDirective_RelativeToBaseParent() As Task
        Dim libraryPath = GetType(System.Xml.Linq.XDocument).Assembly.Location
        Dim libraryDirectory = Path.GetDirectoryName(libraryPath)
        Dim libraryName = Path.GetFileName(libraryPath)
        Dim scriptPath = Path.Combine(libraryDirectory, "inMemoryScriptDirectory", "main.vbx")

        Dim script = VisualBasicScript.Create(
            "#r """ & Path.Combine("..", libraryName) & """" & vbCrLf &
            "? GetType(System.Xml.Linq.XDocument).Name", s_options.WithFilePath(scriptPath))

        Dim diagnostics = script.GetCompilation().GetDiagnostics()
        Assert.DoesNotContain(diagnostics, Function(d) d.Severity = DiagnosticSeverity.Error)
        Assert.Contains(libraryPath, script.GetCompilation().References.OfType(Of PortableExecutableReference)().Select(Function(r) r.FilePath))

        Dim state = Await script.RunAsync()
        Assert.Equal("XDocument", state.ReturnValue)
    End Function

    ''' <summary>
    ''' U9 #7 cell two (C# <c>ReferenceDirective_RelativeToBaseRoot</c>, IS:1251): the same library named by a rooted
    ''' path is resolved regardless of where the script lives. The path is derived from the loaded assembly rather
    ''' than written down, so no machine path is baked into the test.
    ''' </summary>
    <Fact>
    Public Async Function ReferenceDirective_RelativeToBaseRoot() As Task
        Dim libraryPath = GetType(System.Xml.Linq.XDocument).Assembly.Location
        Dim root = Path.GetPathRoot(libraryPath)
        Dim rooted = root & libraryPath.Substring(root.Length)

        Dim script = VisualBasicScript.Create(
            "#r """ & rooted & """" & vbCrLf &
            "? GetType(System.Xml.Linq.XDocument).Name",
            s_options.WithFilePath(Path.Combine(AppContext.BaseDirectory, "main.vbx")))

        Dim diagnostics = script.GetCompilation().GetDiagnostics()
        Assert.DoesNotContain(diagnostics, Function(d) d.Severity = DiagnosticSeverity.Error)
        Assert.Contains(rooted, script.GetCompilation().References.OfType(Of PortableExecutableReference)().Select(Function(r) r.FilePath))

        Dim state = Await script.RunAsync()
        Assert.Equal("XDocument", state.ReturnValue)
    End Function

    ''' <summary>
    ''' U9 #7 cell three (C# <c>ExtensionPriority1</c>/<c>ExtensionPriority2</c>, IS:1271,1301): the extension ladder a
    ''' missing dependency is probed with, in order. The C# baseline builds the same library identity under two
    ''' extensions in a temp directory and reads which one won; that shape needs the two files on disk
    ''' (<c>RuntimeMetadataReferenceResolver.cs:130</c> File.Exists) and the no side effect rule of test-plan §2
    ''' forbids writing them, so the cell pins the ladder the probe iterates
    ''' (<c>RuntimeMetadataReferenceResolver.cs:44</c>, consumed at <c>:127</c> and by
    ''' <c>InteractiveAssemblyLoader.cs:467</c>) - <c>.dll</c> before <c>.exe</c>. It fails if the order is swapped or
    ''' an extension is dropped from the front.
    ''' </summary>
    <Fact>
    Public Sub ExtensionPriority_DllIsProbedBeforeExe()
        Assert.Equal(New String() {".dll", ".exe"}, RuntimeMetadataReferenceResolver.AssemblyExtensions.ToArray())
    End Sub

    ''' <summary>
    ''' U9 #7 cell four: '.winmd' is *not* part of the ladder, even though the C# baseline's shape is written as if it
    ''' competed (the two cases are named after the exe/dll/winmd triple, IS:1271,1301, and the winmd file is created
    ''' and then never selected). Both languages share this list, so the VB reading is the same and the cell records
    ''' that no Windows Runtime probe happens for a script reference.
    ''' </summary>
    <Fact>
    Public Sub ExtensionPriority_WinmdIsNotProbed()
        Assert.DoesNotContain(".winmd", RuntimeMetadataReferenceResolver.AssemblyExtensions)
        Assert.Equal(2, RuntimeMetadataReferenceResolver.AssemblyExtensions.Length)
    End Sub

    ''' <summary>
    ''' U9 #8 cell one (C# <c>LibraryReference_NetStandard20</c>, ISR:26, dotnet/try issue 345): a dependency of a
    ''' referenced library that is not itself referenced by the script is resolved from the *directory of the
    ''' requesting library*, and the script still binds. The library is emitted to memory and its reference carries a
    ''' virtual path inside AppContext.BaseDirectory, where the real Microsoft.CodeAnalysis.VisualBasic.dll sits, so
    ''' ResolveMissingAssembly finds it there. A run that skipped the probe would leave a MissingAssemblySymbol and the
    ''' assertion on the symbol type would fail.
    ''' </summary>
    <Fact>
    Public Sub LibraryReference_DependencyIsResolvedFromTheRequestingLibrarysDirectory()
        Dim dependencyPath = Path.Combine(AppContext.BaseDirectory, "Microsoft.CodeAnalysis.VisualBasic.dll")
        Assert.True(File.Exists(dependencyPath), "the fixture expects this assembly next to the test assembly")

        Dim library = CreateInMemoryLibrary(
            "U9HolderA",
            "Public Class HolderA" & vbCrLf &
            "    Public Shared Function Compile() As Microsoft.CodeAnalysis.VisualBasic.VisualBasicCompilation" & vbCrLf &
            "        Return Nothing" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Class",
            {MetadataReference.CreateFromFile(dependencyPath)},
            Path.Combine(AppContext.BaseDirectory, "U9HolderA.dll"))

        Dim script = VisualBasicScript.Create(
            "Function Uses(h As HolderA) As Object" & vbCrLf &
            "    Return h" & vbCrLf &
            "End Function", ScriptOptions.Default.AddReferences(library))

        Dim compilation = script.GetCompilation()
        Assert.DoesNotContain(compilation.GetDiagnostics(), Function(d) d.Severity = DiagnosticSeverity.Error)

        Dim dependency = DependencyOf(compilation, "U9HolderA", "Microsoft.CodeAnalysis.VisualBasic")
        Assert.NotEqual("MissingAssemblySymbol", dependency.GetType().Name)
        Assert.Equal("Microsoft.CodeAnalysis.VisualBasic", dependency.Name)
    End Sub

    ''' <summary>
    ''' U9 #8 cell two (C# <c>LibraryReference_MissingDependency_MultipleResolveAttempts</c>, ISR:56): more than one
    ''' requesting library, each with its own dependency, has to be probed on its own - the C# baseline is a Theory
    ''' over both orders of the two '#r' directives. The Theory below keeps the order as the variable and asserts that
    ''' both dependencies resolve for both libraries in either order; the C# text reaches the same property with two
    ''' temp directories, which is what the no side effect rule rules out here (both virtual paths therefore live in
    ''' AppContext.BaseDirectory).
    ''' </summary>
    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Sub LibraryReference_MultipleDependenciesResolveIndependently(swapReferences As Boolean)
        Dim visualBasicPath = Path.Combine(AppContext.BaseDirectory, "Microsoft.CodeAnalysis.VisualBasic.dll")
        Dim csharpPath = Path.Combine(AppContext.BaseDirectory, "Microsoft.CodeAnalysis.CSharp.dll")
        Assert.True(File.Exists(visualBasicPath) AndAlso File.Exists(csharpPath))

        Dim visualBasicLibrary = CreateInMemoryLibrary(
            "U9HolderB",
            "Public Class HolderB" & vbCrLf &
            "    Public Shared Function Compile() As Microsoft.CodeAnalysis.VisualBasic.VisualBasicCompilation" & vbCrLf &
            "        Return Nothing" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Class",
            {MetadataReference.CreateFromFile(visualBasicPath)},
            Path.Combine(AppContext.BaseDirectory, "U9HolderB.dll"))

        Dim csharpLibrary = CreateInMemoryLibrary(
            "U9HolderC",
            "Public Class HolderC" & vbCrLf &
            "    Public Shared Function Compile() As Microsoft.CodeAnalysis.CSharp.CSharpCompilation" & vbCrLf &
            "        Return Nothing" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Class",
            {MetadataReference.CreateFromFile(csharpPath)},
            Path.Combine(AppContext.BaseDirectory, "U9HolderC.dll"))

        Dim options = If(swapReferences,
            ScriptOptions.Default.AddReferences(csharpLibrary).AddReferences(visualBasicLibrary),
            ScriptOptions.Default.AddReferences(visualBasicLibrary).AddReferences(csharpLibrary))

        Dim compilation = VisualBasicScript.Create(
            "Function Uses(b As HolderB, c As HolderC) As Object" & vbCrLf &
            "    Return If(b Is Nothing, CObj(c), CObj(b))" & vbCrLf &
            "End Function", options).GetCompilation()

        Assert.DoesNotContain(compilation.GetDiagnostics(), Function(d) d.Severity = DiagnosticSeverity.Error)
        Assert.NotEqual("MissingAssemblySymbol", DependencyOf(compilation, "U9HolderB", "Microsoft.CodeAnalysis.VisualBasic").GetType().Name)
        Assert.NotEqual("MissingAssemblySymbol", DependencyOf(compilation, "U9HolderC", "Microsoft.CodeAnalysis.CSharp").GetType().Name)
    End Sub

    ''' <summary>
    ''' U9 #8 cell three (C# <c>LibraryReference_MissingDependency</c>, ISR:110): a dependency that no probe can find
    ''' is downgraded to a MissingAssemblySymbol and does *not* block the compilation - the C# baseline asserts an
    ''' empty diagnostic set and the symbol type, and both are asserted here. The identity is one that exists nowhere
    ''' on disk, so the probe over the requesting library's directory finds nothing; an implementation that reported
    ''' the missing dependency as an error would fail the first assertion, and one that dropped the entry from the
    ''' reference list would fail the third.
    ''' </summary>
    <Fact>
    Public Sub LibraryReference_MissingDependency_BecomesAMissingAssemblySymbol()
        Dim absentDependency = CreateInMemoryLibrary(
            "U9AbsentDependency",
            "Public Class D" & vbCrLf &
            "    Public Shared ReadOnly Y As Integer = 1" & vbCrLf &
            "End Class",
            {})

        Dim library = CreateInMemoryLibrary(
            "U9HolderD",
            "Public Class HolderD" & vbCrLf &
            "    Public ReadOnly Field As D = Nothing" & vbCrLf &
            "End Class",
            {absentDependency},
            Path.Combine(AppContext.BaseDirectory, "U9HolderD.dll"))

        Dim script = VisualBasicScript.Create(
            "Function Uses(h As HolderD) As Object" & vbCrLf &
            "    Return h" & vbCrLf &
            "End Function", ScriptOptions.Default.AddReferences(library))

        Dim compilation = script.GetCompilation()
        Assert.DoesNotContain(compilation.GetDiagnostics(), Function(d) d.Severity = DiagnosticSeverity.Error)

        Dim sourceModule = compilation.Assembly.Modules.Single()
        Assert.DoesNotContain("U9AbsentDependency", sourceModule.ReferencedAssemblies.Select(Function(a) a.Name))

        Dim dependency = DependencyOf(compilation, "U9HolderD", "U9AbsentDependency")
        Assert.Equal("MissingAssemblySymbol", dependency.GetType().Name)
    End Sub

    ''' <summary>Emits a library to memory. The optional file path is what the compiler sees as the library's origin;
    ''' ResolveMissingAssembly probes the directory of that path, so it has to name a real directory.</summary>
    Private Shared Function CreateInMemoryLibrary(
        assemblyName As String,
        source As String,
        references As IEnumerable(Of MetadataReference),
        Optional virtualFilePath As String = Nothing) As PortableExecutableReference

        Dim metadataReferences = New List(Of MetadataReference) From {
            MetadataReference.CreateFromFile(GetType(Object).Assembly.Location),
            MetadataReference.CreateFromFile(GetType(Strings).Assembly.Location)
        }
        metadataReferences.AddRange(references)

        Dim compilation = VisualBasicCompilation.Create(
            assemblyName,
            {SyntaxFactory.ParseSyntaxTree(source)},
            metadataReferences,
            New VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, rootNamespace:=""))

        Using stream = New MemoryStream()
            Dim result = compilation.Emit(stream)
            Assert.True(result.Success, String.Join(Environment.NewLine, result.Diagnostics))
            Dim image As ImmutableArray(Of Byte) = ImmutableArray.Create(Of Byte)(stream.ToArray())
            ' the IEnumerable(Of Byte) overload is named explicitly because VB's overload resolution cannot choose
            ' between it and the ImmutableArray(Of Byte) one for an ImmutableArray argument.
            Return MetadataReference.CreateFromImage(DirectCast(image, IEnumerable(Of Byte)), MetadataReferenceProperties.Assembly, Nothing, virtualFilePath)
        End Using
    End Function

    ''' <summary>The named dependency of the named library, as the script compilation sees it.</summary>
    Private Shared Function DependencyOf(compilation As Compilation, libraryName As String, dependencyName As String) As IAssemblySymbol
        Dim sourceModule = compilation.Assembly.Modules.Single()
        Dim library = sourceModule.ReferencedAssemblySymbols.Single(Function(a) a.Name = libraryName)
        Return library.Modules.Single().ReferencedAssemblySymbols.Single(Function(a) a.Name = dependencyName)
    End Function
End Class
