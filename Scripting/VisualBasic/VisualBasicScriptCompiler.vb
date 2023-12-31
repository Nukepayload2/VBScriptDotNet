﻿' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Reflection
Imports System.Threading
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic

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

        Private Shared s_cachedGlobalImports As IEnumerable(Of GlobalImport)
        Private Shared Function GetGlobalImportsForCompilation(script As Script) As IEnumerable(Of GlobalImport)
            ' TODO: Import semantics is missing. But at least we can preserve the initial imports defined in rsp.
            ' Remove or rewrite this cache when import semantics are supported.
            ' See https://github.com/dotnet/roslyn/issues/6554
            If s_cachedGlobalImports IsNot Nothing Then Return s_cachedGlobalImports

            ' TODO: get imports out of compilation??? https://github.com/dotnet/roslyn/issues/5854
            s_cachedGlobalImports = script.Options.Imports.Select(Function(n) GlobalImport.Parse(n))
            Return s_cachedGlobalImports
        End Function

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
            Dim tree = SyntaxFactory.ParseSyntaxTree(script.SourceText, If(script.Options.ParseOptions, s_defaultOptions), script.Options.FilePath)

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
