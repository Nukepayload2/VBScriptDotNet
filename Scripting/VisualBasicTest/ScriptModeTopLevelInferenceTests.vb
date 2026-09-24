' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System
Imports System.Linq
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting
Imports Xunit

''' <summary>
''' Issue 32 (script-top-level-dim-type-inference): a top level <c>Dim</c> with no <c>As</c> clause now
''' carries the same <c>Option Infer</c> static type an equivalent method-body local would, instead of
''' <c>Object</c> (SourceMemberFieldSymbol.ComputeType / TryComputeScriptFieldType, gated to
''' IsScriptClass). This file used to pin the opposite - "a top level Dim is Object" - as a registered
''' divergence (design §U1); the author reclassified that as a defect to fix, so the two cells that
''' asserted Object are now positive proofs that the field is inferred.
''' <para>
''' The A1 report "a top level LINQ Group By query throws <c>InvalidCastException</c>" was the downstream
''' cost of the old behaviour: the Object field forced the follow-up member access through the late binder.
''' With the field inferred to the query's <c>IEnumerable</c> of the anonymous projection, that member call
''' binds early and the shape evaluates - which is what the (formerly "negative") cell now asserts.
''' </para>
''' <para>
''' The discriminator for "was the field inferred?" is <b>overload resolution</b>, never <c>GetType()</c>:
''' the pair <c>Tell(o As Object)</c> / <c>Tell(items As IEnumerable)</c> answers differently for the two
''' static types - the <c>IEnumerable</c> parameter is the more specific of two widening reference
''' conversions, so it wins only when the argument really is a sequence. <c>SubLocalQuery_IsSequence</c> is
''' the control that keeps the pair sensitive to inference at all.
''' </para>
''' <para>
''' No file is written, no process is started, and there is no registry or network access. The one assembly
''' reference these cases add is resolved from a path that already exists on disk and opened read-only by the
''' metadata layer.
''' </para>
''' </summary>
Public Class ScriptModeTopLevelInferenceTests

    ''' <summary>
    ''' <see cref="ScriptModeConformance.DefaultOptions"/> carries only the scripting defaults, and the query
    ''' constructors (<c>Select</c>/<c>Where</c>/<c>GroupBy</c>) plus <c>IGrouping</c> live in System.Linq, which
    ''' those defaults do not reference - without the reference a query reports BC36593 and nothing else. No file is
    ''' written: the reference comes from an assembly that is already on disk, which the metadata layer opens
    ''' read-only (<c>MetadataReference.CreateFromFile</c>).
    ''' </summary>
    Private Shared ReadOnly WithLinqOptions As ScriptOptions =
        ScriptModeConformance.DefaultOptions.
            AddReferences(GetType(Enumerable).Assembly).
            AddImports("System.Linq")

    ''' <summary>
    ''' The shape the A1 probes used (tmp\probes\u14\r5\decisive.py): group words by length, project
    ''' (length, count). Over <c>{"aa", "b", "ccc", "dd"}</c> the groups are 2:2, 1:1, 3:1 in first appearance
    ''' order. The same text is used by every cell, so the cells differ only in where the declaration lives and in
    ''' whether it carries an <c>As</c> clause. The projection is anonymous, so the element type has no name that
    ''' an <c>As</c> clause could spell; the compiler infers the <c>IEnumerable</c> of the anonymous type instead.
    ''' </summary>
    Private Const GroupingQuery As String =
        "From w In words Group By k = w.Length Into g = Group Select k, c = g.Count()"

    Private Const Words As String = "Dim words As String() = {""aa"", ""b"", ""ccc"", ""dd""}"

    Private Const ExpectedGroups As String = "2:2,1:1,3:1"

    ''' <summary>
    ''' The overload pair that reads the static type of its argument: an <c>Object</c> argument takes the identity
    ''' conversion to the <c>Object</c> parameter, a sequence argument takes the more specific widening to
    ''' <c>IEnumerable</c>. Under the default <c>Option Strict Off</c> the <c>Object</c> to <c>IEnumerable</c> route
    ''' would be a narrowing conversion, so it never wins by accident.
    ''' </summary>
    Private Const TellOverloads As String =
        "Function Tell(o As Object) As String" & vbCrLf &
        "    Return ""object""" & vbCrLf &
        "End Function" & vbCrLf &
        "Function Tell(items As System.Collections.IEnumerable) As String" & vbCrLf &
        "    Return ""sequence""" & vbCrLf &
        "End Function"

    ' ---- (1) the top level field is INFERRED (this is what issue 32 changed) ----

    ''' <summary>
    ''' A top level <c>Dim q = &lt;query&gt;</c> with no <c>As</c> clause now infers the query's
    ''' <c>IEnumerable</c> of the anonymous projection (like a local would), so the <c>Tell</c> overload pair
    ''' resolves to the <c>IEnumerable</c> arm and answers "sequence". Before issue 32 this cell asserted
    ''' "object" - the registered divergence that the author reclassified as a defect.
    ''' </summary>
    <Fact>
    Public Sub TopLevelQueryField_IsInferred_AsSequence()
        ScriptModeConformance.AssertRuns(
            "Option Infer On" & vbCrLf &
            "Dim answer As String = """"" & vbCrLf &
            TellOverloads & vbCrLf &
            Words & vbCrLf &
            "Dim q = " & GroupingQuery & vbCrLf &
            "answer = Tell(q)" & vbCrLf &
            "Return answer", "sequence", WithLinqOptions)
    End Sub

    ' ---- (2) the same query in a local, for the overload-pair control ----

    ''' <summary>
    ''' Control for the overload pair: the very same query in a local of a top level <c>Sub</c> is inferred, and
    ''' the pair answers "sequence" there. This keeps the reading above discriminating (if the pair answered
    ''' "object" for a known sequence, the inference cell would prove nothing).
    ''' </summary>
    <Fact>
    Public Sub SubLocalQuery_IsSequence()
        ScriptModeConformance.AssertRuns(
            "Option Infer On" & vbCrLf &
            "Dim answer As String = """"" & vbCrLf &
            TellOverloads & vbCrLf &
            "Sub Probe()" & vbCrLf &
            "    " & Words & vbCrLf &
            "    Dim q = " & GroupingQuery & vbCrLf &
            "    answer = Tell(q)" & vbCrLf &
            "End Sub" & vbCrLf &
            "Probe()" & vbCrLf &
            "Return answer", "sequence", WithLinqOptions)
    End Sub

    ''' <summary>
    ''' The same query in a local runs and produces the right groups.
    ''' </summary>
    <Fact>
    Public Sub SubLocalQuery_Evaluates()
        ScriptModeConformance.AssertRuns(
            "Option Infer On" & vbCrLf &
            "Dim answer As String = """"" & vbCrLf &
            "Sub Probe()" & vbCrLf &
            "    " & Words & vbCrLf &
            "    Dim q = " & GroupingQuery & vbCrLf &
            "    answer = String.Join("","", q.Select(Function(x) x.k & "":"" & x.c))" & vbCrLf &
            "End Sub" & vbCrLf &
            "Probe()" & vbCrLf &
            "Return answer", ExpectedGroups, WithLinqOptions)
    End Sub

    ''' <summary>
    ''' The escape hatch that predates the fix: the same query text with an explicit <c>As IEnumerable</c>
    ''' evaluates too. Kept to show the <c>As</c>-clause path is unchanged by inference (issue 32 §四判据 3).
    ''' </summary>
    <Fact>
    Public Sub TopLevelQueryWithAsClause_Evaluates()
        ScriptModeConformance.AssertRuns(
            Words & vbCrLf &
            "Dim q As System.Collections.IEnumerable = " & GroupingQuery & vbCrLf &
            "Return String.Join("","", q.Cast(Of Object)().Select(Function(x) x.k & "":"" & x.c))",
            ExpectedGroups, WithLinqOptions)
    End Sub

    ' ---- (3) the shape that used to throw InvalidCastException now evaluates ----

    ''' <summary>
    ''' The original issue-21 report: a top level <c>Dim q = &lt;query&gt;</c> followed by a member call. When the
    ''' field was <c>Object</c> the <c>q.Select(...)</c> was late bound and threw
    ''' <c>InvalidCastException</c> at run time. With the field inferred to the query type the call binds early and
    ''' the submission produces the expected groups - a positive assertion of a concrete value (issue 32 §四判据 6).
    ''' </summary>
    <Fact>
    Public Sub TopLevelQueryField_InferredSelect_Evaluates()
        ScriptModeConformance.AssertRuns(
            Words & vbCrLf &
            "Dim q = " & GroupingQuery & vbCrLf &
            "Return String.Join("","", q.Select(Function(x) x.k & "":"" & x.c))",
            ExpectedGroups, WithLinqOptions)
    End Sub

End Class
