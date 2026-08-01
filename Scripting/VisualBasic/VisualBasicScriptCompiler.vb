' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.IO
Imports System.Reflection
Imports System.Text
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

        Private Shared Function ExpandLoadDirectives(source As SourceText, options As ScriptOptions) As SourceText
            Dim resolver = options.SourceResolver
            Dim hasLoadDirectives = False
            Dim expanded = ExpandLoadDirectives(source.ToString(), options.FilePath, resolver, New HashSet(Of String)(StringComparer.OrdinalIgnoreCase), hasLoadDirectives)
            If Not hasLoadDirectives Then
                Return source
            End If

            Return SourceText.From(expanded, source.Encoding)
        End Function

        Private Shared Function ExpandLoadDirectives(source As String, baseFilePath As String, resolver As SourceReferenceResolver, activeLoads As HashSet(Of String), ByRef hasLoadDirectives As Boolean) As String
            Dim builder As New StringBuilder()

            Using reader As New StringReader(source)
                While True
                    Dim line = reader.ReadLine()
                    If line Is Nothing Then
                        Exit While
                    End If

                    Dim loadedPath As String = Nothing
                    If TryGetLoadDirectivePath(line, loadedPath) Then
                        hasLoadDirectives = True
                        Dim resolvedPath = resolver.ResolveReference(loadedPath, If(String.IsNullOrEmpty(baseFilePath), Nothing, baseFilePath))
                        If resolvedPath Is Nothing Then
                            ThrowLoadDirectiveError(Diagnostic.Create(MessageProvider.Instance, MessageProvider.Instance.ERR_FileNotFound, loadedPath))
                        End If

                        If Not activeLoads.Add(resolvedPath) Then
                            ThrowLoadDirectiveError(Diagnostic.Create(MessageProvider.Instance, MessageProvider.Instance.ERR_FileNotFound, loadedPath))
                        End If

                        Dim loadedText = resolver.ReadText(resolvedPath)
                        builder.AppendLine(ExpandLoadDirectives(loadedText.ToString(), resolvedPath, resolver, activeLoads, hasLoadDirectives))
                        activeLoads.Remove(resolvedPath)
                    Else
                        builder.AppendLine(line)
                    End If
                End While
            End Using

            Return builder.ToString()
        End Function

        Private Shared Function TryGetLoadDirectivePath(line As String, ByRef path As String) As Boolean
            Dim index = 0
            While index < line.Length AndAlso Char.IsWhiteSpace(line(index))
                index += 1
            End While

            If index >= line.Length OrElse line(index) <> "#"c Then
                Return False
            End If

            index += 1
            While index < line.Length AndAlso Char.IsWhiteSpace(line(index))
                index += 1
            End While

            Const loadKeyword = "Load"
            If index + loadKeyword.Length > line.Length OrElse
                Not String.Equals(line.Substring(index, loadKeyword.Length), loadKeyword, StringComparison.OrdinalIgnoreCase) Then
                Return False
            End If

            index += loadKeyword.Length
            While index < line.Length AndAlso Char.IsWhiteSpace(line(index))
                index += 1
            End While

            If index >= line.Length OrElse line(index) <> """"c Then
                Return False
            End If

            index += 1
            Dim builder As New StringBuilder()
            While index < line.Length
                Dim ch = line(index)
                If ch = """"c Then
                    If index + 1 < line.Length AndAlso line(index + 1) = """"c Then
                        builder.Append(""""c)
                        index += 2
                        Continue While
                    End If

                    index += 1
                    While index < line.Length AndAlso Char.IsWhiteSpace(line(index))
                        index += 1
                    End While

                    If index <> line.Length Then
                        Return False
                    End If

                    path = builder.ToString()
                    Return True
                End If

                builder.Append(ch)
                index += 1
            End While

            Return False
        End Function

        Private Shared Sub ThrowLoadDirectiveError(diagnostic As Diagnostic)
            Throw New CompilationErrorException(diagnostic.GetMessage(), ImmutableArray.Create(diagnostic))
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

            '  TODO report Diagnostics
            diagnostics.Free()

            ' parse:
            Dim sourceText = ExpandLoadDirectives(script.SourceText, script.Options)
            Dim tree = SyntaxFactory.ParseSyntaxTree(sourceText, If(script.Options.ParseOptions, s_defaultOptions), script.Options.FilePath)

            ' create compilation:
            Dim assemblyName As String = Nothing
            Dim submissionTypeName As String = Nothing
            script.Builder.GenerateSubmissionId(assemblyName, submissionTypeName)

            Dim globalImports = GetGlobalImportsForCompilation(script)

            Dim submission = VisualBasicCompilation.CreateScriptCompilation(
                assemblyName,
                tree,
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
