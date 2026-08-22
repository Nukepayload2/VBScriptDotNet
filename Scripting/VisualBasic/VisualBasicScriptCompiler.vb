' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Threading
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Namespace Microsoft.CodeAnalysis.VisualBasic.Scripting

    Friend NotInheritable Class VisualBasicScriptCompiler
        Inherits ScriptCompiler

        Public Shared ReadOnly Instance As ScriptCompiler = New VisualBasicScriptCompiler()

        Private Shared ReadOnly s_defaultOptions As New VisualBasicParseOptions(kind:=SourceCodeKind.Script, languageVersion:=LanguageVersion.Latest)
        Private Shared ReadOnly s_vbRuntimeReference As MetadataReference = MetadataReference.CreateFromAssemblyInternal(GetType(Strings).GetTypeInfo().Assembly)

        ' On .NET Core (10.x) System.Xml.Linq types live in System.Private.Xml.Linq. Roslyn's
        ' IncludeInternalXmlHelper embeds the XML helper tree whenever these types are reachable through
        ' referenced facades (e.g. System.Xml.XDocument), but the embedded tree only binds when the
        ' containing assembly is a direct reference. Reference it by default so script XML literals/imports
        ' compile even with /nostdlib reference sets.
        Private Shared ReadOnly s_xmlLinqReference As MetadataReference = MetadataReference.CreateFromFile(
            Path.Combine(Path.GetDirectoryName(GetType(Object).Assembly.Location), "System.Private.Xml.Linq.dll"))

        Private Sub New()
        End Sub

        Public Overrides ReadOnly Property DiagnosticFormatter As DiagnosticFormatter
            Get
                Return VisualBasicDiagnosticFormatter.Instance
            End Get
        End Property

        Public Overrides ReadOnly Property IdentifierComparer As StringComparer
            Get
                Return CaseInsensitiveComparison.Comparer
            End Get
        End Property

        Public Overrides Function IsCompleteSubmission(tree As SyntaxTree) As Boolean
            Try
                Return SyntaxFactory.IsCompleteSubmission(tree)
            Catch ex As Exception
                ' Syntax error?
                Return True
            End Try
        End Function

        Public Overrides Function ParseSubmission(text As SourceText, parseOptions As ParseOptions, cancellationToken As CancellationToken) As SyntaxTree
            Return SyntaxFactory.ParseSyntaxTree(text, If(parseOptions, s_defaultOptions), cancellationToken:=cancellationToken)
        End Function

        Private Shared Sub ThrowLoadDirectiveError(diagnostic As Diagnostic)
            Throw New CompilationErrorException(diagnostic.GetMessage(), ImmutableArray.Create(diagnostic))
        End Sub

        Private Shared Sub LoadReferencedTrees(
            tree As SyntaxTree,
            parseOptions As ParseOptions,
            options As ScriptOptions,
            loadedTrees As List(Of SyntaxTree),
            activeLoads As HashSet(Of String))

            Dim root = TryCast(tree.GetRoot(), CompilationUnitSyntax)
            If root Is Nothing Then
                Return
            End If

            Dim resolver = options.SourceResolver
            For Each directive In root.GetLoadDirectives()
                Dim path = directive.File.ValueText
                If String.IsNullOrEmpty(path) Then
                    Continue For
                End If

                Dim baseFilePath = If(String.IsNullOrEmpty(tree.FilePath), Nothing, tree.FilePath)
                Dim resolvedPath = resolver.ResolveReference(path, baseFilePath)
                If resolvedPath Is Nothing OrElse Not activeLoads.Add(resolvedPath) Then
                    ThrowLoadDirectiveError(
                        Diagnostic.Create(MessageProvider.Instance, MessageProvider.Instance.ERR_FileNotFound, path).WithLocation(directive.File.GetLocation()))
                End If

                Dim loadedText = resolver.ReadText(resolvedPath)
                Dim loadedTree = SyntaxFactory.ParseSyntaxTree(loadedText, parseOptions, resolvedPath)

                ' Depth-first so that nested #Load trees precede their referrer, matching execution order.
                LoadReferencedTrees(loadedTree, parseOptions, options, loadedTrees, activeLoads)
                activeLoads.Remove(resolvedPath)
                loadedTrees.Add(loadedTree)
            Next
        End Sub

        Private Shared Function GetGlobalImportsForCompilation(script As Script) As IEnumerable(Of GlobalImport)
            Dim importNames = New List(Of String)()
            Dim seenImports = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

            AddImportNames(script.Options.Imports, importNames, seenImports)
            AddPreviousSubmissionImports(script.Previous, importNames, seenImports)
            Return GlobalImport.Parse(importNames)
        End Function

        Private Shared Sub AddImportName(importName As String, importNames As List(Of String), seenImports As HashSet(Of String))
            If seenImports.Add(importName) Then
                importNames.Add(importName)
            End If
        End Sub

        Private Shared Sub AddImportNames(importList As IEnumerable(Of String), importNames As List(Of String), seenImports As HashSet(Of String))
            For Each importName In importList
                AddImportName(importName, importNames, seenImports)
            Next
        End Sub

        Private Shared Sub AddPreviousSubmissionImports(script As Script, importNames As List(Of String), seenImports As HashSet(Of String))
            If script Is Nothing Then
                Return
            End If

            AddPreviousSubmissionImports(script.Previous, importNames, seenImports)

            Dim previousSubmission = TryCast(script.GetCompilation(), VisualBasicCompilation)
            If previousSubmission Is Nothing Then
                Return
            End If

            For Each globalImport In previousSubmission.Options.GlobalImports
                AddImportName(globalImport.Clause.ToString(), importNames, seenImports)
            Next

            For Each syntaxTree In previousSubmission.SyntaxTrees
                Dim root = TryCast(syntaxTree.GetRoot(), CompilationUnitSyntax)
                If root Is Nothing Then
                    Continue For
                End If

                For Each importsStatement In root.Imports
                    For Each clause In importsStatement.ImportsClauses
                        AddImportName(clause.ToString(), importNames, seenImports)
                    Next
                Next
            Next
        End Sub

        Public Overrides Function CreateSubmission(script As Script) As Compilation
            Dim previousSubmission As VisualBasicCompilation = Nothing
            If script.Previous IsNot Nothing Then
                previousSubmission = DirectCast(script.Previous.GetCompilation(), VisualBasicCompilation)
            End If

            Dim diagnostics = DiagnosticBag.GetInstance()
            Dim references = script.GetReferencesForCompilation(MessageProvider.Instance, diagnostics, s_vbRuntimeReference)

            If File.Exists(s_xmlLinqReference.Display) AndAlso
               Not references.Any(Function(r) String.Equals(r.Display, s_xmlLinqReference.Display, StringComparison.OrdinalIgnoreCase)) Then
                references = references.Add(s_xmlLinqReference)
            End If

            '  TODO report Diagnostics
            diagnostics.Free()

            ' parse:
            Dim parseOptions = If(script.Options.ParseOptions, s_defaultOptions)
            Dim tree = SyntaxFactory.ParseSyntaxTree(script.SourceText, parseOptions, script.Options.FilePath)

            ' Each #Load file is parsed as its own tree so spans are preserved. Loaded trees come first
            ' so their top-level code executes before the main file, matching the original text-inline behavior.
            Dim trees = New List(Of SyntaxTree)()
            Dim activeLoads = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            If Not String.IsNullOrEmpty(tree.FilePath) Then
                Dim normalizedMainPath = script.Options.SourceResolver.NormalizePath(tree.FilePath, Nothing)
                If normalizedMainPath IsNot Nothing Then
                    activeLoads.Add(normalizedMainPath)
                End If
            End If
            LoadReferencedTrees(tree, parseOptions, script.Options, trees, activeLoads)
            trees.Add(tree)

            ' create compilation:
            Dim assemblyName As String = Nothing
            Dim submissionTypeName As String = Nothing
            script.Builder.GenerateSubmissionId(assemblyName, submissionTypeName)

            Dim globalImports = GetGlobalImportsForCompilation(script)

            Dim submission = VisualBasicCompilation.CreateScriptCompilation(
                assemblyName,
                trees,
                references,
                New VisualBasicCompilationOptions(
                    outputKind:=OutputKind.DynamicallyLinkedLibrary,
                    mainTypeName:=Nothing,
                    scriptClassName:=submissionTypeName,
                    globalImports:=globalImports,
                    rootNamespace:="",
                    optionStrict:=OptionStrict.Off,
                    optionInfer:=True,
                    optionExplicit:=True,
                    optionCompareText:=False,
                    embedVbCoreRuntime:=False,
                    optimizationLevel:=script.Options.OptimizationLevel,
                    checkOverflow:=script.Options.CheckOverflow,
                    xmlReferenceResolver:=Nothing, ' don't support XML file references in interactive (permissions & doc comment includes)
                    sourceReferenceResolver:=SourceFileResolver.Default,
                    metadataReferenceResolver:=script.Options.MetadataResolver,
                    assemblyIdentityComparer:=DesktopAssemblyIdentityComparer.Default).
                    WithIgnoreCorLibraryDuplicatedTypes(True),
                previousSubmission,
                script.ReturnType,
                script.GlobalsType)

            Return submission
        End Function
    End Class

End Namespace
