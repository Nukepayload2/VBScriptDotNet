' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Linq
Imports Microsoft.CodeAnalysis.Test.Utilities
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Imports Roslyn.Test.Utilities

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests

    ''' <summary>
    ''' Row E / issue 29 truth table (V-A): a BARE (unqualified, no explicit receiver) reference to an inherited
    ''' <c>System.Object</c> member in the three script-shaped containers. The point of the table is that <c>VB</c>
    ''' reaches the SAME observable outcome as C# through a DIFFERENT mechanism, so the <c>ImplicitNamedTypeSymbol.vb:59</c>
    ''' predicate must NOT be tightened (V-B) - see the class-end note.
    ''' <para>
    ''' Measured (this session, current product code, no product change made by these tests):
    ''' </para>
    ''' <list type="table">
    ''' <description>container x position x bare name (diagnostic id sets, from the compiler):</description>
    ''' <item><term>Submission (top / instance / shared), <c>ToString()</c> / <c>GetHashCode()</c></term>
    ''' <description>BC30451 - the implicit submission class reports <c>BaseType = Nothing</c>
    ''' (<c>ImplicitNamedTypeSymbol.vb:59</c>, <c>TypeKind.Submission</c>) so the bare-name lookup never reaches
    ''' <c>Object</c>.</description></item>
    ''' <item><term>Non-submission script class (<c>TypeKind.Class</c>, base <c>Object</c>), <c>ToString()</c> /
    ''' <c>GetHashCode()</c> at top / instance</term><description>no errors - the member resolves via the
    ''' <c>Object</c> base.</description></item>
    ''' <item><term>same, called from a <c>Shared</c> body</term><description>BC30369 - resolved to the
    ''' <c>Object</c> INSTANCE member, then rejected for lack of an instance; exactly what a normal class does, which is
    ''' the positive control that the non-submission script class is behaving like a class here.</description></item>
    ''' <item><term>Script-declared normal class</term><description>no errors at instance, BC30369 at
    ''' <c>Shared</c> - unchanged, the "script class special vs ordinary class ordinary" boundary.</description></item>
    ''' </list>
    ''' <para>
    ''' <c>GetType()</c> is NOT a probe for <c>Object.GetType</c>: bare <c>GetType()</c> binds to the <c>GetType</c>
    ''' <em>operator</em> and reports BC30182 ("a type was expected") in every container, so it is pinned once as a
    ''' documented confound rather than as an <c>Object</c>-member result.
    ''' </para>
    ''' <para>
    ''' V-B decision (why <c>:59</c> stays): C# also does not resolve bare <c>Object</c> members in a script class at
    ''' the symbol level (<c>ImplicitClassTests.cs:63/:76</c>), but it recovers <c>Object</c> for the NON-submission
    ''' script class through a separate binding-time walk (<c>TypeSymbolExtensions.cs:226-232</c>) - so C# resolves
    ''' <c>ToString()</c> in the non-submission script class the same way VB does here, VB just carries an
    ''' <c>Object</c> base instead of a walk. Tightening VB's predicate to <c>IsScriptClass</c> would strip that base
    ''' from the non-submission script class WITHOUT porting the walk, making VB strictly LESS permissive than C# - an
    ''' over-divergence. ⇒ no product change; these cells pin the current, C#-equivalent-observable behavior. The C#
    ''' reading is inherited from the parity queue (◇, not re-run this session); the VB column is measured now.
    ''' </para>
    ''' </summary>
    Public Class ScriptBareObjectMemberTruthTableTests
        Inherits BasicTestBase

        ''' <summary>Just the error ids, sorted - the truth table is about WHICH diagnostic, not its text or span.</summary>
        Private Shared Function ErrorIds(compilation As VisualBasicCompilation) As String()
            Return compilation.GetDiagnostics().
                Where(Function(d) d.Severity = DiagnosticSeverity.Error).
                Select(Function(d) d.Id).
                OrderBy(Function(id) id, StringComparer.Ordinal).
                ToArray()
        End Function

        Private Shared Function NonSubmissionScriptClass(source As String) As VisualBasicCompilation
            Dim tree = SyntaxFactory.ParseSyntaxTree(source, options:=New VisualBasicParseOptions(kind:=SourceCodeKind.Script))
            Return CreateCompilationWithMscorlib461AndVBRuntime({tree}, options:=TestOptions.ReleaseExe.WithScriptClassName("Script"))
        End Function

#Region "Submission class: bare Object members are NOT reachable (BaseType = Nothing)"

        <Fact>
        Public Sub Submission_TopLevel_BareToString_NotFound()
            Dim c = CreateSubmission("ToString()", parseOptions:=TestOptions.Script)
            Assert.Equal({"BC30451"}, ErrorIds(c))
        End Sub

        <Fact>
        Public Sub Submission_InstanceMember_BareToString_NotFound()
            Dim c = CreateSubmission("Sub M()" & vbLf & "    ToString()" & vbLf & "End Sub", parseOptions:=TestOptions.Script)
            Assert.Equal({"BC30451"}, ErrorIds(c))
        End Sub

        <Fact>
        Public Sub Submission_SharedMember_BareGetHashCode_NotFound()
            Dim c = CreateSubmission("Shared Sub M()" & vbLf & "    GetHashCode()" & vbLf & "End Sub", parseOptions:=TestOptions.Script)
            Assert.Equal({"BC30451"}, ErrorIds(c))
        End Sub

#End Region

#Region "Non-submission script class: bare Object members ARE reachable (base = Object) - like a class"

        <Fact>
        Public Sub NonSubmissionScriptClass_TopLevel_BareToString_Resolves()
            Dim c = NonSubmissionScriptClass("ToString()")
            Assert.Empty(ErrorIds(c))
        End Sub

        <Fact>
        Public Sub NonSubmissionScriptClass_InstanceMember_BareToString_Resolves()
            Dim c = NonSubmissionScriptClass("Sub M()" & vbLf & "    ToString()" & vbLf & "End Sub")
            Assert.Empty(ErrorIds(c))
        End Sub

        ''' <summary>
        ''' The positive control that the non-submission script class is treated like a class: a bare
        ''' <c>ToString()</c> from a <c>Shared</c> body resolves to the <c>Object</c> INSTANCE member and is then
        ''' rejected for lack of an instance (BC30369), never "not found" (BC30451) as in the submission class.
        ''' </summary>
        <Fact>
        Public Sub NonSubmissionScriptClass_SharedMember_BareToString_ReportsBadNameNotNotFound()
            Dim c = NonSubmissionScriptClass("Shared Sub M()" & vbLf & "    ToString()" & vbLf & "End Sub")
            Assert.Equal({"BC30369"}, ErrorIds(c))
        End Sub

#End Region

#Region "Script-declared normal class: unchanged (the boundary must not move)"

        <Fact>
        Public Sub ScriptDeclaredNormalClass_InstanceMember_BareToString_Resolves()
            Dim c = CreateSubmission(
                "Class Holder" & vbLf &
                "    Sub M()" & vbLf &
                "        ToString()" & vbLf &
                "    End Sub" & vbLf &
                "End Class", parseOptions:=TestOptions.Script)
            Assert.Empty(ErrorIds(c))
        End Sub

        <Fact>
        Public Sub ScriptDeclaredNormalClass_SharedMember_BareToString_ReportsBadName()
            Dim c = CreateSubmission(
                "Class Holder" & vbLf &
                "    Shared Sub M()" & vbLf &
                "        ToString()" & vbLf &
                "    End Sub" & vbLf &
                "End Class", parseOptions:=TestOptions.Script)
            Assert.Equal({"BC30369"}, ErrorIds(c))
        End Sub

#End Region

#Region "GetType() is the operator keyword, not Object.GetType - a confound in every container"

        <Fact>
        Public Sub GetTypeBare_IsOperatorNotObjectMember_InAllContainers()
            Dim submission = CreateSubmission("GetType()", parseOptions:=TestOptions.Script)
            Dim scriptClass = NonSubmissionScriptClass("GetType()")
            Assert.Equal({"BC30182"}, ErrorIds(submission))
            Assert.Equal({"BC30182"}, ErrorIds(scriptClass))
        End Sub

#End Region
    End Class
End Namespace
