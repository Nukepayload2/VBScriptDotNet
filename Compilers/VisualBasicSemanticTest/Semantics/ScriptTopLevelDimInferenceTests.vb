' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' Issue 32 (script-top-level-dim-type-inference): a top-level `Dim x = <expr>` in a script class
' carries the same Option-Infer-inferred static type an equivalent method-body local would, instead
' of Object. The acceptance is "match a VB local under the same options" - explicitly NOT C#'s
' unconditional CS0029 (that would change Option Strict Off's global semantics). The whole feature is
' gated to IsScriptClass, so ordinary VB fields (which must declare a type) never enter the path.

Imports System.Collections.Immutable
Imports Microsoft.CodeAnalysis.Test.Utilities
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Imports Roslyn.Test.Utilities

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests

    Public Class ScriptTopLevelDimInferenceTests
        Inherits BasicTestBase

        Private Shared Function ScriptOptions() As VisualBasicCompilationOptions
            Return New VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary).
                WithGlobalImports(GlobalImport.Parse("System", "Microsoft.VisualBasic"))
        End Function

        ''' <summary>Just the sorted diagnostic ids; positions differ across shapes and are not the point here.</summary>
        Private Shared Function Ids(compilation As VisualBasicCompilation) As String
            Return String.Join(" | ", compilation.GetDiagnostics().Select(Function(d) d.Id).OrderBy(Function(s) s))
        End Function

        ' Positive: `Dim x = 5` is inferred as Integer, so a String-only member access is a static
        ' error BC30456 (before the fix x was Object, x.Length compiled as a late-bound access and only
        ' failed at run time). This is the concrete proof that the field is statically Integer.
        <Fact>
        Public Sub T1_InferredInteger_MemberAccessIsStaticError()
            Dim c = CreateSubmission(
                "Dim x = 5" & vbCrLf &
                "Console.Write(x.Length)", options:=ScriptOptions(), parseOptions:=TestOptions.Script)

            Assert.Contains("BC30456", Ids(c))
        End Sub

        ' Positive: `Dim s = "hi"` infers String, so s.Length binds and is used - zero diagnostics.
        <Fact>
        Public Sub T2_InferredString_LengthBindsClean()
            Dim c = CreateSubmission(
                "Dim s = ""hi""" & vbCrLf &
                "Console.Write(s.Length)", options:=ScriptOptions(), parseOptions:=TestOptions.Script)

            Assert.Equal("", Ids(c))
        End Sub

        ' Counter-lock: Option Infer OFF keeps today's Object behavior. `Dim x = 5` is Object; under
        ' Option Strict Off the late-bound x.Length compiles with no static error (it would fail at run
        ' time), i.e. the inference change does not leak into the Infer-Off shape.
        <Fact>
        Public Sub T3_OptionInferOff_StaysObjectLateBound()
            Dim options = ScriptOptions().WithOptionInfer(False)
            Dim c = CreateSubmission(
                "Dim x = 5" & vbCrLf &
                "Console.Write(x.Length)", options:=options, parseOptions:=TestOptions.Script)

            Assert.DoesNotContain("BC30456", Ids(c))
        End Sub

        ' Counter-lock: an explicit As clause is untouched by inference (x is Integer by declaration,
        ' exactly as before the feature). Still BC30456 on x.Length, but for the declared reason.
        <Fact>
        Public Sub T4_ExplicitAsUnaffected()
            Dim c = CreateSubmission(
                "Dim x As Integer = 5" & vbCrLf &
                "Console.Write(x.Length)", options:=ScriptOptions(), parseOptions:=TestOptions.Script)

            Assert.Contains("BC30456", Ids(c))
        End Sub

        ' Q1 parity (issue §八): after inference, `Dim x = 5` then `x = "abc"` under Option Strict ON
        ' reports BC30512 (no implicit String -> Integer), byte-for-byte as a method-body local.
        <Fact>
        Public Sub T5_StrictOn_AssignIncompatible_ReportsBC30512()
            Dim options = ScriptOptions().WithOptionStrict(OptionStrict.On)
            Dim c = CreateSubmission(
                "Dim x = 5" & vbCrLf &
                "x = ""abc""", options:=options, parseOptions:=TestOptions.Script)

            Assert.Contains("BC30512", Ids(c))
        End Sub

        ' Q1 parity: under the default Option Strict OFF the same shape COMPILES (implicit narrowing
        ' allowed, fails only at run time) - it must NOT be a hard CS0029-style error. This is the
        ' boundary that keeps the script dialect from overriding Option Strict Off's global semantics.
        <Fact>
        Public Sub T6_StrictOff_AssignIncompatible_Compiles()
            Dim c = CreateSubmission(
                "Dim x = 5" & vbCrLf &
                "x = ""abc""", options:=ScriptOptions(), parseOptions:=TestOptions.Script)

            Assert.Equal("", Ids(c))
        End Sub

        ' Counter-lock (issue §七): Option Infer OFF + Option Strict ON reports BC30209 for the missing
        ' As clause - the pre-existing report point is preserved for the Infer-Off shape (the feature
        ' must not silence it while it wires up inference).
        <Fact>
        Public Sub T7_InferOff_StrictOn_ReportsBC30209()
            Dim options = ScriptOptions().WithOptionInfer(False).WithOptionStrict(OptionStrict.On)
            Dim c = CreateSubmission(
                "Dim x = 5", options:=options, parseOptions:=TestOptions.Script)

            Assert.Contains("BC30209", Ids(c))
        End Sub

    End Class

End Namespace
