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
