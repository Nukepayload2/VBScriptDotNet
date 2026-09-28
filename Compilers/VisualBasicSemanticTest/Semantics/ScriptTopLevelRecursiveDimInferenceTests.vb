' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' Issue 33 (script-top-level-recursive-dim-inference): when the initializer of a script top-level
' `Dim x = <expr>` refers back to its own field - directly (`Dim a = a`) or through a cycle
' (`Dim a = b` / `Dim b = a`) - no type can be inferred. The re-entrancy guard in
' SourceMemberFieldSymbol.TryComputeScriptFieldType now reports the existing BC30980 (same code and
' same message shape as the ordinary local-inference path in Binder_Expressions) and then still
' returns Nothing, so the field keeps degrading to Object. I.e. "report, then degrade" instead of the
' old "degrade in silence" (issue 33: zero compile diagnostics, run-time NRE / zero output).
'
' Every cell pins the EXACT diagnostic set (code + squiggled text + message arguments + start
' position), so neither a silent regression nor a secondary cascade (BC30451 / BC30512 / BC30456) nor
' an error-type fallback can pass unnoticed.
'
' Counts and positions were NOT assumed: they were dumped from this exact harness (CreateSubmission
' with TestOptions.Script) into tmp\probes-cyc\rd-f03\harness-dump.txt. Key measured fact: ONE
' connected cycle produces exactly ONE BC30980, on the identifier of the first field of that
' component whose type gets computed (README §三 constraint ③, P-012; C# side reports a single
' CS7019 for the same shape). Two independent cycles in one document produce TWO (see X3).
'
' The issue-32 cells (T1-T7, the inference itself) live in ScriptTopLevelDimInferenceTests and are
' deliberately untouched here; the non-script dual anchors are VariableTypeInference.vb:20 (self)
' and :52 (mutual).

Imports System.IO
Imports Microsoft.CodeAnalysis.Test.Utilities
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Roslyn.Test.Utilities

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests

    ''' <summary>
    ''' Script top-level `Dim` whose initializer refers back to its own field (issue 33).
    ''' Assertion style throughout: exact diagnostic set via VerifyDiagnostics / DiagnosticDescription.
    ''' </summary>
    Public Class ScriptTopLevelRecursiveDimInferenceTests
        Inherits BasicTestBase

        Private Shared Function ScriptOptions() As VisualBasicCompilationOptions
            Return New VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary).
                WithGlobalImports(GlobalImport.Parse("System", "Microsoft.VisualBasic"))
        End Function

        ''' <summary>Script compilation on the issue-32 harness path (TopLevelDimInferenceTests uses the same one).</summary>
        Private Shared Function Script(code As String, Optional inferOn As Boolean = True) As VisualBasicCompilation
            Dim options = ScriptOptions()
            If Not inferOn Then
                options = options.WithOptionInfer(False)
            End If

            Return CreateSubmission(code, options:=options, parseOptions:=TestOptions.Script)
        End Function

        ''' <summary>
        ''' The script-class field's type name ("Object" / "Int32"). Pins the second half of the
        ''' landing decision: BC30980 is reported and the field STILL degrades to Object, rather than
        ''' turning into an error type (which would change 32's return contract).
        ''' </summary>
        Private Shared Function FieldType(c As VisualBasicCompilation, name As String) As String
            Return DirectCast(c.ScriptClass.GetMembers(name).Single(), FieldSymbol).Type.Name
        End Function

        ''' <summary>
        ''' README §五 immunity guard: the new report point lives inside TryComputeScriptFieldType,
        ''' which is only reachable for ContainingType.IsScriptClass, so it must never fire on an
        ''' ordinary VB field or an ordinary method body. Complements the exact-set assertions (which
        ''' are what pin codes, positions and arguments) by naming the forbidden code explicitly.
        ''' </summary>
        Private Shared Sub AssertNoCircularInferenceAtAll(c As VisualBasicCompilation)
            Assert.Empty(c.GetDiagnostics().Where(Function(d) d.Id = "BC30980"))
        End Sub

#Region "RD-A: the cycle reports BC30980 (test-plan §一)"

        ' A1: direct self-reference. One BC30980 on the identifier itself (col 5 = right after "Dim "),
        ' message argument is the bare name (implementation constraint ②), and the field stays Object.
        <Fact>
        Public Sub A1_SelfReference_ReportsOneBC30980OnTheIdentifier()
            Dim c = Script("Dim a = a")

            c.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_CircularInference1, "a").WithArguments("a").WithLocation(1, 5))

            Assert.Equal("Object", FieldType(c, "a"))
        End Sub

        ' A2: mutual reference. Measured (harness-dump.txt, SHAPE A2-cycle): exactly ONE diagnostic, on
        ' `a` (the first field of the component), not one per field - one connected cycle hits the
        ' re-entrancy guard once (constraint ③ / P-012). Same count as the C# side's single CS7019 (1,5).
        <Fact>
        Public Sub A2_MutualReference_ReportsExactlyOneBC30980()
            Dim c = Script(
                "Dim a = b" & vbCrLf &
                "Dim b = a")

            c.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_CircularInference1, "a").WithArguments("a").WithLocation(1, 5))

            Assert.Equal("Object", FieldType(c, "a"))
            Assert.Equal("Object", FieldType(c, "b"))
        End Sub

        ' A2b: same cycle with the declarations swapped. Measured: the report follows the field whose
        ' type gets computed first (here `b`), i.e. it is not hard-wired to the name "a".
        <Fact>
        Public Sub A2b_MutualReferenceReversedOrder_ReportFollowsTheFirstBoundField()
            Dim c = Script(
                "Dim b = a" & vbCrLf &
                "Dim a = b")

            c.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_CircularInference1, "b").WithArguments("b").WithLocation(1, 5))
        End Sub

        ' A3: the cycle is also read. Measured (SHAPE A3-cycle-used): still ONE diagnostic - no
        ' BC30451/BC30512/BC30456 cascade, because the field degraded to Object (late-bound), not to an
        ' error type. The exact-set assertion below IS the no-cascade pin.
        <Fact>
        Public Sub A3_CycleThatIsAlsoRead_ReportsOnceWithoutSecondaryErrors()
            Dim c = Script(
                "Dim a = b" & vbCrLf &
                "Dim b = a" & vbCrLf &
                "Console.Write(a)")

            c.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_CircularInference1, "a").WithArguments("a").WithLocation(1, 5))
        End Sub

        ' A4: indirect cycle, three hops. Measured: one diagnostic at (1,5); no stack overflow, no hang.
        <Fact>
        Public Sub A4_IndirectThreeHopCycle_ReportsOnceAndDoesNotHang()
            Dim c = Script(
                "Dim a = b" & vbCrLf &
                "Dim b = c" & vbCrLf &
                "Dim c = a")

            c.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_CircularInference1, "a").WithArguments("a").WithLocation(1, 5))

            Assert.Equal("Object", FieldType(c, "a"))
            Assert.Equal("Object", FieldType(c, "c"))
        End Sub

        ' A4b: a longer chain (four hops) still produces a single report - the guard is per symbol, so
        ' chain length does not multiply diagnostics.
        <Fact>
        Public Sub A4b_IndirectFourHopCycle_StillReportsOnlyOnce()
            Dim c = Script(
                "Dim a = b" & vbCrLf &
                "Dim b = c" & vbCrLf &
                "Dim c = d" & vbCrLf &
                "Dim d = a")

            c.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_CircularInference1, "a").WithArguments("a").WithLocation(1, 5))
        End Sub

        ' A5: the reference runs through an expression rather than being a bare name. Measured: one
        ' diagnostic on `a` of line 2 (line 1 is the Option Infer On directive).
        <Fact>
        Public Sub A5_CycleThroughExpression_ReportsBC30980()
            Dim c = Script(
                "Option Infer On" & vbCrLf &
                "Dim a = If(True, a, 1)")

            c.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_CircularInference1, "a").WithArguments("a").WithLocation(2, 5))

            Assert.Equal("Object", FieldType(c, "a"))
        End Sub

        ' A6: nothing in the document ever reads the cyclic fields (no Console, no GetType). This is the
        ' shape issue 33 recorded as COMPLETELY silent before the fix (exit 0, zero bytes of output), so
        ' "nobody uses it" must not be a reason to stay quiet. Measured: both shapes report.
        <Fact>
        Public Sub A6_CycleThatNobodyReferences_StillReports()
            Dim selfRef = Script("Dim a = a")
            selfRef.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_CircularInference1, "a").WithArguments("a").WithLocation(1, 5))

            Dim cycle = Script(
                "Dim a = b" & vbCrLf &
                "Dim b = a")
            cycle.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_CircularInference1, "a").WithArguments("a").WithLocation(1, 5))

            ' Still degrades to Object (the old fallback), it is not turned into an error type.
            Assert.Equal("Object", FieldType(selfRef, "a"))
            Assert.Equal("Object", FieldType(cycle, "b"))
        End Sub

        ' E1: the diagnostic is a real error - the cyclic submission no longer emits. Measured:
        ' Emit().Success = False, and the emit diagnostic bag carries exactly this one BC30980.
        <Fact>
        Public Sub E1_SelfReference_DiagnosticIsErrorSeverityAndBlocksEmit()
            Dim result = DirectCast(Script("Dim a = a"), Compilation).Emit(New MemoryStream())

            Assert.False(result.Success)

            Dim reported = Assert.Single(result.Diagnostics.Where(Function(d) d.Id = "BC30980"))
            Assert.Equal(DiagnosticSeverity.Error, reported.Severity)
        End Sub

        ' S1/S2 (test-plan §五 口径): under Option Strict On the cyclic shape reports BC30980 AND the
        ' pre-existing BC30209 ("Strict On requires an 'As' clause"). Both codes land on the identifier
        ' (line 2, col 5). BC30209 is 32's own early-exit report point, NOT a secondary cascade of this
        ' task, and its count must not change (measured 4 -> 4 across the option matrix by RD-F02).
        <Fact>
        Public Sub S1_StrictOnSelf_ReportsBC30980TogetherWithThePreExistingBC30209()
            Dim c = Script(
                "Option Strict On" & vbCrLf &
                "Dim a = a")

            c.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_CircularInference1, "a").WithArguments("a").WithLocation(2, 5),
                Diagnostic(ERRID.ERR_StrictDisallowImplicitObject, "a").WithLocation(2, 5))
        End Sub

        <Fact>
        Public Sub S2_StrictOnCycle_ReportsBC30980AndBC30209OnceEach()
            Dim c = Script(
                "Option Strict On" & vbCrLf &
                "Dim a = b" & vbCrLf &
                "Dim b = a")

            c.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_CircularInference1, "a").WithArguments("a").WithLocation(2, 5),
                Diagnostic(ERRID.ERR_StrictDisallowImplicitObject, "a").WithLocation(2, 5))
        End Sub

        ' S3 (constraint ④ under Strict On): with Option Infer Off the guard is never reached, so the
        ' Strict-On shape keeps reporting only the pre-existing BC30209 and nothing about inference.
        <Fact>
        Public Sub S3_StrictOnWithInferOff_ReportsOnlyThePreExistingBC30209()
            Dim c = Script(
                "Option Strict On" & vbCrLf &
                "Option Infer Off" & vbCrLf &
                "Dim a = a")

            c.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_StrictDisallowImplicitObject, "a").WithLocation(3, 5))

            AssertNoCircularInferenceAtAll(c)
        End Sub

        ' X3: two INDEPENDENT cycles in one document produce one report each (measured: `a` at (1,5) and
        ' `c` at (3,5)). Pairs with A2/A4 to pin "one per connected component" - not "one per document",
        ' and not "one per field".
        <Fact>
        Public Sub X3_TwoSeparateCycles_ReportOncePerComponent()
            Dim c = Script(
                "Dim a = b" & vbCrLf &
                "Dim b = a" & vbCrLf &
                "Dim c = d" & vbCrLf &
                "Dim d = c")

            c.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_CircularInference1, "a").WithArguments("a").WithLocation(1, 5),
                Diagnostic(ERRID.ERR_CircularInference1, "c").WithArguments("c").WithLocation(3, 5))
        End Sub

        ' X4: a self-referencing cycle that IS read still reports only once (no use-before-assignment or
        ' late-bound diagnostics on top).
        <Fact>
        Public Sub X4_SelfReferenceThatIsAlsoRead_StillReportsOnlyOnce()
            Dim c = Script(
                "Dim a = a" & vbCrLf &
                "Console.Write(a)")

            c.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_CircularInference1, "a").WithArguments("a").WithLocation(1, 5))
        End Sub

#End Region

#Region "RD-B: neighbouring shapes must not get stricter (test-plan §二)"

        ' X1 (counter-lock, script side of C1): an explicit As clause is never inferred, so a
        ' self-referencing initializer on a DECLARED type stays completely silent and keeps the
        ' declared type. Measured: zero diagnostics, a:Int32.
        <Fact>
        Public Sub X1_ScriptFieldWithExplicitAsAndSelfInitializer_StaysSilent()
            Dim c = Script("Dim a As Integer = a")

            c.VerifyDiagnostics()
            Assert.Equal("Int32", FieldType(c, "a"))
            AssertNoCircularInferenceAtAll(c)
        End Sub

        ' X2: same for the type-character shape (`Dim a% = a%`), which the shape gates return early on.
        ' Measured: zero diagnostics, a:Int32.
        <Fact>
        Public Sub X2_ScriptFieldWithTypeCharacterAndSelfInitializer_StaysSilent()
            Dim c = Script("Dim a% = a%")

            c.VerifyDiagnostics()
            Assert.Equal("Int32", FieldType(c, "a"))
            AssertNoCircularInferenceAtAll(c)
        End Sub

        ' B1: plain explicit As (issue 32's shape, see also T4 in ScriptTopLevelDimInferenceTests).
        <Fact>
        Public Sub B1_ExplicitAs_StillNoDiagnostics()
            Dim c = Script("Dim x As Integer = 5")

            c.VerifyDiagnostics()
            Assert.Equal("Int32", FieldType(c, "x"))
            AssertNoCircularInferenceAtAll(c)
        End Sub

        ' B2: Option Infer OFF - the guard is never armed (constraint ④), so the cyclic shapes stay as
        ' silent as they were before this task, and stay Object.
        <Fact>
        Public Sub B2_OptionInferOff_CyclicShapesStaySilentAndObject()
            Dim selfRef = Script("Dim a = a", inferOn:=False)
            selfRef.VerifyDiagnostics()
            Assert.Equal("Object", FieldType(selfRef, "a"))

            Dim cycle = Script(
                "Dim a = b" & vbCrLf &
                "Dim b = a", inferOn:=False)
            cycle.VerifyDiagnostics()
            Assert.Equal("Object", FieldType(cycle, "a"))

            AssertNoCircularInferenceAtAll(selfRef)
            AssertNoCircularInferenceAtAll(cycle)
        End Sub

        ' B3: `Dim a = 5` really inferred Integer (issue 32's T1), and the report point does not leak
        ' into the non-cyclic inference path.
        <Fact>
        Public Sub B3_InferredInteger_MemberAccessStillReportsBC30456()
            Dim c = Script(
                "Dim a = 5" & vbCrLf &
                "Console.Write(a.Length)")

            c.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_NameNotMember2, "a.Length").WithArguments("Length", "Integer").WithLocation(2, 15))

            Assert.Equal("Int32", FieldType(c, "a"))
            AssertNoCircularInferenceAtAll(c)
        End Sub

        ' B4: a Nothing initializer takes the pre-inference exit (`= Nothing` stays Object, silent).
        <Fact>
        Public Sub B4_NothingInitializer_StaysSilentAndObject()
            Dim c = Script("Dim a = Nothing")

            c.VerifyDiagnostics()
            Assert.Equal("Object", FieldType(c, "a"))
            AssertNoCircularInferenceAtAll(c)
        End Sub

        ' Sentinel (test-plan §一 last row, README §一 comparison row): a NON-cyclic forward reference
        ' keeps the inferred type and stays silent.
        <Fact>
        Public Sub B5_NonCyclicForwardReference_UnchangedAndSilent()
            Dim c = Script(
                "Dim a = b" & vbCrLf &
                "Dim b = 5")

            c.VerifyDiagnostics()
            Assert.Equal("Int32", FieldType(c, "a"))
            Assert.Equal("Int32", FieldType(c, "b"))
            AssertNoCircularInferenceAtAll(c)
        End Sub

#End Region

#Region "RD-C: non-script immunity - hard constraint (README §五, test-plan §三)"

        Private Shared Function Regular(source As String, path As String) As VisualBasicCompilation
            Return CreateCompilationWithMscorlib40AndVBRuntime(
                {VisualBasicSyntaxTree.ParseText(source, path:=path)}, options:=TestOptions.ReleaseDll)
        End Function

        ' C1: an ordinary class field (a syntax-level As clause is mandatory there). Measured: zero
        ' diagnostics; the new report point is unreachable for it.
        <Fact>
        Public Sub C1_RegularClassField_SelfReferencingInitializerUnchanged()
            Dim c = Regular(
                "Class C1" & vbCrLf &
                "    Private f As Integer = f" & vbCrLf &
                "End Class", path:="c1.vb")

            c.VerifyDiagnostics()
            AssertNoCircularInferenceAtAll(c)
        End Sub

        ' C2a/C2b: in an ordinary method body the same shape cannot even form a cycle - member lookup
        ' respects declaration order, so BC32000 is what users get, under both Option Explicit settings
        ' (README §一: the report is a scope report, not an Explicit one). BC30980 must not replace it.
        <Fact>
        Public Sub C2a_RegularMethodBody_MutualReference_StillReportsBC32000_ExplicitOn()
            Dim c = Regular(
                "Option Explicit On" & vbCrLf &
                "Module C2a" & vbCrLf &
                "    Sub M()" & vbCrLf &
                "        Dim a = b" & vbCrLf &
                "        Dim b = 1" & vbCrLf &
                "    End Sub" & vbCrLf &
                "End Module", path:="c2a.vb")

            c.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_UseOfLocalBeforeDeclaration1, "b").WithArguments("b").WithLocation(4, 17))
            AssertNoCircularInferenceAtAll(c)
        End Sub

        <Fact>
        Public Sub C2b_RegularMethodBody_MutualReference_StillReportsBC32000_ExplicitOff()
            Dim c = Regular(
                "Option Explicit Off" & vbCrLf &
                "Module C2b" & vbCrLf &
                "    Sub M()" & vbCrLf &
                "        Dim a = b" & vbCrLf &
                "        Dim b = 1" & vbCrLf &
                "    End Sub" & vbCrLf &
                "End Module", path:="c2b.vb")

            c.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_UseOfLocalBeforeDeclaration1, "b").WithArguments("b").WithLocation(4, 17))
            AssertNoCircularInferenceAtAll(c)
        End Sub

        ' C2c: the ordinary LOCAL self-reference keeps its own pre-existing BC30980 (Binder_Expressions,
        ' anchored by VariableTypeInference.vb:20) plus the definite-assignment warning - exactly TWO
        ' diagnostics, i.e. the field path adds nothing on top of the local path.
        <Fact>
        Public Sub C2c_RegularMethodBody_SelfReference_KeepsExactlyTheLocalDiagnostics()
            Dim c = Regular(
                "Module C2c" & vbCrLf &
                "    Sub M()" & vbCrLf &
                "        Dim i = i" & vbCrLf &
                "    End Sub" & vbCrLf &
                "End Module", path:="c2c.vb")

            c.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_CircularInference1, "i").WithArguments("i").WithLocation(3, 17),
                Diagnostic(ERRID.WRN_DefAsgUseNullRef, "i").WithArguments("i").WithLocation(3, 17))
        End Sub

        ' C3a: a Class written by the user INSIDE a script tree. Its fields are not script-class members
        ' (IsScriptClass is per containing type, not per tree), so the shape behaves like C1.
        <Fact>
        Public Sub C3a_UserWrittenClassInsideScriptTree_Unchanged()
            Dim c = Script(
                "Class C3a" & vbCrLf &
                "    Private f As Integer = f" & vbCrLf &
                "End Class")

            c.VerifyDiagnostics()
            AssertNoCircularInferenceAtAll(c)
        End Sub

        ' C3b: an ordinary method body of a user-written Module inside a script tree - like C2a/C2b.
        <Fact>
        Public Sub C3b_UserWrittenModuleMethodBodyInsideScriptTree_StillReportsBC32000()
            Dim c = Script(
                "Module C3b" & vbCrLf &
                "    Sub M()" & vbCrLf &
                "        Dim a = b" & vbCrLf &
                "        Dim b = 1" & vbCrLf &
                "    End Sub" & vbCrLf &
                "End Module")

            c.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_UseOfLocalBeforeDeclaration1, "b").WithArguments("b").WithLocation(3, 17))
            AssertNoCircularInferenceAtAll(c)
        End Sub

        ' C3c: the differential pair of C2c - a user-written class method inside a SCRIPT tree reports
        ' byte-for-byte what the ordinary compilation (C2c) reports: the local path's BC30980 plus the
        ' definite-assignment warning, and no second report from the script field path.
        <Fact>
        Public Sub C3c_UserWrittenClassMethodBodyInsideScriptTree_SameSetAsRegular()
            Dim c = Script(
                "Class C3c" & vbCrLf &
                "    Sub M()" & vbCrLf &
                "        Dim i = i" & vbCrLf &
                "    End Sub" & vbCrLf &
                "End Class")

            c.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_CircularInference1, "i").WithArguments("i").WithLocation(3, 17),
                Diagnostic(ERRID.WRN_DefAsgUseNullRef, "i").WithArguments("i").WithLocation(3, 17))
        End Sub

#End Region

    End Class

End Namespace
