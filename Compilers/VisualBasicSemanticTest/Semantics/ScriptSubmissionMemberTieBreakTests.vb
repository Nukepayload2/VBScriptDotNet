' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' SW-F01 (submission-member-redeclaration-tiebreak, test-plan.md §一): the decision-layer tie-break of
' OverloadResolution.CombineCandidates ends with "Position in interactive submission chain. The last definition
' wins.", the VB counterpart of the C# rule (CSharp OverloadResolution.cs:2504-2523). These are the compiler
' side cells; the executable values (25, 100/200, host object precedence) are pinned on the host side by
' Scripting\VisualBasicTest\ScriptSubmissionMemberRedeclarationTests.vb.
'
' What this file is falsifiable by: a rule keyed on TypeKind.Submission alone - i.e. one without the "different
' submission" and "same signature" guards - shows up in G5, G6 and R3, and a rule that never fires shows up as
' the BC30521 the baseline read in G1, G2 and G3. The F4 region additionally pins the placement: the rule is
' applied for exactly the pairs the C# compiler applies its own rule for (measured against a C# submission
' chain, see the F4 comments), and it refuses pairs whose submissions it cannot order by slot.

Imports Microsoft.CodeAnalysis.Test.Utilities
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Imports Roslyn.Test.Utilities

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests

    Public Class ScriptSubmissionMemberTieBreakTests
        Inherits BasicTestBase

#Region "harness"

        ''' <summary>Ids and severities only - the offsets of a redeclaration are not the point of these cells.</summary>
        Private Shared Function DescribeIds(diagnostics As IEnumerable(Of Diagnostic)) As String
            Return String.Join(" | ", diagnostics.OrderBy(Function(d) d.Id).Select(Function(d) $"{d.Id}:{d.Severity}"))
        End Function

        Private Shared Function Lines(ParamArray text() As String) As String
            Return String.Join(vbCrLf, text)
        End Function

        ''' <summary>
        ''' A submission that declares <paramref name="declarations"/> and ends with the expression
        ''' <paramref name="tail"/>, chained onto <paramref name="previous"/>.
        ''' </summary>
        Private Shared Function Submit(declarations As String, tail As String, Optional previous As VisualBasicCompilation = Nothing) As VisualBasicCompilation
            Return CreateSubmission(declarations & vbCrLf & tail, previous:=previous)
        End Function

        Private Function MBody(body As String) As String
            Return Lines("Function M(x As Integer) As Integer", "    Return " & body, "End Function")
        End Function

        ''' <summary>
        ''' The member the submission's trailing expression binds to. The declaring compilation is the axis the
        ''' tie-break turns on, so that is what the cells below compare against.
        ''' Measured note: a call written as the submission's trailing expression is mapped (the <c>M</c> of
        ''' <c>M(5)</c> resolves), but a plain identifier read of a submission member is not -
        ''' <c>GetSymbolInfo</c> answers null there even with zero diagnostics - so reads of properties and
        ''' fields are taken inside a declared member body and the binding asserted here is the tail call's.
        ''' </summary>
        Private Shared Function BoundMemberSymbol(compilation As VisualBasicCompilation, name As String) As Symbol
            Dim tree = compilation.SyntaxTrees.Last()
            Dim model = compilation.GetSemanticModel(tree)
            Dim identifier = tree.GetRoot().DescendantNodes().
                OfType(Of IdentifierNameSyntax)().
                LastOrDefault(Function(n) n.Identifier.ValueText = name)
            Assert.NotNull(identifier)

            Dim symbol = TryCast(model.GetSymbolInfo(identifier).Symbol, Symbol)
            Assert.NotNull(symbol)
            Return symbol
        End Function

        ''' <summary>
        ''' Asserts the trailing expression of the submission binds to a member declared by
        ''' <paramref name="expectedOwner"/>, which is either the submission itself (the newest definition won) or
        ''' an earlier link of the chain (a real overload was kept).
        ''' </summary>
        Private Shared Sub AssertDeclaredIn(symbol As Symbol, expectedOwner As VisualBasicCompilation)
            Assert.NotNull(symbol)
            Assert.True(symbol.DeclaringCompilation Is expectedOwner, $"unexpected declaring submission: {DescriptionOf(symbol)}")
        End Sub

        ''' <summary>The cell's submission is accepted: no diagnostic of any severity.</summary>
        Private Shared Sub AssertNoDiagnostics(compilation As VisualBasicCompilation)
            Dim ids = DescribeIds(compilation.GetDiagnostics())
            Assert.True(ids = "", $"diagnostics: {ids}")
        End Sub

        ''' <summary>
        ''' "declaringAssembly:ContainingType.Member". Submission assemblies get unique names from
        ''' <see cref="BasicTestBase.GetUniqueName"/>, so the string says which link of the chain declared the
        ''' member without relying on the display of the implicit class.
        ''' </summary>
        Private Shared Function DescriptionOf(symbol As Symbol) As String
            Dim owner = symbol.DeclaringCompilation
            Return $"{If(owner Is Nothing, "?", owner.AssemblyName)}:" &
                   $"{If(symbol.ContainingType IsNot Nothing, symbol.ContainingType.Name & ".", "")}{symbol.Name}"
        End Function

#End Region

#Region "G1 - G3: the tie-break itself"

        ''' <summary>
        ''' G1: two submissions declaring the same <c>Function M(x As Integer)</c>. The baseline read BC30521 with
        ''' candidates <c>Submission#1.M</c> and <c>Submission#0.M</c> (probe p8b-fn-redeclare); the C# shape
        ''' reports nothing and calls the newest definition. Measured: no diagnostics, and the trailing
        ''' <c>M(5)</c> binds to the member the current submission declares, while the earlier submission's own
        ''' call site keeps binding to its own - the winner is by chain position, not by which one exists.
        ''' </summary>
        <Fact>
        Public Sub G1_SameSignatureFunctionAcrossSubmissions_BindsToTheLatestWithoutDiagnostics()
            Dim c0 = Submit(MBody("x + x"), "M(0)")

            Assert.Equal("", DescribeIds(c0.GetDiagnostics()))
            AssertDeclaredIn(BoundMemberSymbol(c0, "M"), c0)

            Dim c1 = Submit(MBody("x * x"), "M(5)", c0)

            AssertNoDiagnostics(c1)
            AssertDeclaredIn(BoundMemberSymbol(c1, "M"), c1)
        End Sub

        ''' <summary>
        ''' G2: the same shape on a <c>Property</c>. This is the one place where the criterion is aligned by
        ''' outcome rather than copied: overloadable properties are a VB feature C# does not have, and
        ''' <c>IsOverloadable</c> (SymbolExtensions.vb:136-154) is what makes both accessors reach the decision
        ''' layer at all. Measured: the chain is accepted with no diagnostics at either link (the baseline read
        ''' BC30521 for the read of <c>P</c>), and the submission's own call binds to its own newest member.
        ''' The value of the read - that it is the newest property that answers - is pinned by the host side
        ''' cell, because a plain identifier read of a submission member is not mapped by
        ''' <c>GetSymbolInfo</c> (measured: null symbol, zero candidates, zero diagnostics).
        ''' </summary>
        <Fact>
        Public Sub G2_SameSignaturePropertyAcrossSubmissions_BindsToTheLatestWithoutDiagnostics()
            Dim declaration As Func(Of Integer, String) = Function(version)
                Return Lines(
                    "Property P As Integer",
                    "    Get",
                    $"        Return {version}",
                    "    End Get",
                    "    Set(value As Integer)",
                    "    End Set",
                    "End Property",
                    "Function ReadP() As Integer",
                    "    Return P",
                    "End Function")
            End Function

            Dim c0 = Submit(declaration(1), "ReadP()")

            Assert.Equal("", DescribeIds(c0.GetDiagnostics()))
            AssertDeclaredIn(BoundMemberSymbol(c0, "ReadP"), c0)

            Dim c1 = Submit(declaration(2), "ReadP()", c0)

            AssertNoDiagnostics(c1)
            AssertDeclaredIn(BoundMemberSymbol(c1, "ReadP"), c1)
        End Sub

        ''' <summary>
        ''' G3: a chain longer than two. Slot comparison is positional, so what has to hold is that every link
        ''' keeps calling its own newest-at-the-time definition and that no link is ambiguous.
        ''' </summary>
        <Fact>
        Public Sub G3_ThreeSubmissionChain_EveryLinkBindsToItsOwnNewestDeclaration()
            Dim c0 = Submit(MBody("x + x"), "M(0)")
            Dim c1 = Submit(MBody("x * x"), "M(0)", c0)
            Dim c2 = Submit(MBody("x * x * x"), "M(5)", c1)

            Assert.Equal("", DescribeIds(c0.GetDiagnostics()))
            Assert.Equal("", DescribeIds(c1.GetDiagnostics()))
            Assert.Equal("", DescribeIds(c2.GetDiagnostics()))

            AssertDeclaredIn(BoundMemberSymbol(c2, "M"), c2)
            AssertDeclaredIn(BoundMemberSymbol(c1, "M"), c1)
            AssertDeclaredIn(BoundMemberSymbol(c0, "M"), c0)
        End Sub

#End Region

#Region "G5, G6: the guards that keep the rule narrow"

        ''' <summary>
        ''' G5 (criterion 4, counter-example): one submission declares the member twice. Both candidates come
        ''' from a single <c>DeclaringCompilation</c>, so the new rule is inert and the shape keeps reporting
        ''' what it reported before the fix. Measured, and measured identically on the pre-fix tree (log 01
        ''' "baseline capture"): the script class accepts the duplicate declaration itself and only the call is
        ''' rejected with BC30521 - the ambiguity the tie-break removes across submissions and must not remove
        ''' inside one. Pinned as id, severity and position of the diagnostic: line 6 is the call, the two
        ''' three-line declarations above it take lines 0 to 5.
        ''' </summary>
        <Fact>
        Public Sub G5_DuplicateWithinOneSubmission_KeepsReportingWhatItReportedBefore()
            Dim c = CreateSubmission(Lines(
                MBody("x + x"),
                MBody("x * x"),
                "M(5)"))

            Dim diagnostics = c.GetDiagnostics().ToArray()

            Assert.Equal("BC30521:Error", DescribeIds(diagnostics))
            Assert.Equal(6, diagnostics(0).Location.GetLineSpan().StartLinePosition.Line)
        End Sub

        ''' <summary>
        ''' G6 (criterion 3): two submissions declaring *differently* signed members of the same name are real
        ''' overloads and both must stay reachable - the reason the fix is a decision-layer tie-break and not a
        ''' lookup-layer truncation. Measured: each argument type binds to its own definition, including the
        ''' earlier submission's.
        ''' </summary>
        <Fact>
        Public Sub G6_DifferentlySignedMembersAcrossSubmissions_BothRemainReachable()
            Dim c0 = Submit(Lines(
                "Function M(x As Integer) As Integer",
                "    Return 100",
                "End Function"), "M(0)")
            Dim c1 = Submit(Lines(
                "Function M(s As String) As Integer",
                "    Return 200",
                "End Function"), "M(""a"")", c0)

            Assert.Equal("", DescribeIds(c0.GetDiagnostics()))
            Assert.Equal("", DescribeIds(c1.GetDiagnostics()))

            AssertDeclaredIn(BoundMemberSymbol(c1, "M"), c1)
            AssertDeclaredIn(BoundMemberSymbol(c0, "M"), c0)
        End Sub

        ''' <summary>
        ''' G6 at the call site that matters for the host reading of 100: an <c>Integer</c> argument still
        ''' selects the earlier submission's overload after a newer submission declared a <c>String</c> one.
        ''' Without the signatureMatch guard this is the cell that breaks (the newer member would eliminate the
        ''' older one and the call would stop binding).
        ''' </summary>
        <Fact>
        Public Sub G6_OverloadFromTheEarlierSubmission_IsStillSelectedForItsArgumentType()
            Dim c0 = Submit(Lines(
                "Function M(x As Integer) As Integer",
                "    Return 100",
                "End Function"), "M(0)")
            Dim c1 = Submit(Lines(
                "Function M(s As String) As Integer",
                "    Return 200",
                "End Function"), "M(5)", c0)

            AssertNoDiagnostics(c1)
            AssertDeclaredIn(BoundMemberSymbol(c1, "M"), c0)
        End Sub

#End Region

#Region "R1, R2, R3: counter-example locks"

        ''' <summary>
        ''' R1: fields are not overloadable, so the lookup layer already stops at the newest submission and the
        ''' decision layer never sees two candidates. The shape reads the same before and after the fix (measured
        ''' both ways: no diagnostic at either link, and the baseline host probe p8-dim-redeclare answered the
        ''' value 2 - re-pinned as a value assertion on the host side). A reader member is deliberately not
        ''' declared per link here: an earlier run of this cell did that, and it failed on the pre-fix tree with
        ''' BC30521 for the *method* redeclaration, which is G1's subject and not this lock's.
        ''' </summary>
        <Fact>
        Public Sub R1_FieldRedeclarationAcrossSubmissions_IsUnchanged()
            Dim c0 = CreateSubmission("Dim x As Integer = 1" & vbCrLf & "x")
            Dim c1 = CreateSubmission("Dim x As Integer = 2" & vbCrLf & "x", previous:=c0)

            Assert.Equal("", DescribeIds(c0.GetDiagnostics()))
            Assert.Equal("", DescribeIds(c1.GetDiagnostics()))
        End Sub

        ''' <summary>
        ''' R2: <c>Shadows</c> and <c>Overloads</c> carry no meaning in the submission chain (the lookup always
        ''' overloads, ignoring the modifier) and the new rule must not start reading them. Baseline: with an
        ''' explicit <c>Shadows</c> the shape still read BC30521 (probe p8c-shadows-fn); measured now, all three
        ''' spellings behave the same and add no diagnostic.
        ''' </summary>
        <Fact>
        Public Sub R2_ShadowsAndOverloadsModifiers_DoNotChangeTheOutcome()
            Dim plain = Submit(MBody("x + x"), "M(0)")
            Dim withShadows = Submit(Lines(
                "Shadows Function M(x As Integer) As Integer",
                "    Return x * x",
                "End Function"), "M(5)", plain)
            Dim withOverloads = Submit(Lines(
                "Overloads Function M(x As Integer) As Integer",
                "    Return x * x * x",
                "End Function"), "M(5)", withShadows)

            Assert.Equal("", DescribeIds(plain.GetDiagnostics()))
            Assert.Equal("", DescribeIds(withShadows.GetDiagnostics()))
            Assert.Equal("", DescribeIds(withOverloads.GetDiagnostics()))

            AssertDeclaredIn(BoundMemberSymbol(withShadows, "M"), withShadows)
            AssertDeclaredIn(BoundMemberSymbol(withOverloads, "M"), withOverloads)
        End Sub

        ''' <summary>
        ''' R3: <c>OverloadResolution.vb</c> is shared by the whole compiler, so the tie-break must be inert
        ''' outside submissions. The control is the shape that is structurally identical one level down: two
        ''' identically signed extension methods on the same receiver type, coming from unrelated modules, so
        ''' the lookup layer hands both to the decision layer and nothing there distinguishes them but a
        ''' submission slot that does not exist. Measured: BC30521, exactly what the submission shape no longer
        ''' reports. (Module members written as plain members are a weaker control - the lookup layer rejects
        ''' those with BC30562 before the decision layer is reached.)
        ''' </summary>
        <Fact>
        Public Sub R3_OrdinaryCompilationAmbiguity_StillReportsBC30521()
            Dim source = Lines(
                "Imports System.Runtime.CompilerServices",
                "",
                "Module Module1",
                "    <Extension> Function F(x As String) As Integer",
                "        Return 1",
                "    End Function",
                "End Module",
                "",
                "Module Module2",
                "    <Extension> Function F(x As String) As Integer",
                "        Return 2",
                "    End Function",
                "End Module",
                "",
                "Module Entry",
                "    Sub Main()",
                "        System.Console.WriteLine(""abc"".F())",
                "    End Sub",
                "End Module")

            Dim c = CreateCompilation(source, options:=TestOptions.ReleaseExe)
            Assert.Contains("BC30521", DescribeIds(c.GetDiagnostics()))
        End Sub

#End Region

#Region "F4: the layer the rule is applied at, and what it refuses to decide"

        ''' <summary>
        ''' The candidate the decision layer sees for a member declared by <paramref name="containingType"/>.
        ''' </summary>
        Private Shared Function CandidateOf(containingType As NamedTypeSymbol, name As String) As OverloadResolution.CandidateAnalysisResult
            Dim method = DirectCast(containingType.GetMembers(name).Single(), MethodSymbol)
            Return New OverloadResolution.CandidateAnalysisResult(New OverloadResolution.MethodCandidate(method))
        End Function

        ''' <summary>
        ''' F4 (reading taken first, on the current code): two submissions declaring the same name with the same
        ''' arity but with parameter types that are not comparable (<c>Integer, Long</c> against
        ''' <c>Long, Integer</c>). The declared signatures do not match, so the tie-break does not apply and the
        ''' pair keeps reporting BC30521 naming both candidates, measured at the call itself (line 3 of the
        ''' submission). The C# compiler is read with the same shape and answers the same way - CS0121 "The call
        ''' is ambiguous between 'M(long, int)' and 'M(int, long)'" - because its rule sits behind a check that
        ''' every pair of corresponding parameter types is identical (OverloadResolution.cs:2218/2352), not at
        ''' the end of the tie-breaking sequence. This cell is the lock for "the placement is the same": a rule
        ''' widened to cover this shape would diverge from C#.
        ''' </summary>
        <Fact>
        Public Sub F4_IncomparableParameterTypesAcrossSubmissions_KeepReportingAmbiguityLikeCSharp()
            Dim c0 = Submit(Lines(
                "Function M(a As Integer, b As Long) As Integer",
                "    Return 100",
                "End Function"), "M(1, 2)")
            Dim c1 = Submit(Lines(
                "Function M(a As Long, b As Integer) As Integer",
                "    Return 200",
                "End Function"), "M(1, 2)", c0)

            AssertNoDiagnostics(c0)

            Dim diagnostics = c1.GetDiagnostics().ToArray()

            Assert.Equal("BC30521:Error", DescribeIds(diagnostics))
            Assert.Equal(3, diagnostics(0).Location.GetLineSpan().StartLinePosition.Line)
        End Sub

        ''' <summary>
        ''' The shape C# does take by chain position although the two members are not declared identically:
        ''' corresponding parameter types that are identical while the parameter kind differs. C# answers with
        ''' the newest definition (probe: <c>int M(int a)</c> then <c>int M(in int a)</c>, no diagnostics), and
        ''' VB does the same because the signature comparison the tie-break is gated on compares parameter
        ''' types only. Measured: no diagnostics, and the call binds to the member of the newer submission.
        ''' </summary>
        <Fact>
        Public Sub F4_SameParameterTypesDifferingKindAcrossSubmissions_BindsToTheLatest()
            Dim c0 = CreateSubmission(Lines(
                "Function M(x As Integer) As Integer",
                "    Return 100",
                "End Function",
                "Dim i As Integer = 1",
                "M(i)"))
            Dim c1 = CreateSubmission(Lines(
                "Function M(ByRef x As Integer) As Integer",
                "    Return 200",
                "End Function",
                "Dim j As Integer = 1",
                "M(j)"), previous:=c0)

            AssertNoDiagnostics(c1)
            AssertDeclaredIn(BoundMemberSymbol(c1, "M"), c1)
        End Sub

        ''' <summary>
        ''' Slot equality is a real state, not a hypothetical one: a submission without code to emit keeps the
        ''' slot of its predecessor (<c>Compilation.GetSubmissionSlotIndex</c>). Measured: the chain head that
        ''' declares something has slot 1, and so does the <c>Imports</c>-only submission after it.
        ''' </summary>
        <Fact>
        Public Sub F4_SubmissionWithoutCodeToEmit_KeepsThePredecessorSlot()
            Dim withCode = CreateSubmission("Dim i As Integer = 1" & vbCrLf & "i")
            Dim importsOnly = CreateSubmission("Imports System", previous:=withCode)

            Assert.Equal(1, withCode.GetSubmissionSlotIndex())
            Assert.Equal(1, importsOnly.GetSubmissionSlotIndex())
        End Sub

        ''' <summary>
        ''' What the tie-break refuses: two candidates whose submissions sit at the same slot. A chain head
        ''' always gets slot 1, so two independent chains are the constructible equal-slot pair. No winner may
        ''' be reported - the pair has to fall back to the diagnostics the rest of overload resolution produces.
        ''' </summary>
        <Fact>
        Public Sub F4_EqualSubmissionSlots_NoWinnerIsReported()
            Dim leftChain = Submit(MBody("x + x"), "M(5)")
            Dim rightChain = Submit(MBody("x * x"), "M(5)")

            Assert.Equal(leftChain.GetSubmissionSlotIndex(), rightChain.GetSubmissionSlotIndex())

            Dim leftWins = False
            Dim rightWins = False
            Assert.False(OverloadResolution.TryGetSubmissionSlotWinner(CandidateOf(leftChain.ScriptClass, "M"), CandidateOf(rightChain.ScriptClass, "M"), leftWins, rightWins))
            Assert.False(leftWins)
            Assert.False(rightWins)
        End Sub

        ''' <summary>
        ''' The contrast that keeps the cell above falsifiable: on a chained pair (slots 1 and 2) the predicate
        ''' does report the later submission, and reports the same winner whichever order the pair is passed in.
        ''' </summary>
        <Fact>
        Public Sub F4_DifferentSubmissionSlots_TheLaterSubmissionWinsInEitherArgumentOrder()
            Dim earlier = Submit(MBody("x + x"), "M(5)")
            Dim later = Submit(MBody("x * x"), "M(5)", earlier)

            Assert.Equal(1, earlier.GetSubmissionSlotIndex())
            Assert.Equal(2, later.GetSubmissionSlotIndex())

            Dim earlierWins = False
            Dim laterWins = False
            Assert.True(OverloadResolution.TryGetSubmissionSlotWinner(CandidateOf(earlier.ScriptClass, "M"), CandidateOf(later.ScriptClass, "M"), earlierWins, laterWins))
            Assert.False(earlierWins)
            Assert.True(laterWins)

            earlierWins = False
            laterWins = False
            Assert.True(OverloadResolution.TryGetSubmissionSlotWinner(CandidateOf(later.ScriptClass, "M"), CandidateOf(earlier.ScriptClass, "M"), laterWins, earlierWins))
            Assert.True(laterWins)
            Assert.False(earlierWins)
        End Sub

        ''' <summary>
        ''' The other half of the guard, pinned at the predicate layer (R3 pins it at the call layer): members of
        ''' ordinary compilations are never compared by slot. Their declaring compilations differ and have no
        ''' slot at all (measured: negative), so the rule is inert for every non-script call.
        ''' </summary>
        <Fact>
        Public Sub F4_NonSubmissionCandidates_NoWinnerIsReported()
            Dim first = CreateCompilation("Module DeclaringModule" & vbCrLf & "    Sub F(x As Integer)" & vbCrLf & "    End Sub" & vbCrLf & "End Module", assemblyName:="FirstAssembly", options:=TestOptions.ReleaseExe)
            Dim second = CreateCompilation("Module DeclaringModule" & vbCrLf & "    Sub F(x As Integer)" & vbCrLf & "    End Sub" & vbCrLf & "End Module", assemblyName:="SecondAssembly", options:=TestOptions.ReleaseExe)

            Assert.True(first.GetSubmissionSlotIndex() < 0)
            Assert.True(second.GetSubmissionSlotIndex() < 0)

            Dim leftWins = False
            Dim rightWins = False
            Assert.False(OverloadResolution.TryGetSubmissionSlotWinner(
                CandidateOf(first.GlobalNamespace.GetTypeMembers("DeclaringModule").Single(), "F"),
                CandidateOf(second.GlobalNamespace.GetTypeMembers("DeclaringModule").Single(), "F"),
                leftWins, rightWins))
            Assert.False(leftWins)
            Assert.False(rightWins)
        End Sub

#End Region

    End Class
End Namespace
