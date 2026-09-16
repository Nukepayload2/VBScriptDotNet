' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable
Imports System.IO
Imports System.Reflection
Imports System.Runtime.CompilerServices
Imports System.Text
Imports Microsoft.CodeAnalysis.PooledObjects
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Xunit

Public Class ScriptOptionsTests

    <Fact>
    Public Sub WithParseOptions_AcceptsVisualBasicScriptKindAndLanguageVersion()
        Dim parseOptions = New VisualBasicParseOptions(
            kind:=SourceCodeKind.Script,
            languageVersion:=LanguageVersion.VisualBasic16_9)

        Dim options = ScriptOptions.Default.WithParseOptions(parseOptions)

        Assert.Same(parseOptions, options.ParseOptions)
        Assert.Equal(SourceCodeKind.Script, DirectCast(options.ParseOptions, VisualBasicParseOptions).Kind)
        Assert.Equal(LanguageVersion.VisualBasic16_9, DirectCast(options.ParseOptions, VisualBasicParseOptions).LanguageVersion)
    End Sub

    <Fact>
    Public Sub WithParseOptions_ReturnsSameInstanceForSameParseOptions()
        Dim parseOptions = New VisualBasicParseOptions(kind:=SourceCodeKind.Script, languageVersion:=LanguageVersion.Latest)
        Dim options = ScriptOptions.Default.WithParseOptions(parseOptions)

        Assert.Same(options, options.WithParseOptions(parseOptions))
    End Sub

    <Fact>
    Public Sub WithParseOptions_RegularKindProducesRegularSyntaxDiagnostics()
        Dim regularOptions = ScriptOptions.Default.WithParseOptions(
            New VisualBasicParseOptions(kind:=SourceCodeKind.Regular, languageVersion:=LanguageVersion.Latest))

        Dim regularDiagnostics = VisualBasicScript.Create("? 1 + 2", regularOptions).
            GetCompilation().
            GetDiagnostics()

        Assert.Contains(regularDiagnostics, Function(d) d.Severity = DiagnosticSeverity.Error)

        Dim scriptKindOptions = ScriptOptions.Default.WithParseOptions(
            New VisualBasicParseOptions(kind:=SourceCodeKind.Script, languageVersion:=LanguageVersion.Latest))

        Assert.Equal(3, VisualBasicScript.EvaluateAsync("? 1 + 2", scriptKindOptions).Result)
    End Sub

    <Fact>
    Public Sub WithParseOptions_NonVisualBasicParseOptionsAreRejectedByCompilation()
        Dim options = ScriptOptions.Default.WithParseOptions(New NonVisualBasicParseOptions(SourceCodeKind.Script))

        Dim ex = Assert.Throws(Of InvalidCastException)(Sub() VisualBasicScript.Create("? 1", options).GetCompilation())
        Assert.Contains(NameOf(VisualBasicParseOptions), ex.Message)
    End Sub

    <Fact>
    Public Sub AddImportsAndWithImports_MutateImmutableImportsList()
        Dim options = ScriptOptions.Default.AddImports("System", "System", "<xmlns:p='urn:test'>")

        Assert.Equal({"System", "System", "<xmlns:p='urn:test'>"}, options.Imports)
        Assert.Empty(ScriptOptions.Default.Imports)

        Dim replacement = options.WithImports("System.Text")

        Assert.Equal({"System.Text"}, replacement.Imports)
        Assert.Equal({"System", "System", "<xmlns:p='urn:test'>"}, options.Imports)
    End Sub

    <Fact>
    Public Sub AddImports_DuplicateImportsAreDeduplicatedForCompilation()
        Dim options = ScriptOptions.Default.AddImports("System", "System")
        Dim compilation = VisualBasicScript.Create("? GetType(Console).FullName", options).GetCompilation()
        Dim globalImports = DirectCast(compilation.Options, VisualBasicCompilationOptions).GlobalImports

        Assert.Single(globalImports)
        Assert.Equal("System", globalImports.Single().Clause.ToString())
    End Sub

    <Fact>
    Public Sub AddImports_XmlNamespaceImportsAreUsedByScript()
        Dim options = ScriptOptions.Default.
            AddReferences(GetType(System.Xml.Linq.XNamespace).Assembly).
            AddImports("<xmlns:p='urn:test'>")

        Assert.Equal("urn:test", VisualBasicScript.EvaluateAsync("? GetXmlNamespace(p).NamespaceName", options).Result)
    End Sub

    <Fact>
    Public Sub Imports_DoNotPolluteOtherOptionsInstances()
        Dim textOptions = ScriptOptions.Default.
            AddReferences(GetType(System.Text.StringBuilder).Assembly).
            AddImports("System.Text")

        Assert.Equal("StringBuilder", VisualBasicScript.EvaluateAsync("? New StringBuilder().GetType().Name", textOptions).Result)

        Dim defaultDiagnostics = VisualBasicScript.Create("? New StringBuilder().GetType().Name").GetCompilation().GetDiagnostics()
        Assert.Contains(defaultDiagnostics, Function(d) d.Id = "BC30002")
    End Sub

    <Fact>
    Public Sub AddReferencesAndWithReferences_MutateImmutableReferencesList()
        Dim stringReference = MetadataReference.CreateFromFile(GetType(String).Assembly.Location)
        Dim consoleReference = MetadataReference.CreateFromFile(GetType(Console).Assembly.Location)
        Dim options = ScriptOptions.Default.WithReferences(stringReference)

        Assert.Equal({stringReference}, options.MetadataReferences)
        Assert.NotSame(ScriptOptions.Default, options)

        Dim withDuplicate = options.AddReferences(stringReference, consoleReference)

        Assert.Equal({stringReference, stringReference, consoleReference}, withDuplicate.MetadataReferences)
        Assert.Equal({stringReference}, options.MetadataReferences)
    End Sub

    <Fact>
    Public Sub AddReferences_ReferencesAreUsedByScript()
        Dim options = ScriptOptions.Default.
            WithReferences(ImmutableArray(Of MetadataReference).Empty).
            AddReferences(GetType(System.Text.StringBuilder).Assembly).
            AddImports("System.Text")

        Assert.Equal("StringBuilder", VisualBasicScript.EvaluateAsync("? New StringBuilder().GetType().Name", options).Result)
    End Sub

    <Fact>
    Public Sub AddReferences_BadMetadataReferenceReportsDiagnostics()
        Dim directory = Path.Combine(AppContext.BaseDirectory, "TestTemp", Guid.NewGuid().ToString("N"))
        System.IO.Directory.CreateDirectory(directory)
        Dim invalidReferencePath = Path.Combine(directory, "Bad.Metadata.Reference.dll")
        File.WriteAllBytes(invalidReferencePath, Array.Empty(Of Byte)())

        Try
            Dim options = ScriptOptions.Default.AddReferences(invalidReferencePath)
            Dim ex = Assert.Throws(Of CompilationErrorException)(Sub() VisualBasicScript.EvaluateAsync("? 1", options).GetAwaiter().GetResult())

            Dim diagnostic = Assert.Single(ex.Diagnostics)
            Assert.Equal("BC31519", diagnostic.Id)
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity)
        Finally
            System.IO.Directory.Delete(directory, recursive:=True)
        End Try
    End Sub

    <Fact>
    Public Sub MutationProperties_AreImmutableAndReturnSameInstanceWhenUnchanged()
        Dim sourceResolver = New TestSourceResolver()
        Dim metadataResolver = New TestMetadataResolver()
        Dim fileEncoding = Encoding.UTF8

        Dim options = ScriptOptions.Default.
            WithFilePath("C:\Temp\script.vbx").
            WithFileEncoding(fileEncoding).
            WithEmitDebugInformation(True).
            WithSourceResolver(sourceResolver).
            WithMetadataResolver(metadataResolver)

        Assert.Equal("C:\Temp\script.vbx", options.FilePath)
        Assert.Same(fileEncoding, options.FileEncoding)
        Assert.True(options.EmitDebugInformation)
        Assert.Same(sourceResolver, options.SourceResolver)
        Assert.Same(metadataResolver, options.MetadataResolver)

        Assert.Same(options, options.WithFilePath("C:\Temp\script.vbx"))
        Assert.Same(options, options.WithFileEncoding(fileEncoding))
        Assert.Same(options, options.WithEmitDebugInformation(True))
        Assert.Same(options, options.WithSourceResolver(sourceResolver))
        Assert.Same(options, options.WithMetadataResolver(metadataResolver))

        Assert.Equal("", ScriptOptions.Default.FilePath)
        Assert.Null(ScriptOptions.Default.FileEncoding)
        Assert.False(ScriptOptions.Default.EmitDebugInformation)
        Assert.NotSame(sourceResolver, ScriptOptions.Default.SourceResolver)
        Assert.NotSame(metadataResolver, ScriptOptions.Default.MetadataResolver)
    End Sub

    ' ---- U9 #13 and #14 (design-detailed.md §U9; C# baseline CoreTest\ScriptOptionsTests.cs:23,37,80,105,121,143
    '      for the null argument families and :220,247,312 for the "applied to the compilation option" family) ----
    '
    ' Every case is in memory. Each null argument cell is paired with the accepted call in the same fact, so a host
    ' that rejected the *legitimate* call as well cannot pass by rejecting everything.

    ''' <summary>
    ''' U9 #13 cell one (C# <c>AddReferences_Errors</c>, K-SO:37): every overload that takes references rejects null,
    ''' and the message names the argument - <c>references</c> for the collection, <c>references[0]</c> for a null
    ''' element. The element index is 0 because the list starts empty, which is why the case builds on
    ''' <c>WithReferences(empty)</c> rather than on the default options (whose 25 framework references would move the
    ''' index). The last line is the accepted call, asserted by value: it has to really accumulate.
    ''' </summary>
    <Fact>
    Public Sub AddReferences_NullArgumentsAreRejected()
        Dim empty = ScriptOptions.Default.WithReferences(ImmutableArray(Of MetadataReference).Empty)
        Dim reference = MetadataReference.CreateFromFile(GetType(String).Assembly.Location)

        Assert.Equal("references", NullArgumentParamName(Sub() empty.AddReferences(DirectCast(Nothing, MetadataReference()))))
        Assert.Equal("references[0]", NullArgumentParamName(Sub() empty.AddReferences(New MetadataReference() {Nothing})))
        Assert.Equal("references", NullArgumentParamName(Sub() empty.AddReferences(DirectCast(Nothing, IEnumerable(Of MetadataReference)))))
        Assert.Equal("references[0]", NullArgumentParamName(Sub() empty.AddReferences(DirectCast(New MetadataReference() {Nothing}, IEnumerable(Of MetadataReference)))))
        Assert.Equal("references", NullArgumentParamName(Sub() empty.AddReferences(DirectCast(Nothing, String()))))
        Assert.Equal("references[0]", NullArgumentParamName(Sub() empty.AddReferences(New String() {Nothing})))
        Assert.Equal("references", NullArgumentParamName(Sub() empty.AddReferences(DirectCast(Nothing, IEnumerable(Of String)))))

        Assert.Equal(New MetadataReference() {reference}, empty.AddReferences(reference).MetadataReferences.ToArray())
    End Sub

    ''' <summary>
    ''' U9 #13 cell two (C# <c>WithReferences_Errors</c>, K-SO:80): the same family, plus the shape that only
    ''' <c>WithReferences</c> has - an ImmutableArray that was never initialized (<c>default</c>) is rejected as a
    ''' null collection, while an *empty* one is accepted. The distinction is what the pair
    ''' <c>emptyReferences</c> / <c>ImmutableArray(Of MetadataReference).Empty</c> pins; a host that treated both
    ''' alike would fail one of them.
    ''' </summary>
    <Fact>
    Public Sub WithReferences_NullArgumentsAreRejected()
        Dim empty = ScriptOptions.Default.WithReferences(ImmutableArray(Of MetadataReference).Empty)
        Dim defaultReferences As ImmutableArray(Of MetadataReference) = Nothing
        Dim defaultAssemblies As ImmutableArray(Of Assembly) = Nothing
        Dim defaultStrings As ImmutableArray(Of String) = Nothing

        Assert.Equal("references", NullArgumentParamName(Sub() empty.WithReferences(DirectCast(Nothing, MetadataReference()))))
        Assert.Equal("references", NullArgumentParamName(Sub() empty.WithReferences(DirectCast(Nothing, IEnumerable(Of MetadataReference)))))
        Assert.Equal("references", NullArgumentParamName(Sub() empty.WithReferences(defaultReferences)))
        Assert.Equal("references[0]", NullArgumentParamName(Sub() empty.WithReferences(New MetadataReference() {Nothing})))
        Assert.Equal("references[0]", NullArgumentParamName(Sub() empty.WithReferences(ImmutableArray.Create(Of MetadataReference)(New MetadataReference() {Nothing}))))
        Assert.Equal("references", NullArgumentParamName(Sub() empty.WithReferences(DirectCast(Nothing, Assembly()))))
        Assert.Equal("references", NullArgumentParamName(Sub() empty.WithReferences(DirectCast(Nothing, IEnumerable(Of Assembly)))))
        Assert.Equal("references", NullArgumentParamName(Sub() empty.WithReferences(defaultAssemblies)))
        Assert.Equal("references[0]", NullArgumentParamName(Sub() empty.WithReferences(New Assembly() {Nothing})))
        Assert.Equal("references", NullArgumentParamName(Sub() empty.WithReferences(DirectCast(Nothing, String()))))
        Assert.Equal("references", NullArgumentParamName(Sub() empty.WithReferences(DirectCast(Nothing, IEnumerable(Of String)))))
        Assert.Equal("references", NullArgumentParamName(Sub() empty.WithReferences(defaultStrings)))
        Assert.Equal("references[0]", NullArgumentParamName(Sub() empty.WithReferences(New String() {Nothing})))

        Assert.Empty(empty.MetadataReferences)
        Assert.Equal(1, empty.WithReferences(MetadataReference.CreateFromFile(GetType(String).Assembly.Location)).MetadataReferences.Length)
    End Sub

    ''' <summary>
    ''' U9 #13 cell three (C# <c>AddImports_Errors</c> and <c>AddNamespaces</c>, K-SO:105,121): imports reject null
    ''' the same way, but a name that is not a valid VB namespace is *accepted* - only the CLR namespace shape is
    ''' checked, which is why "" , "blah." and a name with a NUL character go through. Both directions are asserted
    ''' in one fact so the negative cannot pass by rejecting everything.
    ''' </summary>
    <Fact>
    Public Sub AddImports_NullArgumentsAreRejectedButOddNamesAreAccepted()
        Dim defaultImports As ImmutableArray(Of String) = Nothing
        Dim oddName = "b" & ChrW(0) & "lah"

        Assert.Equal("imports", NullArgumentParamName(Sub() ScriptOptions.Default.AddImports(DirectCast(Nothing, String()))))
        Assert.Equal("imports[0]", NullArgumentParamName(Sub() ScriptOptions.Default.AddImports(New String() {Nothing})))
        Assert.Equal("imports", NullArgumentParamName(Sub() ScriptOptions.Default.AddImports(DirectCast(Nothing, IEnumerable(Of String)))))
        Assert.Equal("imports", NullArgumentParamName(Sub() ScriptOptions.Default.AddImports(defaultImports)))
        Assert.Equal("imports[0]", NullArgumentParamName(Sub() ScriptOptions.Default.AddImports(ImmutableArray.Create(Of String)(New String() {Nothing}))))

        Assert.Equal(
            New String() {"", "blah.", oddName, ".blah", oddName},
            ScriptOptions.Default.WithImports(ImmutableArray(Of String).Empty).
                AddImports("", "blah.", oddName, ".blah", oddName).
                Imports.ToArray())
    End Sub

    ''' <summary>U9 #13 cell four (C# <c>WithImports_Errors</c>, K-SO:143): the replacement overload, same family and
    ''' same two message forms as the additive one.</summary>
    <Fact>
    Public Sub WithImports_NullArgumentsAreRejected()
        Dim defaultImports As ImmutableArray(Of String) = Nothing

        Assert.Equal("imports", NullArgumentParamName(Sub() ScriptOptions.Default.WithImports(DirectCast(Nothing, String()))))
        Assert.Equal("imports[0]", NullArgumentParamName(Sub() ScriptOptions.Default.WithImports(New String() {Nothing})))
        Assert.Equal("imports", NullArgumentParamName(Sub() ScriptOptions.Default.WithImports(DirectCast(Nothing, IEnumerable(Of String)))))
        Assert.Equal("imports", NullArgumentParamName(Sub() ScriptOptions.Default.WithImports(defaultImports)))
        Assert.Equal("imports[0]", NullArgumentParamName(Sub() ScriptOptions.Default.WithImports(ImmutableArray.Create(Of String)(New String() {Nothing}))))

        Assert.Equal(New String() {"System.Text"}, ScriptOptions.Default.AddImports("System").WithImports("System.Text").Imports.ToArray())
    End Sub

    Private Shared Function NullArgumentParamName(action As Action) As String
        Return Assert.Throws(Of ArgumentNullException)(action).ParamName
    End Function

    ''' <summary>
    ''' U9 #14 cell one (C# <c>CheckOverflow_Is_AppliedTo_CompilationOption</c>, K-SO:247): the script option is read
    ''' out of the compilation object itself (<c>VisualBasicScriptCompiler.vb:224</c>), never out of the options the
    ''' caller passed in. Both rows are asserted, so an implementation that hardcoded either value would fail one.
    ''' </summary>
    <Theory>
    <InlineData(True)>
    <InlineData(False)>
    Public Sub CheckOverflow_IsAppliedToTheCompilationOption(checkOverflow As Boolean)
        Dim options = ScriptOptions.Default.WithCheckOverflow(checkOverflow)
        Dim compilation = VisualBasicScript.Create("? 1 + 1", options).GetCompilation()

        Assert.Equal(checkOverflow, options.CheckOverflow)
        Assert.Equal(checkOverflow, compilation.Options.CheckOverflow)
    End Sub

    ''' <summary>
    ''' U9 #14 cell two (C# <c>WarningLevel_Is_AppliedTo_CompilationOption</c>, K-SO:312): registered divergence.
    ''' The compilation object is read, not the options object, and VB does not carry the option across:
    ''' <c>VisualBasicScriptCompiler.vb:212-228</c> passes optimizationLevel and checkOverflow into
    ''' <c>VisualBasicCompilationOptions</c> and has no warningLevel to pass, and the VB options type has no
    ''' parameter for it at all, so the compilation keeps the base default 1 for every value. The last two assertions
    ''' are the contrast that keeps this from being a tautology: two options of the same family *do* arrive.
    ''' </summary>
    <Fact>
    Public Sub WarningLevel_DoesNotReachTheCompilationOption()
        Assert.Equal(0, ScriptOptions.Default.WithWarningLevel(0).WarningLevel)
        Assert.Equal(1, VisualBasicScript.Create("? 1 + 1", ScriptOptions.Default.WithWarningLevel(0)).GetCompilation().Options.WarningLevel)
        Assert.Equal(1, VisualBasicScript.Create("? 1 + 1", ScriptOptions.Default.WithWarningLevel(3)).GetCompilation().Options.WarningLevel)
        Assert.Equal(1, VisualBasicScript.Create("? 1 + 1", ScriptOptions.Default).GetCompilation().Options.WarningLevel)

        Assert.Equal(
            OptimizationLevel.Release,
            VisualBasicScript.Create("? 1 + 1", ScriptOptions.Default.WithOptimizationLevel(OptimizationLevel.Release)).GetCompilation().Options.OptimizationLevel)
        Assert.True(VisualBasicScript.Create("? 1 + 1", ScriptOptions.Default.WithCheckOverflow(True)).GetCompilation().Options.CheckOverflow)
    End Sub

    ''' <summary>
    ''' U9 #14 cell three: VB not applicable, with the retrieval that establishes it.
    ''' <c>grep -rn "AllowUnsafe" Compilers/Core/Portable/Compilation/CompilationOptions.cs</c> has no hits (the
    ''' property is on CSharpCompilationOptions only) and <c>grep -rn "allowUnsafe" Compilers/VisualBasic/Portable</c>
    ''' has none either, so the shared <c>ScriptOptions.AllowUnsafe</c> has no VB compilation option to land on. The
    ''' cell asserts the absence on the type objects, which makes the claim mechanical from the test run instead of
    ''' prose, and the cell count for #14 is 2 rather than 3.
    ''' <para>
    ''' Classification note: <c>AllowUnsafe</c> is an option property, not a writable syntax form, so it sits outside
    ''' the four recorded reasons for not-applicable (descriptive / pure grouping / lexical level / no
    ''' implementation). It is recorded here as a sub-case of that value - the shared option exists but the VB
    ''' language has no compilation option for it, i.e. "no implementation on this side of the option surface" -
    ''' rather than as a fifth value; the recommendation is to state that sub-case in the ledger's definition of the
    ''' value during the design-document round instead of widening the closed set silently.
    ''' </para>
    ''' </summary>
    <Fact>
    Public Sub AllowUnsafe_HasNoCompilationOptionToLandOn()
        Assert.Null(GetType(CompilationOptions).GetProperty("AllowUnsafe"))
        Assert.Null(GetType(VisualBasicCompilationOptions).GetProperty("AllowUnsafe"))
        Assert.False(ScriptOptions.Default.WithAllowUnsafe(False).AllowUnsafe)
        Assert.True(ScriptOptions.Default.WithAllowUnsafe(True).AllowUnsafe)
    End Sub

    Private NotInheritable Class NonVisualBasicParseOptions
        Inherits ParseOptions

        Public Sub New(kind As SourceCodeKind)
            MyBase.New(kind, DocumentationMode.None)
        End Sub

        Public Overrides ReadOnly Property Language As String
            Get
                Return "Not Visual Basic"
            End Get
        End Property

        Public Overrides ReadOnly Property Features As IReadOnlyDictionary(Of String, String)
            Get
                Return ImmutableDictionary(Of String, String).Empty
            End Get
        End Property

        Public Overrides ReadOnly Property PreprocessorSymbolNames As IEnumerable(Of String)
            Get
                Return ImmutableArray(Of String).Empty
            End Get
        End Property

        Friend Overrides Sub ValidateOptions(builder As ArrayBuilder(Of Diagnostic))
        End Sub

        Public Overrides Function CommonWithKind(kind As SourceCodeKind) As ParseOptions
            Return New NonVisualBasicParseOptions(kind)
        End Function

        Protected Overrides Function CommonWithDocumentationMode(documentationMode As DocumentationMode) As ParseOptions
            Return Me
        End Function

        Protected Overrides Function CommonWithFeatures(features As IEnumerable(Of KeyValuePair(Of String, String))) As ParseOptions
            Return Me
        End Function

        Public Overrides Function Equals(obj As Object) As Boolean
            Return ReferenceEquals(Me, obj)
        End Function

        Public Overrides Function GetHashCode() As Integer
            Return RuntimeHelpers.GetHashCode(Me)
        End Function
    End Class

    Private NotInheritable Class TestSourceResolver
        Inherits SourceReferenceResolver

        Public Overrides Function NormalizePath(path As String, baseFilePath As String) As String
            Return path
        End Function

        Public Overrides Function ResolveReference(path As String, baseFilePath As String) As String
            Return path
        End Function

        Public Overrides Function OpenRead(resolvedPath As String) As Stream
            Return New MemoryStream()
        End Function

        Public Overrides Function Equals(obj As Object) As Boolean
            Return ReferenceEquals(Me, obj)
        End Function

        Public Overrides Function GetHashCode() As Integer
            Return RuntimeHelpers.GetHashCode(Me)
        End Function
    End Class

    Private NotInheritable Class TestMetadataResolver
        Inherits MetadataReferenceResolver

        Public Overrides Function ResolveReference(reference As String, baseFilePath As String, properties As MetadataReferenceProperties) As ImmutableArray(Of PortableExecutableReference)
            Return ImmutableArray(Of PortableExecutableReference).Empty
        End Function

        Public Overrides Function Equals(obj As Object) As Boolean
            Return ReferenceEquals(Me, obj)
        End Function

        Public Overrides Function GetHashCode() As Integer
            Return RuntimeHelpers.GetHashCode(Me)
        End Function
    End Class
End Class
