' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports Microsoft.CodeAnalysis.Test.Utilities
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Imports Roslyn.Test.Utilities

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests
    Public Class ScriptSemanticsTests
        Inherits BasicTestBase

        <WorkItem(530404, "http://vstfdevdiv:8080/DevDiv2/DevDiv/_workitems/edit/530404")>
        <Fact>
        Public Sub DiagnosticsPass()
            Dim source0 = "
Function F(e As System.Linq.Expressions.Expression(Of System.Func(Of Object))) As Object
    Return e.Compile()()
End Function"

            Dim c0 = CreateSubmission(source0, {SystemCoreRef})

            Dim source1 = "
F(Function()
    Return Nothing
  End Function)
"
            Dim c1 = CreateSubmission(source1, {SystemCoreRef}, previous:=c0)

            AssertTheseDiagnostics(c1,
<errors>
BC36675: Statement lambdas cannot be converted to expression trees.
F(Function()
  ~~~~~~~~~~~
</errors>)
        End Sub

        <Fact>
        <WorkItem(10023, "https://github.com/dotnet/roslyn/issues/10023")>
        Public Sub Errors_01()
            Dim code = "System.Console.WriteLine(1)"
            Dim compilationUnit = VisualBasic.SyntaxFactory.ParseCompilationUnit(code, options:=New VisualBasicParseOptions(kind:=SourceCodeKind.Script))
            Dim syntaxTree = compilationUnit.SyntaxTree
            Dim compilation = CreateCompilationWithMscorlib461({syntaxTree}, assemblyName:="Errors_01", options:=TestOptions.ReleaseExe)
            Dim semanticModel = compilation.GetSemanticModel(syntaxTree, True)
            Dim node5 As MemberAccessExpressionSyntax = ErrorTestsGetNode(syntaxTree)
            Assert.Equal("WriteLine", node5.Name.ToString())
            Assert.Equal("Sub System.Console.WriteLine(value As System.Int32)", semanticModel.GetSymbolInfo(node5.Name).Symbol.ToTestDisplayString())

            compilation.AssertTheseDiagnostics()

            compilation = CreateCompilationWithMscorlib461({syntaxTree}, options:=TestOptions.ReleaseExe.WithScriptClassName("Script"), assemblyName:="Errors_01")
            semanticModel = compilation.GetSemanticModel(syntaxTree, True)
            node5 = ErrorTestsGetNode(syntaxTree)
            Assert.Equal("WriteLine", node5.Name.ToString())
            Assert.Equal("Sub System.Console.WriteLine(value As System.Int32)", semanticModel.GetSymbolInfo(node5.Name).Symbol.ToTestDisplayString())

            compilation.AssertTheseDiagnostics()

            syntaxTree = SyntaxFactory.ParseSyntaxTree(code, options:=New VisualBasicParseOptions(kind:=SourceCodeKind.Script))
            compilation = CreateCompilationWithMscorlib461AndVBRuntime({syntaxTree}, options:=TestOptions.ReleaseExe)
            semanticModel = compilation.GetSemanticModel(syntaxTree, True)
            node5 = ErrorTestsGetNode(syntaxTree)
            Assert.Equal("WriteLine", node5.Name.ToString())
            Assert.Equal("Sub System.Console.WriteLine(value As System.Int32)", semanticModel.GetSymbolInfo(node5.Name).Symbol.ToTestDisplayString())

            CompileAndVerify(compilation, expectedOutput:="1").VerifyDiagnostics()

            syntaxTree = SyntaxFactory.ParseSyntaxTree(code, options:=New VisualBasicParseOptions(kind:=SourceCodeKind.Script))
            compilation = CreateCompilationWithMscorlib461AndVBRuntime({syntaxTree}, options:=TestOptions.ReleaseExe.WithScriptClassName("Script"))
            semanticModel = compilation.GetSemanticModel(syntaxTree, True)
            node5 = ErrorTestsGetNode(syntaxTree)
            Assert.Equal("WriteLine", node5.Name.ToString())
            Assert.Equal("Sub System.Console.WriteLine(value As System.Int32)", semanticModel.GetSymbolInfo(node5.Name).Symbol.ToTestDisplayString())

            CompileAndVerify(compilation, expectedOutput:="1").VerifyDiagnostics()

            syntaxTree = SyntaxFactory.ParseSyntaxTree(code, options:=New VisualBasicParseOptions(kind:=SourceCodeKind.Script))
            compilation = CreateCompilationWithMscorlib461AndVBRuntime({syntaxTree}, options:=TestOptions.ReleaseExe.WithScriptClassName(""))
            semanticModel = compilation.GetSemanticModel(syntaxTree, True)
            node5 = ErrorTestsGetNode(syntaxTree)
            Assert.Equal("WriteLine", node5.Name.ToString())
            Assert.Equal("Sub System.Console.WriteLine(value As System.Int32)", semanticModel.GetSymbolInfo(node5.Name).Symbol.ToTestDisplayString())

            compilation.AssertTheseDiagnostics(
<expected>
BC2014: the value '' is invalid for option 'ScriptClassName'
</expected>
            )

            syntaxTree = SyntaxFactory.ParseSyntaxTree(code, options:=New VisualBasicParseOptions(kind:=SourceCodeKind.Script))
            compilation = CreateCompilationWithMscorlib461AndVBRuntime({syntaxTree}, options:=TestOptions.ReleaseExe.WithScriptClassName(Nothing))
            semanticModel = compilation.GetSemanticModel(syntaxTree, True)
            node5 = ErrorTestsGetNode(syntaxTree)
            Assert.Equal("WriteLine", node5.Name.ToString())
            Assert.Equal("Sub System.Console.WriteLine(value As System.Int32)", semanticModel.GetSymbolInfo(node5.Name).Symbol.ToTestDisplayString())

            compilation.AssertTheseDiagnostics(
<expected>
BC2014: the value 'Nothing' is invalid for option 'ScriptClassName'
</expected>
            )

            syntaxTree = SyntaxFactory.ParseSyntaxTree(code, options:=New VisualBasicParseOptions(kind:=SourceCodeKind.Script))
            compilation = CreateCompilationWithMscorlib461AndVBRuntime({syntaxTree}, options:=TestOptions.ReleaseExe.WithScriptClassName("a" + ChrW(0) + "b"))
            semanticModel = compilation.GetSemanticModel(syntaxTree, True)
            node5 = ErrorTestsGetNode(syntaxTree)
            Assert.Equal("WriteLine", node5.Name.ToString())
            Assert.Equal("Sub System.Console.WriteLine(value As System.Int32)", semanticModel.GetSymbolInfo(node5.Name).Symbol.ToTestDisplayString())

            compilation.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_InvalidSwitchValue).WithArguments("ScriptClassName", "a" + ChrW(0) + "b").WithLocation(1, 1)
                )
        End Sub

        <Fact>
        <WorkItem(10023, "https://github.com/dotnet/roslyn/issues/10023")>
        <WorkItem("https://github.com/dotnet/roslyn/issues/78792")>
        Public Sub Errors_02()
            ' A script class may span multiple script trees; symbols in each tree resolve normally
            ' even when the trees are used in a regular compilation (previously this threw
            ' InvalidOperationException from SyntaxReferences.Single()).
            Dim compilationUnit = VisualBasic.SyntaxFactory.ParseCompilationUnit("System.Console.WriteLine(1)", options:=New VisualBasicParseOptions(kind:=SourceCodeKind.Script))
            Dim syntaxTree1 = compilationUnit.SyntaxTree
            Dim syntaxTree2 = SyntaxFactory.ParseSyntaxTree("System.Console.WriteLine(2)", options:=New VisualBasicParseOptions(kind:=SourceCodeKind.Script))
            Dim node1 As MemberAccessExpressionSyntax = ErrorTestsGetNode(syntaxTree1)
            Assert.Equal("WriteLine", node1.Name.ToString())
            Dim node2 As MemberAccessExpressionSyntax = ErrorTestsGetNode(syntaxTree2)
            Assert.Equal("WriteLine", node2.Name.ToString())

            Dim expectedDisplay = "Sub System.Console.WriteLine(value As System.Int32)"

            Dim compilation = CreateCompilationWithMscorlib461({syntaxTree1, syntaxTree2})
            Dim semanticModel1 = compilation.GetSemanticModel(syntaxTree1, True)
            Dim semanticModel2 = compilation.GetSemanticModel(syntaxTree2, True)

            Assert.Equal(expectedDisplay, semanticModel1.GetSymbolInfo(node1.Name).Symbol.ToTestDisplayString())
            Assert.Equal(expectedDisplay, semanticModel2.GetSymbolInfo(node2.Name).Symbol.ToTestDisplayString())

            compilation = CreateCompilationWithMscorlib461({syntaxTree2, syntaxTree1})
            semanticModel1 = compilation.GetSemanticModel(syntaxTree1, True)
            semanticModel2 = compilation.GetSemanticModel(syntaxTree2, True)

            Assert.Equal(expectedDisplay, semanticModel1.GetSymbolInfo(node1.Name).Symbol.ToTestDisplayString())
            Assert.Equal(expectedDisplay, semanticModel2.GetSymbolInfo(node2.Name).Symbol.ToTestDisplayString())
        End Sub

        Private Shared Function ErrorTestsGetNode(syntaxTree As SyntaxTree) As MemberAccessExpressionSyntax
            Dim node1 = DirectCast(syntaxTree.GetRoot(), CompilationUnitSyntax)
            Dim node3 = DirectCast(node1.Members.First(), ExpressionStatementSyntax)
            Dim node4 = DirectCast(node3.Expression, InvocationExpressionSyntax)
            Dim node5 = DirectCast(node4.Expression, MemberAccessExpressionSyntax)
            Return node5
        End Function

#Region "Optional leading ? on REPL expressions - L2 semantics (test-plan section 4, S1-S12)"

        Private Shared Function GetFirstExpressionStatement(compilation As VisualBasicCompilation) As ExpressionStatementSyntax
            Dim tree = compilation.SyntaxTrees.Single()
            Dim root = DirectCast(tree.GetRoot(), CompilationUnitSyntax)
            Return DirectCast(root.Members.Single(), ExpressionStatementSyntax)
        End Function

        ''' <summary>
        ''' Script compilation options: mirror the REPL's default global imports (System / Microsoft.VisualBasic)
        ''' so bare identifiers such as Now, Console, and DateTime resolve (CreateSubmission has no default global imports).
        ''' </summary>
        Private Shared Function ScriptCompilationOptions() As VisualBasicCompilationOptions
            Return New VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary).
                WithGlobalImports(GlobalImport.Parse("System", "Microsoft.VisualBasic"))
        End Function

        ''' <summary>
        ''' S1: A bare property value reference (Now) has no diagnostics; GetTypeInfo returns DateTime (non-Void),
        ''' so HasSubmissionResult is True. This visible contract stands in for the bound-kind regression
        ''' (design section 4: BoundPropertyGroup -> MakeRValue -> BoundPropertyAccess -> prints).
        ''' </summary>
        <Fact>
        Public Sub BarePropertyValueReference_NoDiagnostics()
            Dim c = CreateSubmission("Now", options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)
            c.VerifyDiagnostics()

            Dim statement = GetFirstExpressionStatement(c)
            Dim model = c.GetSemanticModel(statement.SyntaxTree)
            Dim type = model.GetTypeInfo(statement.Expression).Type
            Assert.Equal(SpecialType.System_DateTime, type.SpecialType)

            Assert.True(c.HasSubmissionResult())
        End Sub

        ''' <summary>
        ''' S2: A bare local variable (before after "Dim before = Now" in the same submission) has no diagnostics;
        ''' it binds as BoundLocal (design section 3.3 decision table), non-Void, so HasSubmissionResult is True.
        ''' </summary>
        <Fact>
        Public Sub BareLocalValueReference_NoDiagnostics()
            Dim c = CreateSubmission("Dim before = Now : before", options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)
            c.VerifyDiagnostics()

            ' Bound-kind regression: before is a local variable value reference (BoundLocal) within the same submission.
            ' The exact GetTypeInfo shape for a bare local identifier is not asserted here; the visible contract of
            ' "no diagnostics + HasSubmissionResult True" evidences that the value reference prints.
            Assert.True(c.HasSubmissionResult())
        End Sub

        ''' <summary>
        ''' S3: A member-access property (DateTime.Now) has no diagnostics (BC30545 suppressed for the final statement).
        ''' </summary>
        <Fact>
        Public Sub MemberAccessPropertyValueReference_NoDiagnostics()
            Dim c = CreateSubmission("DateTime.Now", options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)
            c.VerifyDiagnostics()
            Assert.True(c.HasSubmissionResult())
        End Sub

        ''' <summary>
        ''' S4a: An arithmetic expression 1 + 2 has no diagnostics.
        ''' Note: the top-level IntegerLiteralToken dispatch gap in F9 was fixed (root cause 1, see Parser.vb ParseDeclarationStatementInternal).
        ''' </summary>
        <Fact>
        Public Sub BareArithmeticExpression_InteractiveNoDiagnostics()
            Dim c = CreateSubmission("1 + 2", parseOptions:=TestOptions.Script)
            c.VerifyDiagnostics()
            Assert.True(c.HasSubmissionResult())
        End Sub

        ''' <summary>
        ''' S4b: A comparison expression x > 5 has no diagnostics (x declared first to avoid the undeclared-variable error under Option Explicit).
        ''' </summary>
        <Fact>
        Public Sub BareComparisonExpression_InteractiveNoDiagnostics()
            Dim c = CreateSubmission("Dim x = 1 : x > 5", parseOptions:=TestOptions.Script)
            c.VerifyDiagnostics()
        End Sub

        ''' <summary>
        ''' S4c: String concatenation "a" & "b" has no diagnostics.
        ''' </summary>
        <Fact>
        Public Sub BareStringConcatenation_InteractiveNoDiagnostics()
            Dim c = CreateSubmission("""a"" & ""b""", parseOptions:=TestOptions.Script)
            c.VerifyDiagnostics()
        End Sub

        ''' <summary>
        ''' S5: Method groups: a Sub MySub does not print (BoundMethodGroup -> BoundCall(Void)) while a Function MyFunc prints
        ''' (BoundCall non-Void). Bound-kind regression: MySub -> HasSubmissionResult False; MyFunc -> True.
        ''' Note: HasSubmissionResult was made method-group aware (root cause 2, see log entry 19), so MySub -> False, MyFunc -> True.
        ''' </summary>
        <Fact>
        Public Sub BareMethodGroup_SubDoesNotPrint_FunctionPrints()
            Dim subCompilation = CreateSubmission("Sub MySub()
End Sub
MySub", parseOptions:=TestOptions.Script)
            subCompilation.VerifyDiagnostics()
            Assert.False(subCompilation.HasSubmissionResult())

            Dim funcCompilation = CreateSubmission("Function MyFunc() As Integer
Return 42
End Function
MyFunc", parseOptions:=TestOptions.Script)
            funcCompilation.VerifyDiagnostics()
            Assert.True(funcCompilation.HasSubmissionResult())
        End Sub

        ''' <summary>
        ''' S6: A side-effecting call Console.WriteLine("hi") has no diagnostics (BoundCall Void; it neither changes the value nor prints).
        ''' </summary>
        <Fact>
        Public Sub SideEffectCall_NoDiagnostics()
            Dim c = CreateSubmission("Console.WriteLine(""hi"")", options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)
            c.VerifyDiagnostics()
            Assert.False(c.HasSubmissionResult())
        End Sub

        ''' <summary>
        ''' S7: An assignment x = 5 does not change the submission value (x declared first); no diagnostics (assignment, no print).
        ''' </summary>
        <Fact>
        Public Sub Assignment_NoDiagnostics_DoesNotPrint()
            Dim c = CreateSubmission("Dim x = 1 : x = 5", parseOptions:=TestOptions.Script)
            c.VerifyDiagnostics()
            Assert.False(c.HasSubmissionResult())
        End Sub

        ''' <summary>
        ''' S8: A non-final bare expression 1 + 2 : x = 5 reports BC31003 (still an error when not final).
        ''' Note: the top-level IntegerLiteralToken dispatch gap in F9 was fixed (root cause 1, see Parser.vb
        ''' ParseDeclarationStatementInternal), so a non-final numeric bare expression now correctly reports BC31003.
        ''' </summary>
        <Fact>
        Public Sub NonFinalBareExpression_StillErrors_BC31003()
            Dim c = CreateSubmission("Dim x = 1 : 1 + 2 : x = 5", parseOptions:=TestOptions.Script)
            Assert.Contains(c.GetDiagnostics(), Function(d) d.Id = "BC31003")
        End Sub

        ''' <summary>
        ''' S8 supplement: a non-final bare identifier x (non-numeric) reports BC31003, confirming the BC31003 mechanism itself
        ''' is unaffected by the F9 numeric dispatch gap.
        ''' </summary>
        <Fact>
        Public Sub NonFinalBareIdentifier_StillErrors_BC31003()
            Dim c = CreateSubmission("Dim x = 1 : x : x = 5", parseOptions:=TestOptions.Script)
            Assert.Contains(c.GetDiagnostics(), Function(d) d.Id = "BC31003")
        End Sub

        ''' <summary>
        ''' S9: A non-final member access DateTime.Now : x = 5 reports BC30545 (the Invocation branch does not suppress it when non-final).
        ''' </summary>
        <Fact>
        Public Sub NonFinalMemberAccess_StillErrors_BC30545()
            Dim c = CreateSubmission("Dim x = 1 : DateTime.Now : x = 5", options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)
            Assert.Contains(c.GetDiagnostics(), Function(d) d.Id = "BC30545")
        End Sub

        ''' <summary>
        ''' S10: Late binding: a member access on an Object receiver as a statement.
        ''' Note: the design section 4 decision table (BoundKind.LateMemberAccess -> no diagnostics, no print) disagrees with the current
        ''' compiler: the statement binds as BoundLateInvocation (Reclassify sets a Call access kind), HasSubmissionResult is True
        ''' (type Object), and InitializerRewriter returns the late-bound call as a submission value by value, reporting BC30491
        ''' (ERR_VoidValue). This is pre-existing behavior (the same statement inside a Regular Sub only warns BC42104 and does not
        ''' report BC30491; BC30491 fires only on the script submission-result path), not a regression from F9/F10. This test records
        ''' the actual behavior; fixing the compiler is out of scope (root cause 3, see log entry 19).
        ''' </summary>
        <Fact>
        Public Sub LateBoundMemberAccess_ReportsVoidValue()
            Dim c = CreateSubmission("Dim o As Object = New Object() : o.Prop", options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)
            Assert.Contains(c.GetDiagnostics(), Function(d) d.Id = "BC30491")
        End Sub

        ''' <summary>
        ''' S11: An empty script or declarations: Sub/Imports/Dim all have no diagnostics and do not print.
        ''' </summary>
        <Fact>
        Public Sub EmptyScriptAndDeclarations_NoDiagnostics()
            Dim subCompilation = CreateSubmission("Sub Goo()
End Sub", parseOptions:=TestOptions.Script)
            subCompilation.VerifyDiagnostics()
            Assert.False(subCompilation.HasSubmissionResult())

            Dim importsCompilation = CreateSubmission("Imports System", parseOptions:=TestOptions.Script)
            importsCompilation.VerifyDiagnostics()
            Assert.False(importsCompilation.HasSubmissionResult())

            Dim dimCompilation = CreateSubmission("Dim i As Integer", parseOptions:=TestOptions.Script)
            dimCompilation.VerifyDiagnostics()
            Assert.False(dimCompilation.HasSubmissionResult())
        End Sub

        ''' <summary>
        ''' S12: Conditional access a?.Length has no diagnostics (ConditionalAccess recurses through the WhenNotNull child;
        ''' BC30545 suppressed for the final statement).
        ''' </summary>
        <Fact>
        Public Sub ConditionalAccess_NoDiagnostics()
            Dim c0 = CreateSubmission("Dim a As New System.Text.StringBuilder()", parseOptions:=TestOptions.Script)
            c0.VerifyDiagnostics()
            Dim c1 = CreateSubmission("a?.Length", previous:=c0, parseOptions:=TestOptions.Script)
            c1.VerifyDiagnostics()
        End Sub

#End Region

    End Class
End Namespace

