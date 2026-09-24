' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' SW-F02 (submission-member-redeclaration-tiebreak, test-plan.md §一): the host side value cells for the
' decision-layer tie-break added in OverloadResolution.CombineCandidates. A redeclared member of the same name
' and signature is answered by the newest definition with no diagnostic at all (C# ScriptTests.cs:452 expects
' the same 25), while differently signed members keep overloading (100/200) and the host object keeps the
' precedence it already had.
'
' The baseline of every cell is the pre-fix reading of the published host 2.0.0-Beta (test-plan §〇, probes in
' tmp\probe-sweep\): p8b-fn-redeclare / p8c-prop / p8c-shadows-fn reported BC30521, the others read the values
' that are re-pinned here unchanged.

Imports System
Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.IO
Imports System.Linq
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting
Imports Roslyn.Test.Utilities
Imports Xunit

''' <summary>
''' Cross-submission redeclaration of a method or property: the newest definition wins, alone, and without a
''' diagnostic. Counter-cells: real overloads (different signatures) survive, a duplicate inside a single
''' submission is still rejected, and a host object member of the same name is untouched by the rule.
''' </summary>
Public Class ScriptSubmissionMemberRedeclarationTests

    Private Shared ReadOnly s_options As ScriptOptions = ScriptModeConformance.DefaultOptions

#Region "harness"

    Private Shared Function Lines(ParamArray text() As String) As String
        Return String.Join(vbCrLf, text)
    End Function

    ''' <summary>A function named M that answers <paramref name="body"/> for its Integer argument.</summary>
    Private Shared Function FunctionM(body As String) As String
        Return Lines("Function M(x As Integer) As Integer", "    Return " & body, "End Function")
    End Function

    ''' <summary>A read-only property P whose getter answers <paramref name="value"/>.</summary>
    Private Shared Function PropertyP(value As Integer) As String
        Return Lines("ReadOnly Property P As Integer", "    Get", $"        Return {value}", "    End Get", "End Property")
    End Function

    ''' <summary>
    ''' The cell's submission is accepted: no diagnostic at all, warnings included - the criterion is "zero
    ''' diagnostics", and the message carries the source and every diagnostic so a stray BC4xxxx is readable.
    ''' </summary>
    Private Shared Sub AssertNoDiagnostics(script As Script)
        Dim diagnostics = script.Compile()
        Assert.True(diagnostics.Length = 0, Describe(script, diagnostics))
    End Sub

    Private Shared Function Describe(script As Script, diagnostics As ImmutableArray(Of Diagnostic)) As String
        Dim builder As New System.Text.StringBuilder()
        builder.AppendLine("--- submission ---")
        For Each line In script.Code.Replace(vbCrLf, vbLf).Split(ControlChars.Lf)
            builder.AppendLine("| " & line)
        Next

        builder.AppendLine("--- diagnostics ---")
        If diagnostics.IsDefaultOrEmpty Then
            builder.AppendLine("(none)")
        Else
            For Each diagnostic In diagnostics
                builder.AppendLine($"{diagnostic.Id} {diagnostic.Severity}: {diagnostic.GetMessage()}")
            Next
        End If

        Return builder.ToString()
    End Function

    ''' <summary>
    ''' A source resolver over an in-memory file set, so that a <c>#Load</c> cell needs nothing on disk. Same
    ''' shape as the one in ImportsAccumulationFailureTests.vb:370.
    ''' </summary>
    Private NotInheritable Class InMemorySourceResolver
        Inherits SourceReferenceResolver

        Private ReadOnly _files As IReadOnlyDictionary(Of String, String)

        Public Sub New(files As IReadOnlyDictionary(Of String, String))
            _files = files
        End Sub

        Public Overrides Function NormalizePath(path As String, baseFilePath As String) As String
            Return If(ResolveReference(path, baseFilePath), path)
        End Function

        Public Overrides Function ResolveReference(path As String, baseFilePath As String) As String
            If _files.ContainsKey(path) Then
                Return path
            End If

            If baseFilePath IsNot Nothing Then
                Dim combined = IO.Path.Combine(IO.Path.GetDirectoryName(baseFilePath), path)
                If _files.ContainsKey(combined) Then
                    Return combined
                End If
            End If

            Return Nothing
        End Function

        Public Overrides Function OpenRead(resolvedPath As String) As Stream
            Return New MemoryStream(System.Text.Encoding.UTF8.GetBytes(_files(resolvedPath)))
        End Function

        Public Overrides Function ReadText(resolvedPath As String) As SourceText
            Return SourceText.From(_files(resolvedPath))
        End Function

        Public Overrides Function Equals(other As Object) As Boolean
            Return ReferenceEquals(Me, other)
        End Function

        Public Overrides Function GetHashCode() As Integer
            Return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Me)
        End Function
    End Class

#End Region

#Region "G1 - G3: newest definition wins"

    ''' <summary>
    ''' G1 (criterion 1, and the C# baseline <c>TestBranchingSubscripts</c> value): a chain that redeclares
    ''' <c>Function M(x As Integer)</c> compiles clean and answers from the newest body - 25, not 10 and not the
    ''' BC30521 the pre-fix host reported for exactly this shape (probe p8b-fn-redeclare).
    ''' </summary>
    <Fact>
    Public Async Function G1_RedeclaredFunction_EvaluatesTheNewestDefinitionWithoutDiagnostics() As Task
        Dim first As ScriptState = Await VisualBasicScript.RunAsync(FunctionM("x + x"), s_options)

        Dim redeclared = first.Script.ContinueWith(Lines(FunctionM("x * x"), "? M(5)"))
        AssertNoDiagnostics(redeclared)
        Assert.Equal(25, Await redeclared.EvaluateAsync())

        ' A second continuation grown from the same state does not carry the redeclaration of the other branch,
        ' so it still answers the body of the only submission it contains: 10, not 25.
        Assert.Equal(10, Await first.Script.ContinueWith("? M(5)").EvaluateAsync())
    End Function

    ''' <summary>
    ''' G2 (criterion 2): the same shape on a <c>ReadOnly Property</c>, which is the VB-only half of the
    ''' criterion - C# has no property to redeclare, and the fork deliberately keeps properties overloadable
    ''' instead of copying <c>IsMethodOrIndexer</c>. Measured: clean, and the read answers from the newest
    ''' getter (2, not 1); the pre-fix host read BC30521 (probe p8c-prop).
    ''' </summary>
    <Fact>
    Public Async Function G2_RedeclaredProperty_EvaluatesTheNewestDefinitionWithoutDiagnostics() As Task
        Dim first As ScriptState = Await VisualBasicScript.RunAsync(PropertyP(1), s_options)

        Dim redeclared = first.Script.ContinueWith(Lines(PropertyP(2), "? P"))
        AssertNoDiagnostics(redeclared)
        Assert.Equal(2, Await redeclared.EvaluateAsync())
    End Function

    ''' <summary>
    ''' G3: a three link chain. Slot comparison is positional, so the reading that has to hold is "the newest of
    ''' three", and each intermediate state re-evaluated to the end reports the last body as well.
    ''' </summary>
    <Fact>
    Public Async Function G3_ThreeLinkChain_EvaluatesTheNewestOfThree() As Task
        Dim first As ScriptState = Await VisualBasicScript.RunAsync(FunctionM("x + x"), s_options)
        Dim second As ScriptState = Await first.ContinueWithAsync(FunctionM("x * x"))
        Dim third = second.Script.ContinueWith(Lines(FunctionM("x * 3"), "? M(5)"))

        AssertNoDiagnostics(third)
        Assert.Equal(15, Await third.EvaluateAsync())

        ' Re-running the two link chain to the end still answers the second body: nothing about the third link
        ' leaks back into a chain that never grew it.
        Assert.Equal(25, Await first.Script.ContinueWith(Lines(FunctionM("x * x"), "? M(5)")).EvaluateAsync())
    End Function

#End Region

#Region "G4: the #Load / multi tree axis"

    ''' <summary>
    ''' G4: the newest definition wins also when the definition it wins over came from a <c>#Load</c>ed tree.
    ''' The loaded file is part of the first submission's compilation (its own tree, before the interactive one),
    ''' so this is the cell that would fail if the tie-break compared "is this symbol from a referenced
    ''' assembly" instead of the submission slot.
    ''' </summary>
    <Fact>
    Public Async Function G4_FunctionDeclaredByALoadedTree_IsRedeclaredByTheNextSubmission() As Task
        Dim files As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"C:\sw-f02\loaded.vbx", FunctionM("x + x")},
            {"C:\sw-f02\main.vbx", Lines("#Load ""loaded.vbx""", "? M(5)")}
        }

        Dim options = s_options.
            WithFilePath("C:\sw-f02\main.vbx").
            WithSourceResolver(New InMemorySourceResolver(files))

        Dim loaded = VisualBasicScript.Create(files("C:\sw-f02\main.vbx"), options)
        AssertNoDiagnostics(loaded)
        Assert.Equal(10, Await loaded.EvaluateAsync())

        Dim redeclared = loaded.ContinueWith(Lines(FunctionM("x * x"), "? M(5)"))
        AssertNoDiagnostics(redeclared)
        Assert.Equal(25, Await redeclared.EvaluateAsync())
    End Function

#End Region

#Region "G6, G7: what the rule must not touch"

    ''' <summary>
    ''' G6 (criterion 3, the baseline pair 100/200): differently signed members of the same name declared by
    ''' different submissions are real overloads, and the argument type still decides. A fix that truncated the
    ''' lookup, or a tie-break without the signatureMatch guard, loses one of these two readings.
    ''' </summary>
    <Fact>
    Public Async Function G6_CrossSubmissionOverloads_StillResolveByArgumentType() As Task
        Dim first As ScriptState = Await VisualBasicScript.RunAsync(
            Lines("Function M(x As Integer) As Integer", "    Return 100", "End Function"), s_options)

        Dim withStringOverload As ScriptState = Await first.ContinueWithAsync(
            Lines("Function M(s As String) As Integer", "    Return 200", "End Function"))

        Dim integerCall = withStringOverload.Script.ContinueWith("? M(5)")
        AssertNoDiagnostics(integerCall)
        Assert.Equal(100, Await integerCall.EvaluateAsync())

        Dim stringCall = withStringOverload.Script.ContinueWith("? M(""a"")")
        AssertNoDiagnostics(stringCall)
        Assert.Equal(200, Await stringCall.EvaluateAsync())
    End Function

    ''' <summary>
    ''' G7 (criterion 6): a host object member of the same name as a submission member. The lookup consults the
    ''' host object only when the submission chain has nothing to offer, so precedence is unchanged by a rule
    ''' that fires between two submission candidates. Both axes are run: the host member alone (6) and the host
    ''' member next to a same signed submission member (10).
    ''' </summary>
    <Fact>
    Public Async Function G7_HostObjectMemberOfTheSameName_KeepsItsVisibilityAndPrecedence() As Task
        Dim hostOnly = VisualBasicScript.Create(Of Object)("? HostM(5)", s_options, GetType(RedeclarationGlobals))
        AssertNoDiagnostics(hostOnly)
        Assert.Equal(6, Await hostOnly.EvaluateAsync(New RedeclarationGlobals()))

        Dim declared = VisualBasicScript.Create(Of Object)(FunctionM("x * 2"), s_options, GetType(RedeclarationGlobals))
        AssertNoDiagnostics(declared)
        Dim state As ScriptState = Await declared.RunAsync(New RedeclarationGlobals())

        ' The submission member shadows the host member of the same name, as it did before the tie-break existed.
        Dim callSite = state.Script.ContinueWith("? M(5)")
        AssertNoDiagnostics(callSite)
        Assert.Equal(10, Await callSite.EvaluateAsync(New RedeclarationGlobals()))
    End Function

    Public Class RedeclarationGlobals
        Public Function HostM(x As Integer) As Integer
            Return x + 1
        End Function
    End Class

#End Region

#Region "R1, R2, G5: counter-example locks"

    ''' <summary>
    ''' R1 (lock): the field face is untouched - <c>Dim x</c> redeclared answers the newest initializer (2, the
    ''' baseline probe p8-dim-redeclare), and so does the change of type across submissions (p8b second half,
    ''' "hello" and its length 5). The tie-break is not reached by either: a field is not overloadable, so the
    ''' lookup layer already stopped at the newest submission.
    ''' </summary>
    <Fact>
    Public Async Function R1_FieldRedeclaration_KeepsReadingTheNewestField() As Task
        Dim first As ScriptState = Await VisualBasicScript.RunAsync("Dim x As Integer = 1", s_options)

        Dim redeclared = first.Script.ContinueWith(Lines("Dim x As Integer = 2", "? x"))
        AssertNoDiagnostics(redeclared)
        Assert.Equal(2, Await redeclared.EvaluateAsync())

        Dim typed As ScriptState = Await VisualBasicScript.RunAsync("Dim y As Integer = 7", s_options)
        Dim changed = typed.Script.ContinueWith(Lines("Dim y As String = ""hello""", "? y"))
        AssertNoDiagnostics(changed)
        Assert.Equal("hello", Await changed.EvaluateAsync())

        Dim reTyped As ScriptState = Await typed.ContinueWithAsync("Dim y As String = ""hello""")
        Dim length = reTyped.Script.ContinueWith("? y.Length")
        AssertNoDiagnostics(length)
        Assert.Equal(5, Await length.EvaluateAsync())
    End Function

    ''' <summary>
    ''' R2 (lock): writing <c>Shadows</c> or <c>Overloads</c> on a redeclared submission member changes nothing -
    ''' no extra diagnostic, and the same newest definition wins. The pre-fix host still reported BC30521 with an
    ''' explicit <c>Shadows</c> (probe p8c-shadows-fn); the modifier is not given any meaning here.
    ''' </summary>
    <Fact>
    Public Async Function R2_ShadowsAndOverloads_StayInertOnTheChain() As Task
        Dim first As ScriptState = Await VisualBasicScript.RunAsync(FunctionM("x + x"), s_options)

        Dim withShadows = first.Script.ContinueWith(Lines(
            Lines("Shadows Function M(x As Integer) As Integer", "    Return x * x", "End Function"), "? M(5)"))
        AssertNoDiagnostics(withShadows)
        Assert.Equal(25, Await withShadows.EvaluateAsync())

        Dim withOverloads = first.Script.ContinueWith(Lines(
            Lines("Overloads Function M(x As Integer) As Integer", "    Return x * x", "End Function"), "? M(5)"))
        AssertNoDiagnostics(withOverloads)
        Assert.Equal(25, Await withOverloads.EvaluateAsync())
    End Function

    ''' <summary>
    ''' G5 (criterion 4 lock, host side): the duplicates have to be in *different* submissions to be resolved by
    ''' position. Two same signed declarations inside one submission stay rejected with BC30521, i.e. the new
    ''' rule does not silently accept a genuine redeclaration in a single link.
    ''' </summary>
    <Fact>
    Public Async Function G5_DuplicateInsideOneSubmission_IsStillRejected() As Task
        Dim duplicated = VisualBasicScript.Create(Lines(FunctionM("x + x"), FunctionM("x * x"), "? M(5)"), s_options)

        Dim ids = duplicated.Compile().Where(Function(d) d.Severity = DiagnosticSeverity.Error).Select(Function(d) d.Id).ToArray()
        Assert.Equal(New String() {"BC30521"}, ids)

        Dim failure As Exception = Nothing
        Try
            Await duplicated.RunAsync()
        Catch ex As Exception
            failure = ex
        End Try

        Assert.IsType(Of CompilationErrorException)(failure)
    End Function

#End Region

End Class
