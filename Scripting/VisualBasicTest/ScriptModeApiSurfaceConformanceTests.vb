' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System
Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Reflection.Metadata
Imports System.Reflection.PortableExecutable
Imports System.Text
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting
Imports Xunit

''' <summary>
''' U9 -- the remaining script API surface gap (design-detailed.md §U9). Cell map, in the order of the
''' §U9 table (C# baseline roots: ST = CSharpTest/ScriptTests.cs, IS = CSharpTest/InteractiveSessionTests.cs,
''' ISR = CSharpTest/InteractiveSessionReferencesTests.cs, K-SO = CoreTest/ScriptOptionsTests.cs):
''' <list type="table">
''' <item><description>#1 branch chain isolation (1) - BranchingSubscripts_*</description></item>
''' <item><description>#2 repeated EvaluateAsync (2) - SubmissionsExecutionOrder_*</description></item>
''' <item><description>#3 Variables collection (3) - ScriptVariablesChain_*</description></item>
''' <item><description>#4 IsReadOnly (2) - ScriptVariable_*</description></item>
''' <item><description>#5 GetImportScopes (1) - InteractiveSession_ImportScopes</description></item>
''' <item><description>#9 Script.Create null arguments and StreamWithOffset (3) - Create_* / StreamWithOffset_*</description></item>
''' <item><description>#10 host conversions and covariance (2) - SubmissionHostConversions_* / SubmissionHostVarianceConversions_*</description></item>
''' <item><description>#11 top level Private/Friend/Protected visibility (3 + 1 paired negative) - TopLevel*_*</description></item>
''' <item><description>#12 recursive base type (1) - RecursiveBaseType_DeclaresAndRuns</description></item>
''' <item><description>extra family 21 Dynamic_Expando (1) - DynamicExpando_LateBoundMembersAcrossSubmissions</description></item>
''' <item><description>extra family 31 (3) - ClosureCapture_*, ExtensionMethods_*, PrivateImplementationDetails_*</description></item>
''' <item><description>extra family 41 implicit receiver + ByRef (1) - ImplicitReceiverWithByRefArgument_*</description></item>
''' <item><description>extra family 53 SharedLibCopy_Different (1 + 1 paired negative) - SharedLibCopy_*</description></item>
''' </list>
''' The cells whose C# baseline shape is file bound live in the files test-plan §2.1.1 names: #6 '#Load' return
''' semantics in ScriptTests.vb (family 13), #7 '#r' relative paths and extension priority and #8
''' MissingAssemblySymbol in InteractiveSessionReferencesTests.vb (families 35/36/59), #13 and #14 in
''' ScriptOptionsTests.vb (families 64/65/67). #14 WarningLevel is now C# parity - it is forwarded into the
''' compilation via <c>VisualBasicCompilationOptions.WithWarningLevel</c> (see <c>WarningLevel_ReachesTheCompilationOption</c>;
''' the former "not plumbed" divergence in <c>InternalDevDocs\issues\issue-warning-level-not-plumbed.md</c> is closed).
''' The one cell left without a VB landing is #14 AllowUnsafe, recorded as not applicable with the retrieval that
''' establishes it (see that cell).
''' </summary>
Public Class ScriptModeApiSurfaceConformanceTests

    Private Shared ReadOnly s_options As ScriptOptions = ScriptModeConformance.DefaultOptions

#Region "1 - branch chain isolation"

    ''' <summary>
    ''' U9 #1 (C# <c>TestBranchingSubscripts</c>, ST:452). Two chains grown from the same <see cref="ScriptState"/> are
    ''' isolated: the member declared by one chain is not visible to the other, and each chain keeps its own
    ''' member. Falsifiable because the two chains would have to disagree: <c>25</c> proves the branch's own
    ''' declaration is the one it calls, <c>10</c> proves the other branch still reaches the original declaration,
    ''' and <c>BC30451</c> proves the branch's declaration did not leak back into the first state.
    ''' </summary>
    <Fact>
    Public Async Function BranchingSubscripts_TwoChainsFromOneState_AreIsolated() As Task
        Dim first = Await VisualBasicScript.RunAsync(
            "Function M(x As Integer) As Integer" & vbCrLf &
            "    Return x + x" & vbCrLf &
            "End Function", s_options)

        Dim multiplying = first.Script.ContinueWith(
            "Function M2(x As Integer) As Integer" & vbCrLf &
            "    Return x * x" & vbCrLf &
            "End Function" & vbCrLf &
            "? M2(5)")
        Assert.Equal(25, Await multiplying.EvaluateAsync())

        Dim unchanged = first.Script.ContinueWith("? M(5)")
        Assert.Equal(10, Await unchanged.EvaluateAsync())

        Dim blind = first.Script.ContinueWith("? M2(5)")
        Assert.Contains("BC30451", blind.Compile().Select(Function(d) d.Id))
    End Function

    ''' <summary>
    ''' U9 #1, the second half of the C# baseline (<c>TestBranchingSubscripts</c>, ST:452 redeclares the *same*
    ''' member name and expects 25). This case used to pin the divergence - VB reported <c>BC30521</c> for a
    ''' member redeclared by a later submission, with or without an explicit <c>Shadows</c> - and is reclaimed
    ''' here once the decision layer ties such candidates by submission slot
    ''' (<c>OverloadResolution.CombineCandidates</c>, "Position in interactive submission chain. The last
    ''' definition wins."). Both halves are asserted: the submission is accepted with no diagnostic at all, and
    ''' the call answers from the newest body. <see cref="ScriptSubmissionMemberRedeclarationTests"/> holds the
    ''' rest of the shape (properties, longer chains, the <c>#Load</c> axis, and the counter-cells).
    ''' </summary>
    <Fact>
    Public Async Function BranchingSubscripts_RedeclaringTheSameMember_TheLatestDefinitionWins() As Task
        Dim first = Await VisualBasicScript.RunAsync(
            "Function M(x As Integer) As Integer" & vbCrLf &
            "    Return x + x" & vbCrLf &
            "End Function", s_options)

        Dim redeclared = first.Script.ContinueWith(
            "Function M(x As Integer) As Integer" & vbCrLf &
            "    Return x * x" & vbCrLf &
            "End Function" & vbCrLf &
            "? M(5)")

        Assert.Empty(redeclared.Compile())
        Assert.Equal(25, Await redeclared.EvaluateAsync())
    End Function

#End Region

#Region "2 - repeated evaluation and the error submission"

    ''' <summary>
    ''' U9 #2 cell one (C# <c>Submissions_ExecutionOrder1</c>, IS:602). A continuation is stable under repeated
    ''' evaluation, and evaluating a *prefix* of it does not disturb it. The VB text declares the result with
    ''' <c>Return</c> because a typed submission follows Function Main semantics (the trailing expression is ignored,
    ''' see <see cref="SubmissionHostConversions_TypedScriptReturnConversions"/>); the C# text gets 3 from the
    ''' trailing expression.
    ''' <para>
    ''' The side effect counter is the discriminating half: it pins *how many times* the chain actually ran.
    ''' <c>Script.EvaluateAsync</c> re-runs the whole chain from its first submission (11, then 12 for the prefix
    ''' alone, then 23 and 34), while a continuation from a <see cref="ScriptState"/> runs only the new submission
    ''' (1, 11, 11, 11). An implementation that re-ran preceding submissions from a state, or that cached a chain
    ''' execution across <c>EvaluateAsync</c> calls, would land on different numbers.
    ''' </para>
    ''' </summary>
    <Fact>
    Public Async Function SubmissionsExecutionOrder_RepeatedEvaluation_IsStable() As Task
        Dim s0 = VisualBasicScript.Create("Dim x As Integer = 1", s_options)
        Dim s1 = s0.ContinueWith("Dim y As Integer = 2")
        Dim s2 = s1.ContinueWith(Of Integer)("Return x + y")

        Assert.Equal(3, Await s2.EvaluateAsync())
        Assert.Null(Await s1.EvaluateAsync())
        Assert.Null(Await s0.EvaluateAsync())
        Assert.Equal(3, Await s2.EvaluateAsync())
        Assert.Null(Await s1.EvaluateAsync())
        Assert.Null(Await s0.EvaluateAsync())
        Assert.Equal(3, Await s2.EvaluateAsync())
        Assert.Equal(3, Await s2.EvaluateAsync())

        Dim globals = New CounterGlobals()
        Dim t0 = VisualBasicScript.Create(Of Object)("Count += 1", s_options, GetType(CounterGlobals))
        Dim t1 = t0.ContinueWith(Of Integer)("Count += 10" & vbCrLf & "Return Count")

        Assert.Equal(11, Await t1.EvaluateAsync(globals))
        Assert.Equal(11, globals.Count)
        Assert.Equal(12, Await t0.EvaluateAsync(globals))
        Assert.Equal(12, globals.Count)
        Assert.Equal(23, Await t1.EvaluateAsync(globals))
        Assert.Equal(23, globals.Count)
        Assert.Equal(34, Await t1.EvaluateAsync(globals))
        Assert.Equal(34, globals.Count)

        Dim scoped = New CounterGlobals()
        Dim u0 = Await VisualBasicScript.Create(Of Object)("Count += 1", s_options, GetType(CounterGlobals)).RunAsync(scoped)
        Assert.Equal(1, scoped.Count)
        Dim u1 = Await u0.ContinueWithAsync("Count += 10")
        Assert.Equal(11, scoped.Count)
        Dim u2 = Await u1.ContinueWithAsync("? Count")
        Assert.Equal(11, scoped.Count)
        Assert.Equal(11, u2.ReturnValue)
    End Function

    ''' <summary>
    ''' U9 #2 cell two (C# <c>Submissions_ExecutionOrder2</c>, IS:621). A rejected submission is rejected at every
    ''' point of the chain and leaves the chain reusable: the declaration made before it (<c>x</c>) and the one made
    ''' after it (<c>y</c>) both survive, so the last continuation answers 12. The diagnostic ids are the VB
    ''' counterpart of the C# syntax and name errors: BC30195/BC30188 for <c>invalid$syntax</c>, BC30451 for the
    ''' undeclared name. An implementation that dropped the chain on a rejected continuation would fail to bind
    ''' <c>x + y</c> at all.
    ''' </summary>
    <Fact>
    Public Async Function SubmissionsExecutionOrder_RejectedSubmission_DoesNotPolluteTheChain() As Task
        Dim s0 = Await VisualBasicScript.Create("Dim x As Integer = 1", s_options).RunAsync()

        Dim syntaxIds = Await CaptureErrorIds(s0, "invalid$syntax")
        Assert.Contains("BC30195", syntaxIds)
        Assert.Contains("BC30188", syntaxIds)

        Dim s1 = Await s0.ContinueWithAsync("x = 2 : x = 10")
        Assert.Equal(10, s1.ReturnValue)

        Assert.Contains("BC30195", Await CaptureErrorIds(s1, "invalid$syntax"))
        Dim nameIds = Await CaptureErrorIds(s1, "x = undefined_symbol")
        Assert.Equal(New String() {"BC30451"}, nameIds)

        Dim s2 = Await s1.ContinueWithAsync("Dim y As Integer = 2")
        Assert.Null(s2.ReturnValue)

        Dim s3 = Await s2.ContinueWithAsync("x + y")
        Assert.Equal(12, s3.ReturnValue)
    End Function

    ''' <summary>The submission has to be rejected and the rejection has to be observable as a diagnostic set. The
    ''' call is inside the try because a rejected continuation throws from <c>ContinueWithAsync</c> itself rather
    ''' than handing back a faulted task (<c>Script.cs:542</c> reaches the compiler synchronously).</summary>
    Private Shared Async Function CaptureErrorIds(state As ScriptState, code As String) As Task(Of String())
        Try
            Await state.ContinueWithAsync(code)
        Catch ex As CompilationErrorException
            Return ex.Diagnostics.Select(Function(d) d.Id).ToArray()
        End Try

        Assert.True(False, "the submission was expected to be rejected: " & code)
        Return Array.Empty(Of String)()
    End Function

    Public Class CounterGlobals
        Public Count As Integer
    End Class

#End Region

#Region "3 - the Variables collection"

    ''' <summary>
    ''' U9 #3 cell one (C# <c>ScriptVariables_Chain</c>, ST:383). The name/value/type triples of a five submission
    ''' chain, in submission order, including a shadowed name (two entries called <c>a</c>, the first the Char of
    ''' submission #0 and the third the Decimal of submission #2) and the globals object excluded from the
    ''' collection. Every declaration is explicitly typed here on purpose: this cell pins name/value/type triples across
    ''' a submission chain, so it wants declared types that hold regardless of what Option Infer would have produced
    ''' (inference itself is pinned by ScriptModeTopLevelInferenceTests.vb after issue 32 reclassified the old
    ''' "top level Dim x = ... stays Object" divergence as a defect). An implementation that filtered the shadowed
    ''' entry, reordered by name, or read the types off the wrong submission would change one of the three sequences.
    ''' </summary>
    <Fact>
    Public Async Function ScriptVariablesChain_NameValueTypeTriples() As Task
        Dim globals = New VariableGlobals()
        Dim script = VisualBasicScript.
            Create("Dim a As Char = ""1""c", s_options, GetType(VariableGlobals)).
            ContinueWith("Dim b As UInteger = 2UI").
            ContinueWith("Dim a As Decimal = 3D").
            ContinueWith("Dim x As Decimal = a + b").
            ContinueWith("Dim z As Integer = Seed")

        Dim state = Await script.RunAsync(globals)

        Assert.Equal(New String() {"a", "b", "a", "x", "z"}, state.Variables.Select(Function(v) v.Name).ToArray())
        Assert.Equal(New String() {"1", "2", "3", "5", "20"}, state.Variables.Select(Function(v) Convert.ToString(v.Value)).ToArray())
        ' ScriptVariable.Type is the reflected System.Type of the emitted field, so the names are the CLR ones.
        Assert.Equal(New String() {"Char", "UInt32", "Decimal", "Decimal", "Int32"}, state.Variables.Select(Function(v) v.Type.Name).ToArray())
    End Function

    Public Class VariableGlobals
        Public Seed As Integer = 20
    End Class

    ''' <summary>
    ''' U9 #3 cell two (same C# baseline): lookup by name. VB identifiers are case insensitive, so <c>GetVariable</c>
    ''' answers for any casing and returns the *same* instance (the C# baseline asserts the opposite: <c>GetVariable("A")</c>
    ''' is null in C#). The registered divergence is what the assertion pins; an unknown name still answers Nothing.
    ''' </summary>
    <Fact>
    Public Async Function ScriptVariablesChain_GetVariableLookup() As Task
        Dim state = Await VisualBasicScript.Create(
            "Dim a As Integer = 1" & vbCrLf &
            "Dim named As String = ""first""", s_options).RunAsync()

        Assert.Equal(1, state.GetVariable("a").Value)
        Assert.Same(state.GetVariable("a"), state.GetVariable("a"))
        Assert.Same(state.GetVariable("a"), state.GetVariable("A"))
        Assert.Equal("named", state.GetVariable("NAMED").Name)
        Assert.Null(state.GetVariable("notDeclared"))
    End Function

    ''' <summary>
    ''' U9 #3 cell three (C# <c>ScriptVariable_SetValue</c>, ST:413): the write back is visible to a continuation of
    ''' the state, and re-running the same <c>Script</c> rebuilds the variable from the submission (1) instead of
    ''' keeping the written value (2). The pair 2/2/1 is the discriminator: a state that shares storage with a fresh
    ''' run would answer 1 on the last assertion, and a continuation that re-ran the chain would answer 1 instead of 2.
    ''' </summary>
    <Fact>
    Public Async Function ScriptVariablesChain_SetValueWritesBack() As Task
        Dim state = Await VisualBasicScript.Create("Dim x As Integer = 1", s_options).RunAsync()
        Dim variable = state.GetVariable("x")

        variable.Value = 2
        Assert.Equal(2, variable.Value)
        Assert.Equal(2, state.GetVariable("x").Value)

        Dim continued = Await state.ContinueWithAsync("? x")
        Assert.Equal(2, continued.ReturnValue)

        Dim rerun = Await state.Script.RunAsync()
        Assert.Equal(1, rerun.GetVariable("x").Value)
    End Function

#End Region

#Region "4 - IsReadOnly"

    ''' <summary>
    ''' U9 #4 cell one (C# <c>ScriptVariable_SetValue_Errors</c>, ST:432). A top level <c>ReadOnly</c> field is
    ''' <c>IsInitOnly</c> in the emitted submission class, so <see cref="ScriptVariable.IsReadOnly"/> is true and the
    ''' setter raises <see cref="InvalidOperationException"/> with the read only message - while a plain field of the
    ''' same shape accepts a same typed write and rejects a mismatched one with <see cref="ArgumentException"/>.
    ''' The two exception *types* on the same object are what makes the cell fail if the read only check is dropped:
    ''' the mismatched write would then be the only signal.
    ''' </summary>
    <Fact>
    Public Async Function ScriptVariable_ReadOnlyTopLevelField_IsReadOnly() As Task
        Dim state = Await VisualBasicScript.Create(
            "Dim writable As Integer = 1" & vbCrLf &
            "ReadOnly frozen As Integer = 2", s_options).RunAsync()

        Dim writable = state.GetVariable("writable")
        Assert.False(writable.IsReadOnly)
        writable.Value = 5
        Assert.Equal(5, writable.Value)
        Assert.Throws(Of ArgumentException)(Sub() writable.Value = "not an integer")

        Dim frozen = state.GetVariable("frozen")
        Assert.True(frozen.IsReadOnly)
        Assert.Equal(2, frozen.Value)
        Dim thrown = Assert.Throws(Of InvalidOperationException)(Sub() frozen.Value = 0)
        Assert.Equal(ScriptingResources.CannotSetReadOnlyVariable, thrown.Message)
    End Function

    ''' <summary>
    ''' U9 #4 cell two: the second read only source. A top level <c>Const</c> is a literal (shared) field, so
    ''' <c>IsReadOnly</c> is true through <c>IsLiteral</c> rather than <c>IsInitOnly</c> and the message is the
    ''' constant one. The cell fails if the constant path is folded into the read only path, or if constants stop
    ''' reaching the variable collection at all.
    ''' </summary>
    <Fact>
    Public Async Function ScriptVariable_ConstTopLevelField_IsReadOnly() As Task
        Dim state = Await VisualBasicScript.Create(
            "Const frozen As Integer = 3", s_options).RunAsync()

        Dim frozen = state.GetVariable("frozen")
        Assert.True(frozen.IsReadOnly)
        Assert.Equal(3, frozen.Value)
        Dim thrown = Assert.Throws(Of InvalidOperationException)(Sub() frozen.Value = 3)
        Assert.Equal(ScriptingResources.CannotSetConstantVariable, thrown.Message)
    End Function

#End Region

#Region "5 - GetImportScopes"

    ''' <summary>
    ''' U9 #5 (C# <c>InteractiveSession_ImportScopes</c>, IS:1174): the alias/extern/xmlns/imports structure of the
    ''' submission import scope. Four readings, each with a distinct failure mode.
    ''' <list type="number">
    ''' <item><description>Options imports land in one scope with no declaring syntax reference (the C# baseline's
    ''' assertion, plus the three empty collections).</description></item>
    ''' <item><description>An <c>Imports</c> clause of a *previous* submission reaches the next submission's scope
    ''' (and its compilation <c>GlobalImports</c>) - the hoisting of
    ''' <c>VisualBasicScriptCompiler.vb:134-162</c>; an implementation that dropped the accumulation would show one
    ''' import instead of two.</description></item>
    ''' <item><description>An <c>Imports</c> clause of the *current* tree does not appear as an import scope at
    ''' position 0 at all (0 scopes) even though it takes effect - pinned so the asymmetry with the previous case is
    ''' recorded rather than assumed.</description></item>
    ''' <item><description>The negative half: a position outside the tree is rejected, not silently answered.</description></item>
    ''' </list>
    ''' </summary>
    <Fact>
    Public Async Function InteractiveSession_ImportScopes() As Task
        Dim optionsOnly = VisualBasicScript.Create("? 1 + 1", s_options)
        Dim optionsCompilation = optionsOnly.GetCompilation()
        Dim optionsModel = optionsCompilation.GetSemanticModel(optionsCompilation.SyntaxTrees.Single())
        Dim scopes = optionsModel.GetImportScopes(0)

        Assert.Equal(1, scopes.Length)
        Dim scope = scopes.Single()
        Assert.Empty(scope.Aliases)
        Assert.Empty(scope.ExternAliases)
        Assert.Empty(scope.XmlNamespaces)
        Assert.Equal(1, scope.Imports.Length)
        Dim [import] = scope.Imports.Single()
        Assert.Equal("System.Threading.Tasks", [import].NamespaceOrType.ToDisplayString())
        Assert.Null([import].DeclaringSyntaxReference)

        Dim first = Await VisualBasicScript.RunAsync(
            "Imports System.Text" & vbCrLf & "Dim builder As New StringBuilder()", s_options)
        Dim second = Await first.ContinueWithAsync("? 1 + 1")
        Dim carried = second.Script.GetCompilation()
        Dim carriedModel = carried.GetSemanticModel(carried.SyntaxTrees.Last())
        Dim carriedScope = carriedModel.GetImportScopes(0).Single()
        Assert.Equal(
            New String() {"System.Text", "System.Threading.Tasks"},
            carriedScope.Imports.Select(Function(i) i.NamespaceOrType.ToDisplayString()).OrderBy(Function(n) n, StringComparer.Ordinal).ToArray())
        Assert.Equal(
            New String() {"System.Threading.Tasks", "System.Text"},
            DirectCast(carried.Options, VisualBasicCompilationOptions).GlobalImports.Select(Function(g) g.Clause.ToString()).ToArray())

        Dim own = VisualBasicScript.Create("Imports System.Text" & vbCrLf & "? 1 + 1", s_options).GetCompilation()
        Dim ownModel = own.GetSemanticModel(own.SyntaxTrees.Single())
        Assert.Empty(ownModel.GetImportScopes(0))

        Assert.Throws(Of ArgumentException)(Function() ownModel.GetImportScopes(99))
    End Function

#End Region

#Region "9 - Script.Create null arguments and streams with an offset"

    ''' <summary>
    ''' U9 #9 cell one (C# <c>TestCreateScript_CodeIsNull</c>, ST:40). Registered divergence: C# throws
    ''' <see cref="ArgumentNullException"/> for a null code string, VB substitutes the empty string
    ''' (<c>VisualBasicScript.vb:29</c> <c>SourceText.From(If(code, String.Empty))</c>) and the submission still runs
    ''' and answers Nothing. The case pins the VB value; it fails if the substitution is replaced by a throw.
    ''' </summary>
    <Fact>
    Public Async Function Create_NullCodeIsAnEmptyScript() As Task
        Dim script = VisualBasicScript.Create(DirectCast(Nothing, String), s_options)
        Assert.Equal("", script.Code)

        Dim state = Await script.RunAsync()
        Assert.Null(state.ReturnValue)
    End Function

    ''' <summary>
    ''' U9 #9 cell two (C# <c>TestCreateFromStreamScript_StreamIsNull</c>, ST:53): a null stream is rejected, and the
    ''' parameter name is pinned so the failure points at the argument rather than at the encoding path. Paired with
    ''' the cell above: VB treats the two null arguments differently, and both halves are asserted.
    ''' </summary>
    <Fact>
    Public Sub Create_NullStreamThrowsArgumentNullException()
        Dim thrown = Assert.Throws(Of ArgumentNullException)(Sub() VisualBasicScript.Create(DirectCast(Nothing, Stream), s_options))
        Assert.Equal("stream", thrown.ParamName)
    End Sub

    ''' <summary>
    ''' U9 #9 cell three (C# <c>StreamWithOffset</c>, ST:940, issue 12348). A stream whose contents start partway
    ''' into a backing buffer must be read from *its own* origin: the direct stream case is asserted on
    ''' <c>Script.Code</c> (the padding Qs must not appear) and the '#Load' case goes through a
    ''' <see cref="SourceReferenceResolver"/> that hands out such a segment stream, so the loaded method has to be
    ''' the segment text. An implementation that read the backing buffer instead of the stream would see the Qs and
    ''' the load would not compile.
    ''' </summary>
    <Fact>
    Public Async Function StreamWithOffset_SegmentStreamsAreReadFromTheirOwnOrigin() As Task
        Const padding As Integer = 42
        Dim code = "? 6 * 7"
        Dim bytes = Enumerable.Repeat(CByte(AscW("Q"c)), code.Length + padding).ToArray()
        Encoding.ASCII.GetBytes(code, 0, code.Length, bytes, padding)

        Using stream = New MemoryStream(bytes, padding, code.Length, writable:=False, publiclyVisible:=True)
            Dim script = VisualBasicScript.Create(stream, s_options)
            Assert.Equal(code, script.Code)
            Dim state = Await script.RunAsync()
            Assert.Equal(42, state.ReturnValue)
        End Using

        Dim resolver = New SegmentSourceResolver(padding,
            "Function Loaded() As Integer" & vbCrLf & "    Return 5" & vbCrLf & "End Function")
        Dim options = s_options.WithFilePath("C:\scripts\main.vbx").WithSourceResolver(resolver)
        Dim loaded = VisualBasicScript.Create("#Load ""a.vbx""" & vbCrLf & "? Loaded()", options)

        Assert.DoesNotContain(loaded.Compile(), Function(d) d.Severity = DiagnosticSeverity.Error)
        Dim loadedState = Await loaded.RunAsync()
        Assert.Equal(5, loadedState.ReturnValue)
    End Function

    ''' <summary>Hands out '#Load' text as a stream over a segment of a padded buffer, and implements only the
    ''' members the C# baseline's <c>StreamOffsetResolver</c> implements (ScriptTests.cs:1111), so the read goes
    ''' through <c>SourceReferenceResolver.ReadText</c>'s default <c>OpenRead</c> fallback.</summary>
    Private NotInheritable Class SegmentSourceResolver
        Inherits SourceReferenceResolver

        Private ReadOnly _padding As Integer
        Private ReadOnly _text As String

        Public Sub New(padding As Integer, text As String)
            _padding = padding
            _text = text
        End Sub

        Public Overrides Function NormalizePath(path As String, baseFilePath As String) As String
            Return path
        End Function

        Public Overrides Function ResolveReference(path As String, baseFilePath As String) As String
            Return If(path = "a.vbx", "C:\scripts\a.vbx", Nothing)
        End Function

        Public Overrides Function OpenRead(resolvedPath As String) As Stream
            Dim bytes = Enumerable.Repeat(CByte(AscW("Q"c)), _text.Length + _padding).ToArray()
            Encoding.ASCII.GetBytes(_text, 0, _text.Length, bytes, _padding)
            Return New MemoryStream(bytes, _padding, _text.Length, writable:=False, publiclyVisible:=True)
        End Function

        Public Overrides Function Equals(other As Object) As Boolean
            Return ReferenceEquals(Me, other)
        End Function

        Public Overrides Function GetHashCode() As Integer
            Return 42
        End Function
    End Class

#End Region

#Region "10 - host conversions"

    ''' <summary>
    ''' U9 #10 cell one (C# <c>Submission_HostConversions</c>, IS:1429). The conversion behaviour of a typed
    ''' submission, which in VB is Function Main semantics plus Option Strict Off
    ''' (<c>VisualBasicScriptCompiler.vb:218</c>), not the C# conversion rules:
    ''' <list type="bullet">
    ''' <item><description><c>Return 1 + 1</c> as Integer is 2, but <c>1 + 1</c> alone is 0: a trailing expression is
    ''' not the result of a typed submission (the dual of the C# text, where the trailing expression is used).</description></item>
    ''' <item><description><c>Return Nothing</c> is Nothing for String and 0 for Integer (C# reports CS0037/CS0029
    ''' here); <c>Return 1 + 1</c> as String is "2" rather than CS0029.</description></item>
    ''' <item><description>Narrowing conversions are permitted and fail at run time (<c>Return "abc"</c> as Integer
    ''' raises InvalidCastException), while a conversion with no narrowing at all is a compile error: BC30311 for
    ''' <c>Return System.Guid.NewGuid()</c> as Integer.</description></item>
    ''' </list>
    ''' The pair 2 (with Return) / 0 (trailing only) and the pair compile error (Guid) / run time error (String) are
    ''' the discriminators: a submission that used the trailing expression would answer 2 for the fourth case, and one
    ''' that checked conversions strictly would report an error for the String case.
    ''' </summary>
    <Fact>
    Public Async Function SubmissionHostConversions_TypedScriptReturnConversions() As Task
        Assert.Equal(2, Await VisualBasicScript.EvaluateAsync(Of Integer)("Return 1 + 1", s_options))

        Dim trailingOnly = VisualBasicScript.Create(Of Integer)("1 + 1", s_options)
        Assert.DoesNotContain(trailingOnly.Compile(), Function(d) d.Severity = DiagnosticSeverity.Error)
        Assert.Equal(0, (Await trailingOnly.RunAsync()).ReturnValue)

        Dim bareReturn = Await VisualBasicScript.RunAsync(Of Integer)("Return", s_options)
        Assert.Equal(0, bareReturn.ReturnValue)

        Dim nullAsString = Await VisualBasicScript.RunAsync(Of String)("Return Nothing", s_options)
        Assert.Null(nullAsString.ReturnValue)

        Dim nullAsInteger = Await VisualBasicScript.RunAsync(Of Integer)("Return Nothing", s_options)
        Assert.Equal(0, nullAsInteger.ReturnValue)

        Dim sumAsString = Await VisualBasicScript.RunAsync(Of String)("Return 1 + 1", s_options)
        Assert.Equal("2", sumAsString.ReturnValue)

        Dim rounded = Await VisualBasicScript.RunAsync(Of Integer)("Return 1.5", s_options)
        Assert.Equal(2, rounded.ReturnValue)

        Dim inconvertible = VisualBasicScript.Create(Of Integer)("Return System.Guid.NewGuid()", s_options)
        Assert.Contains("BC30311", inconvertible.Compile().Select(Function(d) d.Id))

        Dim narrowed = VisualBasicScript.Create(Of Integer)("Return ""abc""", s_options)
        Assert.DoesNotContain(narrowed.Compile(), Function(d) d.Severity = DiagnosticSeverity.Error)
        Await Assert.ThrowsAsync(Of InvalidCastException)(Async Function() Await narrowed.RunAsync())
    End Function

    ''' <summary>
    ''' U9 #10 cell two (C# <c>Submission_HostVarianceConversions</c>, IS:1482): a List(Of ArgumentException) is a
    ''' valid covariant answer for an IEnumerable(Of Exception) return type and arrives unchanged (empty, no
    ''' elements). The negative half is registered rather than asserted as correct: the opposite direction compiles
    ''' (VB narrowing again) and fails at the cast out of the submission with InvalidCastException; the same source
    ''' in an ordinary compilation is accepted too (measured), so this is a VB conversion rule and not a script mode
    ''' property.
    ''' </summary>
    <Fact>
    Public Async Function SubmissionHostVarianceConversions_CovariantReturnIsAccepted() As Task
        Dim state = Await VisualBasicScript.RunAsync(Of IEnumerable(Of Exception))(
            "Return New System.Collections.Generic.List(Of System.ArgumentException)()", s_options)

        Assert.NotNull(state.ReturnValue)
        Assert.Empty(state.ReturnValue)
        Assert.Null(state.ReturnValue.FirstOrDefault())

        Dim nonCovariant = VisualBasicScript.Create(Of IEnumerable(Of Exception))(
            "Return New System.Collections.Generic.List(Of String)()", s_options)
        Assert.DoesNotContain(nonCovariant.Compile(), Function(d) d.Severity = DiagnosticSeverity.Error)
        Await Assert.ThrowsAsync(Of InvalidCastException)(Async Function() Await nonCovariant.RunAsync())
    End Function

#End Region

#Region "11 - top level Private / Friend / Protected"

    ''' <summary>
    ''' U9 #11 cell one (C# <c>PrivateTopLevel</c>, IS:299). A top level <c>Private</c> field keeps its declared
    ''' accessibility on the symbol but is emitted as a public field of the interactive submission class
    ''' (<c>Symbols\Symbol.vb:97-102</c>, the <c>Case Accessibility.Private</c> arm of
    ''' <c>MetadataVisibility</c>), so it shows up in <see cref="ScriptState.Variables"/> and later submissions read
    ''' it. Three assertions, three ways to fail: the symbol accessibility, the collection, and the value read from a
    ''' later submission.
    ''' </summary>
    <Fact>
    Public Async Function TopLevelPrivateField_IsVisibleToLaterSubmissions() As Task
        Dim state = Await VisualBasicScript.Create("Private hidden As Integer = 4", s_options).RunAsync()

        Assert.Equal("Private", SubmissionField(state, "hidden").DeclaredAccessibility.ToString())
        Assert.Contains("hidden", state.Variables.Select(Function(v) v.Name))

        Dim reader = Await state.ContinueWithAsync("Dim total As Integer = hidden + 1")
        Assert.Equal(5, reader.GetVariable("total").Value)
        Dim caller = Await state.ContinueWithAsync("? hidden")
        Assert.Equal(4, caller.ReturnValue)
    End Function

    ''' <summary>U9 #11 cell two (C# <c>Fields_Visibility</c>, IS:362): the same three readings for <c>Friend</c>,
    ''' whose symbol accessibility is <c>Internal</c>.</summary>
    <Fact>
    Public Async Function TopLevelFriendField_IsVisibleToLaterSubmissions() As Task
        Dim state = Await VisualBasicScript.Create("Friend hidden As Integer = 5", s_options).RunAsync()

        Assert.Equal("Internal", SubmissionField(state, "hidden").DeclaredAccessibility.ToString())
        Assert.Contains("hidden", state.Variables.Select(Function(v) v.Name))

        Dim caller = Await state.ContinueWithAsync("? hidden + 1")
        Assert.Equal(6, caller.ReturnValue)
    End Function

    ''' <summary>U9 #11 cell three: the same three readings for <c>Protected</c>, whose symbol accessibility is
    ''' <c>Protected</c>.</summary>
    <Fact>
    Public Async Function TopLevelProtectedField_IsVisibleToLaterSubmissions() As Task
        Dim state = Await VisualBasicScript.Create("Protected hidden As Integer = 6", s_options).RunAsync()

        Assert.Equal("Protected", SubmissionField(state, "hidden").DeclaredAccessibility.ToString())
        Assert.Contains("hidden", state.Variables.Select(Function(v) v.Name))

        Dim caller = Await state.ContinueWithAsync("? hidden + 1")
        Assert.Equal(7, caller.ReturnValue)
    End Function

    ''' <summary>
    ''' The paired negative for #11 (the C# baseline's <c>NestedVisibility</c>, IS:326): the widening above applies
    ''' to the submission's own members only, not to a private member of a nested type, which is rejected from a
    ''' later submission with BC30389/BC30390. The positive control in the same source (a Public nested class read
    ''' through the same path) is what makes the negative decisive: without it the failure could be blamed on the
    ''' access path rather than on the accessibility.
    ''' </summary>
    <Fact>
    Public Async Function TopLevelVisibility_PrivateNestedMember_IsNotVisibleToLaterSubmissions() As Task
        Dim state = Await VisualBasicScript.Create(
            "Class Holder" & vbCrLf &
            "    Private Class Hidden" & vbCrLf &
            "        Public Shared Function V() As Integer" & vbCrLf &
            "            Return 1" & vbCrLf &
            "        End Function" & vbCrLf &
            "    End Class" & vbCrLf &
            "    Public Class Exposed" & vbCrLf &
            "        Public Shared Function V() As Integer" & vbCrLf &
            "            Return 2" & vbCrLf &
            "        End Function" & vbCrLf &
            "    End Class" & vbCrLf &
            "End Class", s_options).RunAsync()

        Dim blocked = state.Script.ContinueWith("? Holder.Hidden.V()")
        Dim ids = blocked.Compile().Select(Function(d) d.Id).ToArray()
        Assert.Contains("BC30389", ids)
        Assert.Contains("BC30390", ids)

        Dim allowed = state.Script.ContinueWith("? Holder.Exposed.V()")
        Assert.DoesNotContain(allowed.Compile(), Function(d) d.Severity = DiagnosticSeverity.Error)
        Assert.Equal(2, Await allowed.EvaluateAsync())
    End Function

    Private Shared Function SubmissionField(state As ScriptState, name As String) As IFieldSymbol
        Dim submission = state.Script.GetCompilation().GetTypeByMetadataName("Submission#0")
        Assert.NotNull(submission)
        Return submission.GetMembers(name).OfType(Of IFieldSymbol)().Single()
    End Function

#End Region

#Region "12 - recursive base type"

    ''' <summary>
    ''' U9 #12 (C# <c>RecursiveBaseType</c>, IS:732): <c>B(Of T) : A(Of B(Of B(Of T)))</c> is accepted by the script
    ''' submission and the submission still runs. The assertion is the return value, not the mere absence of an
    ''' exception: a submission that failed to bind the declaration would be rejected before it produced 7. Note the
    ''' boundary: *instantiating* B is refused by the runtime with TypeLoadException ("has recursive generic
    ''' definition"), which is the CLR's rule and matches the C# baseline's shape, which also only declares.
    ''' </summary>
    <Fact>
    Public Async Function RecursiveBaseType_DeclaresAndRuns() As Task
        Dim script = VisualBasicScript.Create(
            "Class A(Of T)" & vbCrLf &
            "End Class" & vbCrLf &
            "Class B(Of T)" & vbCrLf &
            "    Inherits A(Of B(Of B(Of T)))" & vbCrLf &
            "End Class" & vbCrLf &
            "Return 7", s_options)

        Assert.DoesNotContain(script.Compile(), Function(d) d.Severity = DiagnosticSeverity.Error)
        Dim state = Await script.RunAsync()
        Assert.Equal(7, state.ReturnValue)
    End Function

#End Region

#Region "extra family 21 - Dynamic_Expando"

    ''' <summary>
    ''' Extra family 21 (C# <c>Dynamic_Expando</c>, IS:209): an ExpandoObject written and read through late binding
    ''' across three submissions. <c>goo</c> does not exist as a member of anything, so 1 can only come from the
    ''' member added two submissions earlier reaching the late binder of the third; the reference to
    ''' System.Linq.Expressions is what makes the type resolvable (BC30002 without it, measured).
    ''' </summary>
    <Fact>
    Public Async Function DynamicExpando_LateBoundMembersAcrossSubmissions() As Task
        Dim options = s_options.
            AddReferences(GetType(System.Dynamic.ExpandoObject).Assembly).
            AddImports("System.Dynamic")

        Dim value = Await VisualBasicScript.Create(
            "Dim expando As Object = New ExpandoObject()", options).
            ContinueWith("expando.goo = 1").
            ContinueWith("? expando.goo").
            EvaluateAsync()

        Assert.Equal(1, value)

        Dim unresolved = VisualBasicScript.Create("Dim expando As Object = New System.Dynamic.ExpandoObject()", s_options)
        Assert.Contains("BC30002", unresolved.Compile().Select(Function(d) d.Id))
    End Function

#End Region

#Region "extra family 31 - closures, extension methods, PrivateImplementationDetails"

    ''' <summary>
    ''' Extra family 31 cell one (C# <c>TestInteractiveClosures</c>, IS:982): a lambda declared by a later submission
    ''' captures a field declared by an earlier one and writes it back, so the two readings differ (1 then 2). A
    ''' closure over a copy, or one that could not see the earlier submission's field, would answer 1 and 1.
    ''' </summary>
    <Fact>
    Public Async Function ClosureCapture_CrossesSubmissions() As Task
        Dim script = VisualBasicScript.Create(
            "Shared collected As New System.Collections.Generic.List(Of Integer)()", s_options).
            ContinueWith("Dim x As Integer = 1").
            ContinueWith(
                "Dim step1 As System.Func(Of Integer) = Function()" & vbCrLf &
                "    Dim previous As Integer = x" & vbCrLf &
                "    x += 1" & vbCrLf &
                "    Return previous" & vbCrLf &
                "End Function").
            ContinueWith("collected.Add(step1())").
            ContinueWith("collected.Add(x)").
            ContinueWith("? collected.Count & "":"" & collected(0) & "":"" & collected(1)")

        Assert.Equal("2:1:2", Await script.EvaluateAsync())
    End Function

    ''' <summary>
    ''' Extra family 31 cell two (C# <c>ExtensionMethods</c>, IS:999): LINQ extension methods are usable at the top
    ''' level of a submission. 2 is the exact answer of the C# baseline's expression over the same array; an
    ''' implementation that lost extension method lookup at the top level would report BC30456 instead.
    ''' </summary>
    <Fact>
    Public Async Function ExtensionMethods_AreUsableAtTheTopLevel() As Task
        Dim options = s_options.
            AddReferences(GetType(Enumerable).Assembly).
            AddImports("System.Linq")

        Dim value = Await VisualBasicScript.Create(
            "Dim fruit() As String = {""banana"", ""orange"", ""lime"", ""apple"", ""kiwi""}", options).
            ContinueWith("? fruit.Skip(1).Where(Function(s) s.Length > 4).Count()").
            EvaluateAsync()

        Assert.Equal(2, value)
    End Function

    ''' <summary>
    ''' Extra family 31 cell three (C# <c>PrivateImplementationDetailsType</c>, IS:1036): the synthesized
    ''' PrivateImplementationDetails type is unique per submission. The C# baseline only checks the array values;
    ''' this cell reads the emitted metadata of each chained submission and asserts that each carries exactly one
    ''' <c>&lt;PrivateImplementationDetails&gt;N</c> type and that the three names differ - which is what stops two
    ''' submissions in one process from colliding on the synthesized container. The values are asserted as well, so a
    ''' rename that also broke the initializer would still fail.
    ''' </summary>
    <Fact>
    Public Async Function PrivateImplementationDetails_AreUniqueAcrossSubmissions() As Task
        Dim first = Await VisualBasicScript.RunAsync(Of Integer())(
            "Return New Integer() {1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16}", s_options)
        Dim second = Await first.ContinueWithAsync(Of Integer())(
            "Return New Integer() {1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17}", s_options)
        Dim third = Await second.ContinueWithAsync(Of Integer())(
            "Return New Integer() {1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18}", s_options)

        Assert.Equal(16, first.ReturnValue.Length)
        Assert.Equal(17, second.ReturnValue.Length)
        Assert.Equal(18, third.ReturnValue.Length)
        Assert.Equal(16, first.ReturnValue(15))
        Assert.Equal(17, second.ReturnValue(16))
        Assert.Equal(18, third.ReturnValue(17))

        Dim names = New List(Of String)
        For Each state In {DirectCast(first, ScriptState), second, third}
            Dim elaborated = ElaboratedContainerNames(state.Script.GetCompilation())
            Assert.Equal(1, elaborated.Count)
            names.Add(elaborated(0))
        Next

        Assert.Equal(3, names.Distinct(StringComparer.Ordinal).Count())
    End Function

    ''' <summary>The names of the emitted synthetic containers, read from the metadata of an in memory emit.</summary>
    Private Shared Function ElaboratedContainerNames(compilation As Compilation) As List(Of String)
        Dim names = New List(Of String)
        Using stream = New MemoryStream()
            Dim result = compilation.Emit(stream)
            Assert.True(result.Success, String.Join(Environment.NewLine, result.Diagnostics))
            stream.Position = 0
            Using reader = New PEReader(stream)
                Dim metadata = reader.GetMetadataReader()
                For Each handle In metadata.TypeDefinitions
                    Dim name = metadata.GetString(metadata.GetTypeDefinition(handle).Name)
                    If name.StartsWith("<PrivateImplementationDetails", StringComparison.Ordinal) Then
                        names.Add(name)
                    End If
                Next
            End Using
        End Using
        names.Sort(StringComparer.Ordinal)
        Return names
    End Function

#End Region

#Region "extra family 41 - implicit receiver with a ByRef argument"

    ''' <summary>
    ''' Extra family 41 (C# <c>MethodCallWithImplicitReceiverAndOutVar</c>, IS:1836; VB has no <c>out var</c>, so the
    ''' VB half is the ByRef write back). The host object's method is called without a receiver and its ByRef
    ''' parameter has to arrive back in the submission's field: 5, not 0. The paired negative shows the call is not
    ''' simply late bound into thin air: a ByRef parameter cannot take an expression that has no storage
    ''' (BC30491). An implementation that passed the argument by value would answer 0 for the first case.
    ''' </summary>
    <Fact>
    Public Async Function ImplicitReceiverWithByRefArgument_WritesBackThroughTheHost() As Task
        Dim state = Await VisualBasicScript.RunAsync(
            "Dim target As Integer" & vbCrLf &
            "Fill(target)" & vbCrLf &
            "? target", s_options, globals:=New ByRefGlobals())

        Assert.Equal(5, state.ReturnValue)

        Dim noStorage = VisualBasicScript.Create("? Fill(3)", s_options, GetType(ByRefGlobals))
        Assert.Contains("BC30491", noStorage.Compile().Select(Function(d) d.Id))
    End Function

    Public Class ByRefGlobals
        Public Sub Fill(ByRef value As Integer)
            value = 5
        End Sub
    End Class

#End Region

#Region "extra family 53 - SharedLibCopy_Different"

    ''' <summary>
    ''' Extra family 53 cell one (C# <c>SharedLibCopy_Different</c>, CLR:779). The C# baseline writes two same
    ''' named DLLs with different contents into a temp directory and expects <c>AssemblyAlreadyLoaded</c> for the
    ''' second one. Both inputs of that branch are available without writing anything:
    ''' <list type="bullet">
    ''' <item><description>the already loaded assembly with the same simple name is emitted in memory and handed to
    ''' the loader through <c>RegisterDependency(Assembly)</c> (<c>InteractiveAssemblyLoader.cs:296</c>), which
    ''' registers it with no location;</description></item>
    ''' <item><description>the file the branch probes for is the copy of that same assembly which already sits next
    ''' to the test assembly. The probe only reads it - <c>FindExistingAssemblyFile</c> (<c>:448</c>) calls
    ''' <c>File.Exists</c> and <c>TryReadMvid</c> (<c>:480-499</c>) opens with
    ''' <c>FileShare.ReadWrite | Delete</c> - so nothing is created, moved or deleted.</description></item>
    ''' </list>
    ''' Falsifiable three ways. The control half (a second loader with no registration) has to answer the on disk
    ''' assembly - it reaches the same directory probe, so the throw really comes from the registration and not from
    ''' the directory argument. If the module version comparison (<c>:409-413</c>) were replaced by an identity only
    ''' comparison the loader would answer the in memory assembly and the throw would disappear, which is why the
    ''' MVID inequality is asserted. And the message is formatted from the same resource the loader formats, so a
    ''' throw from the other conflicting branch (<c>:430</c>, the paired case below) cannot satisfy it.
    ''' </summary>
    <Fact>
    Public Sub SharedLibCopy_Different_ConflictingContentsAreRejected()
        Dim loader = New InteractiveAssemblyLoader()
        Dim inMemory = EmitInMemoryAssembly(ConflictingAssemblyName, assemblyVersion:=Nothing)
        loader.RegisterDependency(inMemory)

        Dim requested = AssemblyIdentity.FromAssemblyDefinition(inMemory)
        Assert.Equal(ConflictingAssemblyName, requested.Name)
        Assert.False(requested.IsStrongName)

        Dim onDisk = Path.Combine(AppContext.BaseDirectory, ConflictingAssemblyName & ".dll")
        Assert.True(File.Exists(onDisk), onDisk)
        Assert.NotEqual(inMemory.ManifestModule.ModuleVersionId, ReadModuleVersionId(onDisk))

        Dim thrown = Assert.Throws(Of InteractiveAssemblyLoaderException)(
            Function() loader.ResolveAssembly(requested, AppContext.BaseDirectory))

        Assert.Equal(
            String.Format(
                CultureInfo.CurrentCulture,
                ScriptingResources.AssemblyAlreadyLoaded,
                requested.Name,
                requested.Version.ToString(),
                Nothing,
                onDisk),
            thrown.Message)

        Dim control = New InteractiveAssemblyLoader().ResolveAssembly(requested, AppContext.BaseDirectory)
        Assert.NotSame(inMemory, control)
        Assert.Equal(ConflictingAssemblyName, control.GetName().Name)
    End Sub

    ''' <summary>
    ''' The paired negative of extra family 53: the *other* throw of the same branch
    ''' (<c>InteractiveAssemblyLoader.cs:430</c>, <c>AssemblyAlreadyLoadedNotSigned</c>). It is reached when the
    ''' loaded assembly of the same simple name is weak named but its version differs from the requested one, so
    ''' the equal name and version probe at <c>:396-398</c> finds nothing and the unsigned conflict check at
    ''' <c>:426</c> takes over. The cell above and this one are distinguished by their messages, which is what keeps
    ''' the first one from passing on the strength of either throw. Note that the requested identity is built by
    ''' hand here: it has to disagree with the emitted version while keeping the same simple name.
    ''' </summary>
    <Fact>
    Public Sub SharedLibCopy_DifferentVersion_UnsignedConflictIsRejected()
        Dim loader = New InteractiveAssemblyLoader()
        Dim inMemory = EmitInMemoryAssembly(ConflictingAssemblyName, assemblyVersion:="9.9.9.9")
        loader.RegisterDependency(inMemory)

        Dim requested = New AssemblyIdentity(ConflictingAssemblyName, New Version(0, 0, 0, 0))
        Assert.False(requested.IsStrongName)
        Assert.NotEqual(requested.Version, inMemory.GetName().Version)

        Dim onDisk = Path.Combine(AppContext.BaseDirectory, ConflictingAssemblyName & ".dll")
        Assert.True(File.Exists(onDisk), onDisk)

        Dim thrown = Assert.Throws(Of InteractiveAssemblyLoaderException)(
            Function() loader.ResolveAssembly(requested, AppContext.BaseDirectory))

        Assert.Equal(
            String.Format(
                CultureInfo.CurrentCulture,
                ScriptingResources.AssemblyAlreadyLoadedNotSigned,
                ConflictingAssemblyName,
                Nothing,
                onDisk),
            thrown.Message)
    End Sub

    Private Const ConflictingAssemblyName As String = "Microsoft.CodeAnalysis.Scripting"

    ''' <summary>Emits a weak named library with the given simple name entirely in memory, so the conflict the
    ''' loader has to reject can be produced without a file. <paramref name="assemblyVersion"/> is Nothing for the
    ''' compiler default (0.0.0.0) and is otherwise written as an assembly level attribute.</summary>
    Private Shared Function EmitInMemoryAssembly(simpleName As String, assemblyVersion As String) As Assembly
        Dim trees As New List(Of SyntaxTree)()
        trees.Add(VisualBasicSyntaxTree.ParseText(
            "Public Class Marker" & vbCrLf & "End Class",
            path:="marker.vb"))
        If assemblyVersion IsNot Nothing Then
            trees.Add(VisualBasicSyntaxTree.ParseText(
                "<Assembly: System.Reflection.AssemblyVersion(""" & assemblyVersion & """)>",
                path:="version.vb"))
        End If

        Dim compilation = VisualBasicCompilation.Create(
            simpleName,
            trees,
            {
                MetadataReference.CreateFromFile(GetType(Object).Assembly.Location),
                MetadataReference.CreateFromFile(GetType(AssemblyIdentity).Assembly.Location)
            },
            New VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary))

        Using stream = New MemoryStream()
            Dim result = compilation.Emit(stream)
            Assert.True(result.Success, String.Join(Environment.NewLine, result.Diagnostics))
            Return Assembly.Load(stream.ToArray())
        End Using
    End Function

    ''' <summary>The MVID of a file on disk, read with the same sharing mode the loader uses. Read only.</summary>
    Private Shared Function ReadModuleVersionId(path As String) As Guid
        Using stream = New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
            Using reader = New PEReader(stream)
                Dim metadata = reader.GetMetadataReader()
                Return metadata.GetGuid(metadata.GetModuleDefinition().Mvid)
            End Using
        End Using
    End Function

#End Region

#Region "extra family 45 - LocalFunction_PreviousSubmissionAndGlobal"

    ''' <summary>
    ''' Extra family 45 (C# <c>LocalFunction_PreviousSubmissionAndGlobal</c>, IS:2126). VB has no local function
    ''' (<c>LocalFunctionStatementSyntax</c> has no occurrence in <c>Compilers\VisualBasic</c>; the only member of
    ''' that name is the empty <c>UsedLocalFunctions</c> override), so the VB dual of the C# cell is a lambda in
    ''' place of the local function: the later submission's lambda calls the function the earlier submission
    ''' declared, and reads the host object's member. Both halves have to arrive for the value to be 4 - <c>Y</c>
    ''' is the host object's and <c>InInitialSubmission</c> is submission 0's, and the call has to reach the
    ''' previous submission's body rather than answer 2. A lambda that could not see the earlier submission's member
    ''' would fail to compile (BC30451), and one bound to a private copy of <c>Y</c> could not answer 4.
    ''' </summary>
    <Fact>
    Public Async Function LocalFunctionDual_LaterLambdaCallsThePreviousSubmissionsFunction() As Task
        Dim state = Await VisualBasicScript.
            Create("Function InInitialSubmission() As Integer" & vbCrLf &
                   "    Return Y" & vbCrLf &
                   "End Function",
                   s_options, globalsType:=GetType(LocalFunctionDualGlobals)).
            ContinueWith("Dim lambda As System.Func(Of Integer) = Function()" & vbCrLf &
                         "    Return Y + InInitialSubmission()" & vbCrLf &
                         "End Function" & vbCrLf &
                         "Return lambda()").
            RunAsync(New LocalFunctionDualGlobals())

        Assert.Equal(4, state.ReturnValue)
    End Function

    ''' <summary>The host object of the cell above; its single member is the global the C# fixture calls <c>Y</c>.</summary>
    Public Class LocalFunctionDualGlobals
        Public ReadOnly Property Y As Integer = 2
    End Class

#End Region

End Class
