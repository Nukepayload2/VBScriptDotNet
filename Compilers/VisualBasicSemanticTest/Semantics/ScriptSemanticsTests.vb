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

#Region "Top level scripts: 'Await' in Catch/Finally/SyncLock and the shared implicit Me (F10/F08)"

        Private Shared Function ErrorCode(errorId As ERRID) As String
            Return "BC" & CInt(errorId).ToString("00000")
        End Function

        ''' <summary>
        ''' The span of BC36943 is the whole 'Await' expression, exactly as it is in an ordinary async method
        ''' (see <c>Semantics\AsyncAwait.vb</c>).
        ''' </summary>
        Private Const AwaitDelayText As String = "Await System.Threading.Tasks.Task.Delay(1)"

        ''' <summary>The span of BC36937 and of the shared-initializer error is the 'Await' keyword.</summary>
        Private Const AwaitKeywordText As String = "Await"

        ''' <summary>
        ''' Asserts that the compilation reports exactly one diagnostic, that it is <paramref name="errorId"/> and
        ''' that it points at the piece of source text <paramref name="squiggledText"/>.
        ''' </summary>
        Private Shared Sub AssertSingleError(compilation As VisualBasicCompilation, errorId As ERRID, squiggledText As String)
            Dim diagnostics = compilation.GetDiagnostics()
            Assert.Equal(1, diagnostics.Length)
            Dim [error] = diagnostics(0)
            Assert.Equal(ErrorCode(errorId), [error].Id)
            Assert.Equal(squiggledText, [error].Location.SourceTree.GetText().ToString([error].Location.SourceSpan))
        End Sub

        ''' <summary>
        ''' 'Await' is not allowed inside a 'Catch' statement (BC36943, <see cref="ERRID.ERR_BadAwaitInTryHandler"/>).
        ''' A method body gets that check from <c>BindMethodBlock</c>; top level statements are bound through the
        ''' initializer path, whose synthesized host body is a stub, so the check has to be run there as well.
        ''' </summary>
        <Fact>
        Public Sub TopLevelAwaitInCatch_ReportsBadAwaitInTryHandler()
            Dim c = CreateSubmission(
                "Try" & vbLf &
                "    System.Console.WriteLine(""TRY"")" & vbLf &
                "Catch ex As System.Exception" & vbLf &
                "    Await System.Threading.Tasks.Task.Delay(1)" & vbLf &
                "End Try", options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            AssertSingleError(c, ERRID.ERR_BadAwaitInTryHandler, AwaitDelayText)
        End Sub

        <Fact>
        Public Sub TopLevelAwaitInFinally_ReportsBadAwaitInTryHandler()
            Dim c = CreateSubmission(
                "Try" & vbLf &
                "    System.Console.WriteLine(""TRY"")" & vbLf &
                "Finally" & vbLf &
                "    Await System.Threading.Tasks.Task.Delay(1)" & vbLf &
                "End Try", options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            AssertSingleError(c, ERRID.ERR_BadAwaitInTryHandler, AwaitDelayText)
        End Sub

        ''' <summary>
        ''' The 'SyncLock' sub-shape compiles and produces a broken artifact instead of crashing, so only this
        ''' diagnostic keeps it out of code generation.
        ''' </summary>
        <Fact>
        Public Sub TopLevelAwaitInSyncLock_ReportsBadAwaitInTryHandler()
            Dim c = CreateSubmission(
                "Dim gate As New Object" & vbLf &
                "SyncLock gate" & vbLf &
                "    Await System.Threading.Tasks.Task.Delay(1)" & vbLf &
                "End SyncLock", options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            AssertSingleError(c, ERRID.ERR_BadAwaitInTryHandler, AwaitDelayText)
        End Sub

        ''' <summary>'Await' inside the 'Try' block itself stays legal.</summary>
        <Fact>
        Public Sub TopLevelAwaitInTryBlock_NoDiagnostics()
            Dim c = CreateSubmission(
                "Try" & vbLf &
                "    Await System.Threading.Tasks.Task.Delay(1)" & vbLf &
                "Catch ex As System.Exception" & vbLf &
                "    System.Console.WriteLine(""CATCH"")" & vbLf &
                "End Try", options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            c.VerifyDiagnostics()
        End Sub

        ''' <summary>'Await' inside a 'Using' block stays legal - 'Using' is not part of the BC36943 family.</summary>
        <Fact>
        Public Sub TopLevelAwaitInUsing_NoDiagnostics()
            Dim c = CreateSubmission(
                "Using d As New System.IO.MemoryStream()" & vbLf &
                "    Await System.Threading.Tasks.Task.Delay(1)" & vbLf &
                "End Using", options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            c.VerifyDiagnostics()
        End Sub

        ''' <summary>An ordinary async method keeps reporting one diagnostic per offending 'Await'.</summary>
        <Fact>
        Public Sub AsyncMethodAwaitInCatchAndFinally_ReportsOneDiagnosticEach()
            Dim c = CreateSubmission(
                "Class C" & vbLf &
                "    Async Function F() As System.Threading.Tasks.Task" & vbLf &
                "        Try" & vbLf &
                "            Await System.Threading.Tasks.Task.Delay(1)" & vbLf &
                "        Catch ex As System.Exception" & vbLf &
                "            Await System.Threading.Tasks.Task.Delay(1)" & vbLf &
                "        Finally" & vbLf &
                "            Await System.Threading.Tasks.Task.Delay(1)" & vbLf &
                "        End Try" & vbLf &
                "    End Function" & vbLf &
                "End Class", options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            Dim diagnostics = c.GetDiagnostics()
            Assert.Equal(2, diagnostics.Length)
            Assert.True(diagnostics.All(Function(d) d.Id = ErrorCode(ERRID.ERR_BadAwaitInTryHandler)),
                        String.Join(" | ", diagnostics))
        End Sub

        ''' <summary>
        ''' The top level check runs with the 'Await' position check only: the 'On Error' related diagnostics of the
        ''' same walker keep out of top level code.
        ''' </summary>
        <Fact>
        Public Sub TopLevelOnErrorResumeNextWithTry_KeepsItsExistingDiagnostics()
            Dim c = CreateSubmission(
                "On Error Resume Next" & vbLf &
                "Try" & vbLf &
                "    System.Console.WriteLine(""TRY"")" & vbLf &
                "Catch ex As System.Exception" & vbLf &
                "    System.Console.WriteLine(""CATCH"")" & vbLf &
                "End Try", options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            ' The statement itself keeps the one diagnostic top level code always had for an 'On Error'
            ' inside an async context (BC36956, reported by BindOnErrorStatement and unrelated to the walker).
            ' Pinning it keeps the lock from passing vacuously if that diagnostic ever disappears.
            AssertSingleError(c, ERRID.ERR_ResumablesCannotContainOnError, "On Error Resume Next")

            Dim diagnostics = c.GetDiagnostics()
            Assert.DoesNotContain(diagnostics, Function(d) d.Id = ErrorCode(ERRID.ERR_TryAndOnErrorDoNotMix))
            Assert.DoesNotContain(diagnostics, Function(d) d.Id = ErrorCode(ERRID.ERR_BadAwaitInTryHandler))
        End Sub

        ''' <summary>
        ''' The walker does not dive into lambdas, so an 'Await' inside a lambda stays legal even when the lambda sits
        ''' inside a 'SyncLock' block.
        ''' </summary>
        <Fact>
        Public Sub TopLevelAwaitInLambdaInsideSyncLock_NoDiagnostics()
            Dim c = CreateSubmission(
                "Dim gate As New Object" & vbLf &
                "Dim t As System.Threading.Tasks.Task(Of Integer)" & vbLf &
                "SyncLock gate" & vbLf &
                "    t = System.Threading.Tasks.Task.Run(Async Function()" & vbLf &
                "                                              Await System.Threading.Tasks.Task.FromResult(1)" & vbLf &
                "                                          End Function)" & vbLf &
                "End SyncLock", options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            c.VerifyDiagnostics()
        End Sub

        ''' <summary>
        ''' A shared member of a script class has no instance to offer, so an implicit reference to an instance member
        ''' is the ordinary BC30369 (ERR_BadInstanceMemberAccess) that an ordinary class reports as well.
        ''' </summary>
        <Fact>
        Public Sub TopLevelSharedMethodBody_ReadingInstanceField_ReportsBadInstanceMemberAccess()
            Dim c = CreateSubmission(
                "Dim sx As Integer = 5" & vbLf &
                "Shared Sub S()" & vbLf &
                "    System.Console.WriteLine(sx)" & vbLf &
                "End Sub" & vbLf &
                "S()", options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            AssertSingleError(c, ERRID.ERR_BadInstanceMemberAccess, "sx")
        End Sub

        ''' <summary>The shared initializer shape: calling an instance method relies on an implicit Me.</summary>
        <Fact>
        Public Sub TopLevelSharedFieldInitializer_CallingInstanceMethod_ReportsBadInstanceMemberAccess()
            Dim c = CreateSubmission(
                "Function F() As Integer" & vbLf &
                "    Return 3" & vbLf &
                "End Function" & vbLf &
                "Shared Dim y As Integer = F()", options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            AssertSingleError(c, ERRID.ERR_BadInstanceMemberAccess, "F")
        End Sub

        ''' <summary>The shared property initializer shape, which binds against the property symbol.</summary>
        <Fact>
        Public Sub TopLevelSharedPropertyInitializer_CallingInstanceMethod_ReportsBadInstanceMemberAccess()
            Dim c = CreateSubmission(
                "Function F() As Integer" & vbLf &
                "    Return 3" & vbLf &
                "End Function" & vbLf &
                "Shared ReadOnly Property P As Integer = F()", options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            AssertSingleError(c, ERRID.ERR_BadInstanceMemberAccess, "F")
        End Sub

        ''' <summary>An implicit reference made from an instance member of the script class stays legal.</summary>
        <Fact>
        Public Sub TopLevelInstanceMethodBody_ReadingInstanceField_NoDiagnostics()
            Dim c = CreateSubmission(
                "Dim sx As Integer = 5" & vbLf &
                "Sub S()" & vbLf &
                "    System.Console.WriteLine(sx)" & vbLf &
                "End Sub" & vbLf &
                "S()", options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            c.VerifyDiagnostics()
        End Sub

        ''' <summary>A top level statement is bound by the '&lt;Initialize&gt;' method, which is not shared.</summary>
        <Fact>
        Public Sub TopLevelStatement_ReadingInstanceField_NoDiagnostics()
            Dim c = CreateSubmission(
                "Dim sx As Integer = 5" & vbLf &
                "System.Console.WriteLine(sx)", options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            c.VerifyDiagnostics()
        End Sub

        ''' <summary>An explicit 'Me' in a script class keeps BC36966.</summary>
        <Fact>
        Public Sub TopLevelExplicitMe_ReportsKeywordNotAllowedInScript()
            Dim c = CreateSubmission(
                "Dim sx As Integer = 5" & vbLf &
                "Sub S()" & vbLf &
                "    System.Console.WriteLine(Me.sx)" & vbLf &
                "End Sub", options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            AssertSingleError(c, ERRID.ERR_KeywordNotAllowedInScript, "Me")
        End Sub

        ''' <summary>An ordinary class keeps reporting BC30369 for both shapes.</summary>
        <Fact>
        Public Sub OrdinaryClassSharedMembers_StillReportBadInstanceMemberAccess()
            Dim c = CreateSubmission(
                "Class C" & vbLf &
                "    Public x As Integer = 5" & vbLf &
                "    Public Shared Sub S()" & vbLf &
                "        System.Console.WriteLine(x)" & vbLf &
                "    End Sub" & vbLf &
                "End Class" & vbLf &
                "C.S()", options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            AssertSingleError(c, ERRID.ERR_BadInstanceMemberAccess, "x")
        End Sub

#End Region

#Region "Top level scripts: 'Await' in a shared initializer (F06)"

        ''' <summary>
        ''' A shared field or property initializer is executed by the shared constructor of the script class, which is
        ''' synchronous, so 'Await' cannot be honored there - it used to survive into code generation and trip the
        ''' assertion in <c>CodeGen\EmitExpression.vb:207</c>.
        ''' </summary>
        <Fact>
        Public Sub TopLevelSharedFieldInitializerAwait_ReportsBadAwaitInSharedInitializer()
            Dim c = CreateSubmission(
                "Shared Dim x = Await System.Threading.Tasks.Task.FromResult(1)",
                options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            AssertSingleError(c, ERRID.ERR_BadAwaitInSharedInitializer, AwaitKeywordText)
        End Sub

        <Fact>
        Public Sub TopLevelSharedReadOnlyFieldInitializerAwait_ReportsBadAwaitInSharedInitializer()
            Dim c = CreateSubmission(
                "Shared ReadOnly x As Integer = Await System.Threading.Tasks.Task.FromResult(1)",
                options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            AssertSingleError(c, ERRID.ERR_BadAwaitInSharedInitializer, AwaitKeywordText)
        End Sub

        ''' <summary>
        ''' The property shape: the initializer binds against the <c>PropertySymbol</c> of the script class, not against
        ''' a backing field, so the check has to accept both member kinds.
        ''' </summary>
        <Fact>
        Public Sub TopLevelSharedPropertyInitializerAwait_ReportsBadAwaitInSharedInitializer()
            Dim c = CreateSubmission(
                "Shared ReadOnly Property P As Integer = Await System.Threading.Tasks.Task.FromResult(7)",
                options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            AssertSingleError(c, ERRID.ERR_BadAwaitInSharedInitializer, AwaitKeywordText)
        End Sub

        ''' <summary>An array field needed no '=' to require the shared constructor, and its upper bound is 'Await'-checked as well.</summary>
        <Fact>
        Public Sub TopLevelSharedArrayFieldBoundsAwait_ReportsBadAwaitInSharedInitializer()
            Dim c = CreateSubmission(
                "Shared Dim arr(Await System.Threading.Tasks.Task.FromResult(2)) As Integer",
                options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            AssertSingleError(c, ERRID.ERR_BadAwaitInSharedInitializer, AwaitKeywordText)
        End Sub

        ''' <summary>An instance initializer runs inside the asynchronous '&lt;Initialize&gt;' method, so 'Await' stays legal.</summary>
        <Fact>
        Public Sub TopLevelInstanceFieldInitializerAwait_NoDiagnostics()
            Dim c = CreateSubmission(
                "Dim y = Await System.Threading.Tasks.Task.FromResult(2)",
                options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            c.VerifyDiagnostics()
        End Sub

        <Fact>
        Public Sub TopLevelInstancePropertyInitializerAwait_NoDiagnostics()
            Dim c = CreateSubmission(
                "ReadOnly Property P As Integer = Await System.Threading.Tasks.Task.FromResult(7)",
                options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            c.VerifyDiagnostics()
        End Sub

        ''' <summary>
        ''' An initializer of a nested (non-script) type is not an asynchronous context at all, so it keeps reporting
        ''' BC36937 and is not taken over by the script specific check.
        ''' </summary>
        <Fact>
        Public Sub NestedClassSharedInitializerAwait_ReportsBadAwaitNotInAsyncMethodOrLambda()
            Dim c = CreateSubmission(
                "Class C" & vbLf &
                "    Shared s As Integer = Await System.Threading.Tasks.Task.FromResult(9)" & vbLf &
                "End Class", options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            AssertSingleError(c, ERRID.ERR_BadAwaitNotInAsyncMethodOrLambda, AwaitKeywordText)
        End Sub

#End Region

#Region "Top level scripts: explicit MyBase (script-top-level-crashes-2, U4)"

        ''' <summary>
        ''' A submission class has no base type, so the error path of an explicit 'MyBase' has to fall back to an error
        ''' type: the diagnostic is reported instead of terminating the process (the bound node used to be constructed
        ''' with a <c>Nothing</c> type and tripped its non-null assertion).
        ''' </summary>
        <Fact>
        Public Sub TopLevelBareMyBase_ReportsKeywordNotAllowedInScript()
            Dim c = CreateSubmission("MyBase.ToString()", options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            AssertSingleError(c, ERRID.ERR_KeywordNotAllowedInScript, "MyBase")
        End Sub

        <Fact>
        Public Sub TopLevelMyBaseInInstanceMethod_ReportsKeywordNotAllowedInScript()
            Dim c = CreateSubmission(
                "Sub Go()" & vbLf &
                "    System.Console.WriteLine(MyBase.ToString())" & vbLf &
                "End Sub" & vbLf &
                "Go()", options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            AssertSingleError(c, ERRID.ERR_KeywordNotAllowedInScript, "MyBase")
        End Sub

        <Fact>
        Public Sub TopLevelMyBaseInSharedMethod_ReportsKeywordNotAllowedInScript()
            Dim c = CreateSubmission(
                "Shared Sub Go()" & vbLf &
                "    System.Console.WriteLine(MyBase.ToString())" & vbLf &
                "End Sub" & vbLf &
                "Go()", options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            AssertSingleError(c, ERRID.ERR_KeywordNotAllowedInScript, "MyBase")
        End Sub

        ''' <summary>
        ''' The control: an ordinary class with a base type keeps its working 'MyBase' (the same shape as the
        ''' <c>mybase-ordinary-class</c> probe).
        ''' </summary>
        <Fact>
        Public Sub OrdinaryClassMyBase_StillWorks()
            Dim source =
                "Class Base2" & vbLf &
                "    Public Overrides Function ToString() As String" & vbLf &
                "        Return ""B""" & vbLf &
                "    End Function" & vbLf &
                "End Class" & vbLf &
                "Class Derived" & vbLf &
                "    Inherits Base2" & vbLf &
                "    Public Function Go() As String" & vbLf &
                "        Return MyBase.ToString()" & vbLf &
                "    End Function" & vbLf &
                "End Class" & vbLf &
                "Module Entry" & vbLf &
                "    Sub Main()" & vbLf &
                "        System.Console.WriteLine(New Derived().Go())" & vbLf &
                "    End Sub" & vbLf &
                "End Module"

            Dim c = CreateCompilationWithMscorlib461AndVBRuntime(source, options:=TestOptions.ReleaseExe)

            CompileAndVerify(c, expectedOutput:="B").VerifyDiagnostics()
        End Sub

#End Region

#Region "Top level scripts: instance constructors (script-top-level-crashes-2, U1)"

        ''' <summary>
        ''' The host creates the submission instance and the compiler synthesizes its constructor, so a declared
        ''' instance constructor has nothing to run and cannot take that member slot. It used to be added next to
        ''' the synthesized one, which ended in the lexical order assertion of <c>LexicalOrderSymbolComparer</c>
        ''' (constructor first in the file) or in <c>NamedTypeSymbol.GetScriptConstructor</c> throwing
        ''' <c>InvalidOperationException</c> (anything before the constructor).
        ''' </summary>
        <Fact>
        Public Sub TopLevelParameterlessInstanceConstructor_ReportsSubmissionCannotDeclareInstanceConstructor()
            Dim c = CreateSubmission(
                "Sub New()" & vbLf &
                "End Sub",
                options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            AssertSingleError(c, ERRID.ERR_SubmissionCannotDeclareInstanceConstructor, "Sub New()")
        End Sub

        <Fact>
        Public Sub TopLevelInstanceConstructorWithParameters_ReportsSubmissionCannotDeclareInstanceConstructor()
            Dim c = CreateSubmission(
                "Sub New(x As Integer)" & vbLf &
                "End Sub",
                options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            AssertSingleError(c, ERRID.ERR_SubmissionCannotDeclareInstanceConstructor, "Sub New(x As Integer)")
        End Sub

        ''' <summary>
        ''' The access modifiers do not change the decision, and the diagnostic is anchored to the declaration.
        ''' </summary>
        <Fact>
        Public Sub TopLevelInstanceConstructorWithAccessModifier_ReportsSubmissionCannotDeclareInstanceConstructor()
            For Each modifier In {"Protected", "Private", "Public"}
                Dim c = CreateSubmission(
                    modifier & " Sub New()" & vbLf &
                    "End Sub",
                    options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

                AssertSingleError(c, ERRID.ERR_SubmissionCannotDeclareInstanceConstructor, modifier & " Sub New()")
            Next
        End Sub

        ''' <summary>
        ''' The shape whose declaration is not the first thing in the file - the one that used to throw out of
        ''' <c>GetScriptConstructor</c> instead of asserting.
        ''' </summary>
        <Fact>
        Public Sub TopLevelInstanceConstructorAfterStatement_ReportsSubmissionCannotDeclareInstanceConstructor()
            Dim c = CreateSubmission(
                "System.Console.WriteLine(""X"")" & vbLf &
                "Sub New()" & vbLf &
                "End Sub",
                options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            AssertSingleError(c, ERRID.ERR_SubmissionCannotDeclareInstanceConstructor, "Sub New()")
        End Sub

        ''' <summary>A shared constructor is not the synthesized one, so it keeps compiling.</summary>
        <Fact>
        Public Sub TopLevelSharedConstructor_NoDiagnostics()
            Dim c = CreateSubmission(
                "Shared Sub New()" & vbLf &
                "End Sub",
                options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            c.VerifyDiagnostics()
        End Sub

        ''' <summary>A nested class of a script class may declare an instance constructor as usual.</summary>
        <Fact>
        Public Sub NestedClassInstanceConstructor_NoDiagnostics()
            Dim c = CreateSubmission(
                "Class Widget" & vbLf &
                "    Public Sub New()" & vbLf &
                "    End Sub" & vbLf &
                "End Class" & vbLf &
                "Dim w As New Widget" & vbLf &
                "System.Console.WriteLine(""OK"")",
                options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            c.VerifyDiagnostics()
        End Sub

        ''' <summary>An ordinary class keeps declaring constructors with either form.</summary>
        <Fact>
        Public Sub OrdinaryClassInstanceConstructors_StillCompile()
            Dim source =
                "Class Widget" & vbLf &
                "    Sub New()" & vbLf &
                "    End Sub" & vbLf &
                "    Sub New(x As Integer)" & vbLf &
                "    End Sub" & vbLf &
                "End Class" & vbLf &
                "Module Entry" & vbLf &
                "    Sub Main()" & vbLf &
                "        Dim w As New Widget" & vbLf &
                "        Dim v As New Widget(1)" & vbLf &
                "        System.Console.WriteLine(""OK"")" & vbLf &
                "    End Sub" & vbLf &
                "End Module"

            Dim c = CreateCompilationWithMscorlib461AndVBRuntime(source, options:=TestOptions.ReleaseExe)

            CompileAndVerify(c, expectedOutput:="OK").VerifyDiagnostics()
        End Sub

        ''' <summary>
        ''' A script class that is not a submission class: the compiler synthesizes its constructor and the
        ''' generated entry point constructs the script instance, the same relationship the host has with a
        ''' submission, so a declared instance constructor takes the synthesized one's member slot here as well.
        ''' </summary>
        Private Shared Function CreateScriptClassCompilation(source As String) As VisualBasicCompilation
            Return VisualBasicCompilation.Create(
                "ScriptClassInstanceConstructor",
                {SyntaxFactory.ParseSyntaxTree(source, options:=TestOptions.Script)},
                {MscorlibRef_v4_0_30316_17626, MsvbRef_v4_0_30319_17929},
                New VisualBasicCompilationOptions(OutputKind.ConsoleApplication).WithScriptClassName("Script"))
        End Function

        ''' <summary>
        ''' Asserts that the compilation reports one BC37342 on <paramref name="squiggledText"/>, and that analysis
        ''' and emit survive it: both threw <c>InvalidCastException</c> out of
        ''' <c>NamedTypeSymbol.GetScriptConstructor</c> while the declared constructor reached the member table.
        ''' </summary>
        Private Shared Sub AssertScriptClassInstanceConstructorRejected(source As String, squiggledText As String)
            Dim c = CreateScriptClassCompilation(source)

            AssertSingleError(c, ERRID.ERR_SubmissionCannotDeclareInstanceConstructor, squiggledText)

            Dim result = c.Emit(peStream:=New System.IO.MemoryStream())
            Assert.False(result.Success)
        End Sub

        <Fact>
        Public Sub ScriptClassParameterlessInstanceConstructor_ReportsSubmissionCannotDeclareInstanceConstructor()
            AssertScriptClassInstanceConstructorRejected(
                "Sub New()" & vbLf &
                "End Sub",
                "Sub New()")
        End Sub

        <Fact>
        Public Sub ScriptClassInstanceConstructorWithParameters_ReportsSubmissionCannotDeclareInstanceConstructor()
            AssertScriptClassInstanceConstructorRejected(
                "Sub New(x As Integer)" & vbLf &
                "End Sub",
                "Sub New(x As Integer)")
        End Sub

        ''' <summary>The declaration is not the first thing in the file, so the entry point is generated first.</summary>
        <Fact>
        Public Sub ScriptClassInstanceConstructorAfterStatement_ReportsSubmissionCannotDeclareInstanceConstructor()
            AssertScriptClassInstanceConstructorRejected(
                "System.Console.WriteLine(""X"")" & vbLf &
                "Sub New()" & vbLf &
                "End Sub",
                "Sub New()")
        End Sub

        ''' <summary>
        ''' Discriminating contrast for the widened gate: an ordinary class nested in the same script class keeps
        ''' declaring instance constructors.
        ''' </summary>
        <Fact>
        Public Sub ScriptClassNestedClassInstanceConstructor_NoDiagnostics()
            Dim c = CreateScriptClassCompilation(
                "Class Widget" & vbLf &
                "    Public Sub New()" & vbLf &
                "    End Sub" & vbLf &
                "End Class" & vbLf &
                "Dim w As New Widget" & vbLf &
                "System.Console.WriteLine(""OK"")")

            c.VerifyDiagnostics()
        End Sub

#End Region

#Region "Top level scripts: branching out of a 'Finally' block (script-top-level-crashes-2, U5)"

        ''' <summary>
        ''' Asserts that the submission reports one BC30101 on <paramref name="squiggledText"/> and nothing else.
        ''' </summary>
        Private Shared Sub AssertSingleBranchOutOfFinally(source As String,
                                                          squiggledText As String,
                                                          Optional returnType As Type = Nothing)
            Dim c = CreateSubmission(source,
                                     options:=ScriptCompilationOptions(),
                                     parseOptions:=TestOptions.Script,
                                     returnType:=returnType)

            AssertSingleError(c, ERRID.ERR_BranchOutOfFinally, squiggledText)
        End Sub

        ''' <summary>
        ''' A branch out of a 'Finally' block is rejected by the control flow pass, which never ran on top level
        ''' statements: their host is the stub body of the synthesized script initializer. The branch survived into
        ''' code generation and the produced method was rejected by the runtime
        ''' (<c>InvalidProgramException</c>). The reported span is the 'GoTo' label, as in an ordinary method body.
        ''' </summary>
        <Fact>
        Public Sub TopLevelGoToOutOfFinally_ReportsBranchOutOfFinally()
            AssertSingleBranchOutOfFinally(
                "Try" & vbLf &
                "    System.Console.WriteLine(""T"")" & vbLf &
                "Finally" & vbLf &
                "    GoTo after" & vbLf &
                "End Try" & vbLf &
                "after:" & vbLf &
                "System.Console.WriteLine(""A"")",
                "after")
        End Sub

        ''' <summary>
        ''' Every branch kind out of the 'Finally', not just the 'GoTo'. Enumerating statement kinds is what would
        ''' miss the 'Return' / 'Exit' / 'Continue' family, so each one is pinned here.
        ''' </summary>
        <Fact>
        Public Sub TopLevelBranchOutOfFinally_ReportsBranchOutOfFinallyForEveryBranchKind()
            AssertSingleBranchOutOfFinally(
                "Try" & vbLf &
                "    System.Console.WriteLine(""T"")" & vbLf &
                "Finally" & vbLf &
                "    Return" & vbLf &
                "End Try",
                "Return")

            AssertSingleBranchOutOfFinally(
                "Try" & vbLf &
                "    System.Console.WriteLine(""T"")" & vbLf &
                "Finally" & vbLf &
                "    Return 3" & vbLf &
                "End Try",
                "Return 3",
                returnType:=GetType(Integer))

            AssertSingleBranchOutOfFinally(
                "For i As Integer = 1 To 3" & vbLf &
                "    Try" & vbLf &
                "    Finally" & vbLf &
                "        Exit For" & vbLf &
                "    End Try" & vbLf &
                "Next",
                "Exit For")

            AssertSingleBranchOutOfFinally(
                "While True" & vbLf &
                "    Try" & vbLf &
                "    Finally" & vbLf &
                "        Exit While" & vbLf &
                "    End Try" & vbLf &
                "End While",
                "Exit While")

            AssertSingleBranchOutOfFinally(
                "Do" & vbLf &
                "    Try" & vbLf &
                "    Finally" & vbLf &
                "        Exit Do" & vbLf &
                "    End Try" & vbLf &
                "Loop",
                "Exit Do")

            AssertSingleBranchOutOfFinally(
                "Select Case 1" & vbLf &
                "    Case 1" & vbLf &
                "        Try" & vbLf &
                "        Finally" & vbLf &
                "            Exit Select" & vbLf &
                "        End Try" & vbLf &
                "End Select",
                "Exit Select")

            AssertSingleBranchOutOfFinally(
                "For i As Integer = 1 To 3" & vbLf &
                "    Try" & vbLf &
                "    Finally" & vbLf &
                "        Continue For" & vbLf &
                "    End Try" & vbLf &
                "Next",
                "Continue For")
        End Sub

        ''' <summary>
        ''' The shapes where the 'Try/Finally' is not the top level statement itself: nested 'Finally', a 'Finally'
        ''' inside 'Using' or 'Catch', a 'Try' with several 'Catch' blocks, and a 'Finally' that follows an 'Await'.
        ''' </summary>
        ''' <summary>
        ''' A branch that leaves a 'Finally' nested inside another 'Finally' is reported once per enclosing
        ''' 'Finally' block: the control flow pass reports the leftover pending branch in every 'VisitFinallyBlock'
        ''' it propagates through. An ordinary method body shows the same duplication at the
        ''' <c>Compilation.GetDiagnostics</c> layer, so only the diagnostic identity and the anchor are asserted
        ''' here.
        ''' </summary>
        <Fact>
        Public Sub TopLevelNestedFinally_ReportsBranchOutOfFinally()
            Dim c = CreateSubmission(
                "Try" & vbLf &
                "    System.Console.WriteLine(""T"")" & vbLf &
                "Finally" & vbLf &
                "    Try" & vbLf &
                "    Finally" & vbLf &
                "        Return" & vbLf &
                "    End Try" & vbLf &
                "End Try", options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)

            Dim diagnostics = c.GetDiagnostics()
            Assert.NotEmpty(diagnostics)
            Assert.True(diagnostics.All(Function(d) d.Id = ErrorCode(ERRID.ERR_BranchOutOfFinally)),
                        String.Join(" | ", diagnostics.Select(Function(d) d.ToString())))

            Dim first = diagnostics(0)
            Assert.Equal("Return", first.Location.SourceTree.GetText().ToString(first.Location.SourceSpan))
        End Sub

        <Fact>
        Public Sub TopLevelNestedFinallyShapes_ReportBranchOutOfFinally()
            AssertSingleBranchOutOfFinally(
                "Using d As New System.IO.MemoryStream" & vbLf &
                "    Try" & vbLf &
                "    Finally" & vbLf &
                "        GoTo after" & vbLf &
                "    End Try" & vbLf &
                "End Using" & vbLf &
                "after:" & vbLf &
                "System.Console.WriteLine(""A"")",
                "after")

            AssertSingleBranchOutOfFinally(
                "Try" & vbLf &
                "    Throw New System.Exception()" & vbLf &
                "Catch ex As System.Exception" & vbLf &
                "    Try" & vbLf &
                "    Finally" & vbLf &
                "        GoTo after" & vbLf &
                "    End Try" & vbLf &
                "End Try" & vbLf &
                "after:" & vbLf &
                "System.Console.WriteLine(""A"")",
                "after")

            AssertSingleBranchOutOfFinally(
                "Try" & vbLf &
                "    System.Console.WriteLine(""T"")" & vbLf &
                "Catch ex As System.ArgumentException" & vbLf &
                "    System.Console.WriteLine(""A"")" & vbLf &
                "Catch ex As System.Exception" & vbLf &
                "    System.Console.WriteLine(""B"")" & vbLf &
                "Finally" & vbLf &
                "    GoTo after" & vbLf &
                "End Try" & vbLf &
                "after:" & vbLf &
                "System.Console.WriteLine(""A"")",
                "after")

            AssertSingleBranchOutOfFinally(
                "Await System.Threading.Tasks.Task.Yield()" & vbLf &
                "Try" & vbLf &
                "    System.Console.WriteLine(""T"")" & vbLf &
                "Finally" & vbLf &
                "    GoTo after" & vbLf &
                "End Try" & vbLf &
                "after:" & vbLf &
                "System.Console.WriteLine(""A"")",
                "after")
        End Sub

        ''' <summary>
        ''' The controls: branches out of a 'Try' or a 'Catch' block stay legal, a 'Finally' with no branch at all
        ''' stays legal, and a 'GoTo' whose target is inside the same 'Finally' block is not a branch out of it.
        ''' </summary>
        <Fact>
        Public Sub TopLevelBranchesThatStayInsideTheEnclosingRegion_NoDiagnostics()
            Dim outOfCatch = CreateSubmission(
                "Try" & vbLf &
                "    System.Console.WriteLine(""T"")" & vbLf &
                "Catch ex As System.Exception" & vbLf &
                "    GoTo after" & vbLf &
                "Finally" & vbLf &
                "    System.Console.WriteLine(""F"")" & vbLf &
                "End Try" & vbLf &
                "after:" & vbLf &
                "System.Console.WriteLine(""A"")",
                options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)
            outOfCatch.VerifyDiagnostics()

            Dim outOfUsingBody = CreateSubmission(
                "Using d As New System.IO.MemoryStream" & vbLf &
                "    Try" & vbLf &
                "        System.Console.WriteLine(""T"")" & vbLf &
                "    Finally" & vbLf &
                "        System.Console.WriteLine(""F"")" & vbLf &
                "    End Try" & vbLf &
                "    GoTo after" & vbLf &
                "End Using" & vbLf &
                "after:" & vbLf &
                "System.Console.WriteLine(""A"")",
                options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)
            outOfUsingBody.VerifyDiagnostics()

            Dim noBranch = CreateSubmission(
                "Try" & vbLf &
                "    System.Console.WriteLine(""T"")" & vbLf &
                "Finally" & vbLf &
                "    System.Console.WriteLine(""F"")" & vbLf &
                "End Try" & vbLf &
                "System.Console.WriteLine(""A"")",
                options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)
            noBranch.VerifyDiagnostics()

            Dim targetInsideFinally = CreateSubmission(
                "Try" & vbLf &
                "    System.Console.WriteLine(""T"")" & vbLf &
                "Finally" & vbLf &
                "    If True Then" & vbLf &
                "        GoTo skip" & vbLf &
                "    End If" & vbLf &
                "skip:" & vbLf &
                "    System.Console.WriteLine(""F"")" & vbLf &
                "End Try" & vbLf &
                "System.Console.WriteLine(""A"")",
                options:=ScriptCompilationOptions(), parseOptions:=TestOptions.Script)
            targetInsideFinally.VerifyDiagnostics()
        End Sub

#End Region

    End Class
End Namespace

