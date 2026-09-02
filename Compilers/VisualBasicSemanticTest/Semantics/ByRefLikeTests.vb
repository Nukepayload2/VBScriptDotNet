' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports Microsoft.CodeAnalysis.Test.Utilities
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Roslyn.Test.Utilities

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests

    Public Class ByRefLikeTests
        Inherits BasicTestBase

        <Fact>
        Public Sub S1_SpanIsRefLike()
            Dim comp = CreateCompilation("", targetFramework:=TargetFramework.NetLatest)
            Dim span = comp.GetTypeByMetadataName("System.Span`1")
            Assert.True(span.IsRefLikeType)
        End Sub

        <Fact>
        Public Sub S1_RegularStructIsNotRefLike()
            Dim comp = CreateCompilation("Structure P
End Structure")
            Dim p = comp.GetTypeByMetadataName("P")
            Assert.False(p.IsRefLikeType)
        End Sub

        <Fact>
        Public Sub S16_TypedReferenceFieldStillRestricted()
            Dim source =
"Imports System
Class C
    Dim f As TypedReference
End Class"
            Dim comp = CreateCompilation(source)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedType1, "TypedReference").WithArguments("System.TypedReference"))
        End Sub

        <Fact>
        Public Sub S16_ArgIteratorBoxingStillRestricted()
            Dim source =
"Imports System
Class C
    Sub F()
        Dim obj As Object
        obj = New ArgIterator
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.Mscorlib40)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedConversion1, "New ArgIterator").WithArguments("System.ArgIterator"))
        End Sub

        <Fact>
        Public Sub S2_SpanSuppressesObsoleteError()
            Dim source =
"Imports System
Class C
    Sub F()
        Dim s As New Span(Of Integer)(1)
        s.Slice(0)
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub S3_BoxingToObjectRestrictedConversion()
            Dim source =
"Imports System
Class C
    Sub F()
        Dim o As Object
        o = New Span(Of Integer)(1)
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedConversion1, "New Span(Of Integer)(1)").WithArguments("System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub S4_NullableOfSpanRestrictedType()
            Dim source =
"Imports System
Class C
    Dim n As Nullable(Of Span(Of Integer))
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedType1, "n").WithArguments("System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub S5_SpanFieldRestrictedType()
            Dim source =
"Imports System
Class C
    Dim f As Span(Of Integer)
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedType1, "Span(Of Integer)").WithArguments("System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub S6_SpanArrayElementRestrictedType()
            Dim source =
"Imports System
Class C
    Dim a() As Span(Of Integer)
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedType1, "Span(Of Integer)").WithArguments("System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub S7_SpanReturnAllowed()
            ' Plain ref struct return types are legal (C# allows ref struct return by value).
            Dim source =
"Imports System
Class C
    Function F() As Span(Of Integer)
        Return Nothing
    End Function
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub S7_ArrayOfSpanReturnRestrictedType()
            Dim source =
"Imports System
Class C
    Function F() As Span(Of Integer)()
        Return Nothing
    End Function
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedType1, "Span(Of Integer)()").WithArguments("System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub S8_ByRefSpanParamRestrictedType()
            Dim source =
"Imports System
Class C
    Sub F(ByRef s As Span(Of Integer))
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedType1, "Span(Of Integer)").WithArguments("System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub S9_SpanGenericTypeArgRestrictedType()
            Dim source =
"Imports System
Class C(Of T)
End Class
Class D
    Dim c As C(Of Span(Of Integer))
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedType1, "c").WithArguments("System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub S10_SpanInQueryCannotLift()
            Dim source =
"Imports System
Imports System.Linq
Class C
    Function F() As Object
        Dim s As New Span(Of Integer)(1)
        Dim col = New Integer() {1, 2}
        Dim q = From i In col Where s.Length > 0 Select i
        Return q
    End Function
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyEmitDiagnostics(
                Diagnostic(ERRID.ERR_CannotLiftRestrictedTypeQuery, "s").WithArguments("System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub S11_SpanInLambdaCannotLift()
            Dim source =
"Imports System
Class C
    Sub F()
        Dim s As New Span(Of Integer)(1)
        Dim a As Action = Sub() s.Slice(0)
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyEmitDiagnostics(
                Diagnostic(ERRID.ERR_CannotLiftRestrictedTypeLambda, "s").WithArguments("System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub S12_SpanLocalInAsyncStateMachine()
            Dim source =
"Imports System
Imports System.Threading.Tasks
Class C
    Async Function F() As Task
        Dim s As Span(Of Integer) = New Span(Of Integer)(1)
        Dim x = s.Length
        Await Task.Delay(1)
    End Function
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_CannotLiftRestrictedTypeResumable1, "Span(Of Integer)").WithArguments("System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub S13_SpanMethodLocalIsUsable()
            Dim source =
"Imports System
Class C
    Sub F()
        Dim s As New Span(Of Integer)(1)
        Dim x = s.Length
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub S14_ByValSpanParamIsUsable()
            Dim source =
"Imports System
Class C
    Sub F(s As Span(Of Integer))
        Dim x = s.Length
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub S15_ConversionClassifierRejectsSpanToObject()
            Dim comp = CreateCompilation("", targetFramework:=TargetFramework.NetLatest)
            Dim spanDef = comp.GetTypeByMetadataName("System.Span`1")
            Dim span = DirectCast(spanDef.Construct(comp.GetSpecialType(CodeAnalysis.SpecialType.System_Int32)), TypeSymbol)
            Dim [object] = DirectCast(comp.GetSpecialType(CodeAnalysis.SpecialType.System_Object), TypeSymbol)
            Dim useSiteInfo = New CompoundUseSiteInfo(Of AssemblySymbol)(comp.Assembly)
            Dim conv = Conversions.ClassifyConversion(span, [object], useSiteInfo)
            Assert.True(Conversions.NoConversion(conv.Key))
        End Sub

        <Fact>
        Public Sub S17_SpanGetHashCodeIsObsolete()
            ' Span overrides GetHashCode and marks it obsolete ("will always throw"); this is NOT an
            ' inherited Object/ValueType member call, so no restricted-access error is reported.
            Dim source =
"Imports System
Class C
    Sub F()
        Dim s As New Span(Of Integer)(1)
        s.GetHashCode()
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.WRN_UseOfObsoleteSymbol2, "s.GetHashCode()").
                    WithArguments("Public Overrides Function GetHashCode() As Integer", "GetHashCode() on Span will always throw an exception."))
        End Sub

        <Fact>
        Public Sub S17_InheritedObjectMemberOnCustomRefStruct()
            ' Calling a member inherited from Object/ValueType on a ref-like receiver would box the
            ' receiver at runtime (Object.GetType also boxes internally), so BC31393
            ' (ERR_RestrictedAccess) is reported. C# reports CS0029 for the same scenario.
            Dim sourceA = "public ref struct R { }"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA,
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.Net70))
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim sourceB =
"Class C
    Sub F()
        Dim r As New R()
        r.GetHashCode()
    End Sub
End Class"
            Dim compB = CreateCompilation(sourceB, references:={refA})
            compB.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedAccess, "r.GetHashCode()").WithArguments("R"))
        End Sub

        <Fact>
        Public Sub S17_ObjectMembersOnCustomRefStructAllReported()
            ' GetHashCode/ToString/Equals/GetType all come from Object/ValueType (R does not
            ' override them) and each must be rejected on a ref-like receiver.
            Dim sourceA = "public ref struct R { }"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA,
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.Net70))
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim sourceB =
"Class C
    Sub F()
        Dim r As New R()
        r.GetHashCode()
        r.ToString()
        r.Equals(r)
        r.GetType()
    End Sub
End Class"
            Dim compB = CreateCompilation(sourceB, references:={refA})
            ' r.Equals(r): the argument r -> Object conversion is itself a restricted conversion
            ' (BC31394), reported on the argument. GetHashCode/ToString/GetType report the receiver
            ' boxing directly (BC31393).
            compB.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedAccess, "r.GetHashCode()").WithArguments("R"),
                Diagnostic(ERRID.ERR_RestrictedAccess, "r.ToString()").WithArguments("R"),
                Diagnostic(ERRID.ERR_RestrictedConversion1, "r").WithArguments("R"),
                Diagnostic(ERRID.ERR_RestrictedAccess, "r.GetType()").WithArguments("R"))
        End Sub

        <Fact>
        Public Sub S17_EqualsWithObjectCompatibleArgumentReportsRestrictedAccess()
            ' r.Equals(Nothing): the argument converts cleanly to Object, so the reported problem is
            ' the receiver boxing itself (BC31393).
            Dim sourceA = "public ref struct R { }"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA,
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.Net70))
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim sourceB =
"Class C
    Sub F()
        Dim r As New R()
        r.Equals(Nothing)
    End Sub
End Class"
            Dim compB = CreateCompilation(sourceB, references:={refA})
            compB.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedAccess, "r.Equals(Nothing)").WithArguments("R"))
        End Sub

        <Fact>
        Public Sub S17_OwnMembersOnCustomRefStructAreUsable()
            ' Members declared on the ref struct itself (property/field/method/indexer) do not
            ' require boxing and remain usable.
            Dim sourceA = "public ref struct R { public int P { get { return 42; } } public int M() { return 42; } }"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA,
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.Net70))
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim sourceB =
"Class C
    Sub F()
        Dim r As New R()
        Dim x = r.P
        Dim y = r.M()
    End Sub
End Class"
            Dim compB = CreateCompilation(sourceB, references:={refA})
            compB.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub S17_OverriddenObjectMemberOnCustomRefStructNotRestricted()
            ' A ref struct that overrides an Object member binds to its own override (ContainingType
            ' is the ref struct, not Object/ValueType), so no restricted-access error is reported.
            Dim sourceA = "public ref struct R { public override string ToString() => ""R""; }"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA,
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.Net70))
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim sourceB =
"Class C
    Sub F()
        Dim r As New R()
        Dim s = r.ToString()
    End Sub
End Class"
            Dim compB = CreateCompilation(sourceB, references:={refA})
            compB.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub S17_PlainStructObjectMemberIsUsable()
            ' A plain (non-ref-like) struct calling an inherited Object/ValueType member is legal:
            ' boxing a regular value type is allowed.
            Dim source =
"Structure P
End Structure
Class C
    Sub F()
        Dim p As New P()
        Dim h = p.GetHashCode()
        Dim s = p.ToString()
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub S17_RefLikeCapableTypeParameterObjectMemberRejected()
            ' Aligned with the C# CallObjectMember test: calling Object.ToString on a ref-like
            ' capable type parameter T is rejected (it would box T at runtime).
            Dim csHelper = CreateAllowsRefStructHelperReference()
            Dim source =
"Public Class Impl(Of T)
    Implements IThing(Of T)
    Public Sub Use(v As T) Implements IThing(Of T).Use
        Dim s = v.ToString()
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={csHelper})
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedAccess, "v.ToString()").WithArguments("T"))
        End Sub

        <Fact>
        Public Sub S18_SpanConstraintNotAllowed()
            ' A ref struct is a value type; VB only permits class/interface (or the Structure flag)
            ' as a type constraint, so a specific value-type constraint is rejected with BC32048.
            ' BC32061 (ERR_ConstraintIsRestrictedType1) applies to restricted CLASS constraints
            ' (Object/ValueType/Enum/Delegate/Array), not to ref structs.
            Dim source =
"Imports System
Class C(Of T As Span(Of Integer))
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_ConstNotClassInterfaceOrTypeParam1, "Span(Of Integer)").WithArguments("System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub S19_ExplicitCTypeAndDirectCastBoxing()
            Dim source =
"Imports System
Class C
    Sub F()
        Dim s As New Span(Of Integer)(1)
        Dim o1 As Object = CType(s, Object)
        Dim o2 As Object = DirectCast(s, Object)
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedConversion1, "s").WithArguments("System.Span(Of Integer)"),
                Diagnostic(ERRID.ERR_RestrictedConversion1, "s").WithArguments("System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub S20_TryCastSpanToInterface()
            ' TryCast restricted check only fires for Object/ValueType targets; an interface target
            ' falls through to the generic type-mismatch error (BC30311).
            Dim source =
"Imports System
Class C
    Sub F()
        Dim s As New Span(Of Integer)(1)
        Dim eq As IEquatable(Of Integer) = TryCast(s, IEquatable(Of Integer))
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_TypeMismatch2, "s").WithArguments("System.Span(Of Integer)", "System.IEquatable(Of Integer)"))
        End Sub

        <Fact>
        Public Sub S21_ImplicitInterfaceBoxingOfSpan()
            ' Interface boxing is rejected, but via the generic type-mismatch error (BC30311) rather
            ' than BC31394: the restricted-conversion check only fires for Object/ValueType targets.
            Dim source =
"Imports System
Class C
    Sub F()
        Dim s As New Span(Of Integer)(1)
        Dim i As IDisposable = s
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_TypeMismatch2, "s").WithArguments("System.Span(Of Integer)", "System.IDisposable"))
        End Sub

        <Fact>
        Public Sub S22_InterfaceUnboxingToSpan()
            ' Unboxing an interface reference to a ref struct is rejected via the generic
            ' type-mismatch error (BC30311); no restricted-specific code is reported for a
            ' restricted conversion destination.
            Dim source =
"Imports System
Class C
    Sub F()
        Dim i As IDisposable = Nothing
        Dim s As Span(Of Integer) = CType(i, Span(Of Integer))
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_TypeMismatch2, "i").WithArguments("System.IDisposable", "System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub S23_StringConcatenationOfSpan()
            ' The '&' operator cannot convert the restricted Span operand, so no applicable '&'
            ' overload exists for String + Span (BC30452), rather than a boxing BC31394.
            Dim source =
"Imports System
Class C
    Sub F()
        Dim s As New Span(Of Integer)(1)
        Dim str As String = ""a"" & s
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_BinaryOperands3, """a"" & s").WithArguments("&", "String", "System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub S24_ForEachOverReadOnlySpan()
            ' For Each over a ref struct enumerator works. Enumerator.Current is a ref readonly
            ' property (metadata modreq(In)) that the compiler imports and reads as an RValue at the
            ' read site (auto-deref). The For Each pattern's Current predicate
            ' (s_isReadablePropertyWithoutArguments) requires only a readable, parameterless property,
            ' so the loop binds, compiles, and enumerates 'a' and 'b'.
            Dim source =
"Imports System
Module Program
    Sub Main()
        Dim s As New ReadOnlySpan(Of Char)(""ab"".ToCharArray())
        For Each c As Char In s
            Console.WriteLine(c)
        Next
    End Sub
End Module"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, options:=TestOptions.ReleaseExe)
            comp.AssertNoDiagnostics()
            CompileAndVerify(comp, expectedOutput:=<![CDATA[
a
b]]>)
        End Sub

        <Fact>
        Public Sub S25_ArrayLiteralOfSpanRestrictedType()
            Dim source =
"Imports System
Class C
    Sub F()
        Dim s As New Span(Of Integer)(1)
        Dim x = {s, s}
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedType1, "= {s, s}").WithArguments("System.Span(Of Integer)"),
                Diagnostic(ERRID.ERR_RestrictedType1, "{s, s}").WithArguments("System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub S26_WithBlockAndNothingAreUsable()
            Dim source =
"Imports System
Class C
    Sub F()
        Dim s As New Span(Of Integer)(1)
        With s
            .Slice(1)
        End With
        Dim t As Span(Of Integer) = Nothing
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub S27_SpanInAnonymousTypeRestricted()
            Dim source =
"Imports System
Class C
    Sub F()
        Dim s As New Span(Of Integer)(1)
        Dim a = New With {.S = s}
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedType1, "s").WithArguments("System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub S28_AsyncSpanParameterCannotLift()
            ' A ByVal restricted parameter in an Async method is rejected at declaration with
            ' BC36932 (ERR_RestrictedResumableType1), not BC37052.
            Dim source =
"Imports System
Imports System.Threading.Tasks
Class C
    Async Function F(s As Span(Of Integer)) As Task
        Dim x = s.Length
        Await Task.Delay(1)
    End Function
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedResumableType1, "Span(Of Integer)").WithArguments("System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub S28_IteratorYieldSpanCannotLift()
            ' A ByVal restricted parameter in an Iterator method is rejected at declaration with
            ' BC36932 (ERR_RestrictedResumableType1), not BC37052.
            Dim source =
"Imports System
Class C
    Iterator Function F(s As Span(Of Integer)) As System.Collections.Generic.IEnumerable(Of Integer)
        Yield s.Length
    End Function
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedResumableType1, "Span(Of Integer)").WithArguments("System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub G1_FieldRestrictedType()
            Dim source =
"Imports System
Class C
    Dim f As Span(Of Integer)
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedType1, "Span(Of Integer)").WithArguments("System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub G2_ReturnByValueAllowed()
            Dim source =
"Imports System
Class C
    Function F() As Span(Of Integer)
        Return Nothing
    End Function
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub G3_ByRefParamRestrictedType()
            Dim source =
"Imports System
Class C
    Sub F(ByRef s As Span(Of Integer))
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedType1, "Span(Of Integer)").WithArguments("System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub G4_BoxingRestrictedConversion()
            Dim source =
"Imports System
Class C
    Sub F()
        Dim o As Object = New Span(Of Integer)(1)
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedConversion1, "New Span(Of Integer)(1)").WithArguments("System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub G5_PlainStructFieldNotRestricted()
            Dim source =
"Structure P
End Structure
Class C
    Dim f As P
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub G6_TypedReferenceFieldStillRestricted()
            Dim source =
"Imports System
Class C
    Dim t As TypedReference
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedType1, "TypedReference").WithArguments("System.TypedReference"))
        End Sub

        <Fact>
        Public Sub G7_MethodLocalAndByValUsable()
            Dim source =
"Imports System
Class C
    Sub F(s As Span(Of Integer))
        Dim x As New Span(Of Integer)(1)
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub G8_CTypeAndInterfaceBoxing()
            Dim source =
"Imports System
Class C
    Sub F()
        Dim o As Object = CType(New Span(Of Integer)(1), Object)
        Dim i As IDisposable = New Span(Of Integer)(1)
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedConversion1, "New Span(Of Integer)(1)").WithArguments("System.Span(Of Integer)"),
                Diagnostic(ERRID.ERR_TypeMismatch2, "New Span(Of Integer)(1)").WithArguments("System.Span(Of Integer)", "System.IDisposable"))
        End Sub

        <Fact>
        Public Sub G9_ValueTypeConstraintNotAllowed()
            Dim source =
"Imports System
Class C(Of T As Span(Of Integer))
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_ConstNotClassInterfaceOrTypeParam1, "Span(Of Integer)").WithArguments("System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub G10_SpanGetHashCodeObsolete()
            Dim source =
"Imports System
Class C
    Sub F()
        Dim s As New Span(Of Integer)(1)
        s.GetHashCode()
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.WRN_UseOfObsoleteSymbol2, "s.GetHashCode()").
                    WithArguments("Public Overrides Function GetHashCode() As Integer", "GetHashCode() on Span will always throw an exception."))
        End Sub

        <Fact>
        Public Sub G11_StringConcatenationOfSpan()
            Dim source =
"Imports System
Class C
    Sub F()
        Dim s As String = ""a"" & New Span(Of Integer)(1)
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_BinaryOperands3, """a"" & New Span(Of Integer)(1)").WithArguments("&", "String", "System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub G12_ArrayLiteralOfSpanRestrictedType()
            Dim source =
"Imports System
Class C
    Sub F()
        Dim a = {New Span(Of Integer)(1)}
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedType1, "= {New Span(Of Integer)(1)}").WithArguments("System.Span(Of Integer)"),
                Diagnostic(ERRID.ERR_RestrictedType1, "{New Span(Of Integer)(1)}").WithArguments("System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub G13_AnonymousTypeRestrictedAndNothingUsable()
            Dim source =
"Imports System
Class C
    Sub F()
        Dim a = New With {.S = New Span(Of Integer)(1)}
        Dim s As Span(Of Integer) = Nothing
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedType1, "New Span(Of Integer)(1)").WithArguments("System.Span(Of Integer)"))
        End Sub

        Private Function CreateDisposableAndDimHelperReference() As MetadataReference
            Dim csSource =
"using System;
public ref struct DisposableRefStruct : IDisposable { public void Dispose() { } }
public class NormalDisposable : IDisposable { public void Dispose() { } }
public interface IHasDefault<T> where T : allows ref struct { void M(T v); void Default(T v) { } }
"
            Return CreateCSharpCompilation(GetUniqueName(), csSource,
                parseOptions:=CSharp.CSharpParseOptions.Default.WithLanguageVersion(CSharp.LanguageVersion.Preview),
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.NetLatest)).EmitToImageReference()
        End Function

        Private Function CreateAllowsRefStructHelperReference() As MetadataReference
            Dim csSource =
"public interface IThing<T> where T : allows ref struct { void Use(T v); }
public interface INormal<T> { void Use(T v); }
public interface IC { void M<T>() where T : allows ref struct; }
public class C { public virtual void M<T2>(T2 x) where T2 : allows ref struct { } }
public class C2 : C { public override void M<T2>(T2 x) { } }
public interface IProp<T> where T : allows ref struct { T Prop { get; } }
public interface IBase<T> where T : allows ref struct { void BaseUse(T v); }
public interface IDerived<T> : IBase<T> where T : allows ref struct { }
"
            Return CreateCSharpCompilation(GetUniqueName(), csSource,
                parseOptions:=CSharp.CSharpParseOptions.Default.WithLanguageVersion(CSharp.LanguageVersion.Preview),
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.NetLatest)).EmitToImageReference()
        End Function

        <Fact>
        Public Sub H1_OverrideAllowsRefStructMethod()
            Dim csHelper = CreateAllowsRefStructHelperReference()
            Dim source =
"Public Class B
    Inherits C
    Public Overrides Sub M(Of T)(x As T)
        Dim o As Object = x
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={csHelper})
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_TypeMismatch2, "x").WithArguments("T", "Object"))
        End Sub

        <Fact>
        Public Sub H2_ImplementAllowsRefStructGenericMethod()
            Dim csHelper = CreateAllowsRefStructHelperReference()
            Dim source =
"Public Class B
    Implements IC
    Sub M(Of T) Implements IC.M
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={csHelper})
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub H3_ClassTypeParameterAllowsRefLikePassthrough()
            Dim csHelper = CreateAllowsRefStructHelperReference()
            Dim source =
"Public Class Impl(Of T)
    Implements IThing(Of T)
    Public Sub Use(v As T) Implements IThing(Of T).Use
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={csHelper})
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub H4_RefLikeCapableTypeParameterFieldRejected()
            Dim csHelper = CreateAllowsRefStructHelperReference()
            Dim source =
"Public Class Impl(Of T)
    Implements IThing(Of T)
    Private _f As T
    Public Sub Use(v As T) Implements IThing(Of T).Use
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={csHelper})
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedType1, "T").WithArguments("T"))
        End Sub

        <Fact>
        Public Sub H5_RefLikeCapableTypeParameterArrayRejected()
            Dim csHelper = CreateAllowsRefStructHelperReference()
            Dim source =
"Public Class Impl(Of T)
    Implements IThing(Of T)
    Public Sub Use(v As T) Implements IThing(Of T).Use
        Dim a() As T = Nothing
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={csHelper})
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedType1, "T").WithArguments("T"))
        End Sub

        <Fact>
        Public Sub H6_RefLikeCapableTypeParameterByRefRejected()
            Dim csHelper = CreateAllowsRefStructHelperReference()
            Dim source =
"Public Class Impl(Of T)
    Implements IThing(Of T)
    Public Sub Use(v As T) Implements IThing(Of T).Use
    End Sub
    Public Sub G(ByRef x As T)
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={csHelper})
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedType1, "T").WithArguments("T"))
        End Sub

        <Fact>
        Public Sub H7_RefLikeCapableTypeParameterBoxingRejected()
            Dim csHelper = CreateAllowsRefStructHelperReference()
            Dim source =
"Public Class Impl(Of T)
    Implements IThing(Of T)
    Public Sub Use(v As T) Implements IThing(Of T).Use
        Dim o As Object = v
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={csHelper})
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_TypeMismatch2, "v").WithArguments("T", "Object"))
        End Sub

        <Fact>
        Public Sub H8_RefLikeCapableTypeParameterAsyncCaptureRejected()
            Dim csHelper = CreateAllowsRefStructHelperReference()
            Dim source =
"Imports System
Imports System.Threading.Tasks
Public Class Impl(Of T)
    Implements IThing(Of T)
    Public Sub Use(v As T) Implements IThing(Of T).Use
    End Sub
    Async Function G() As Task
        Dim s As T = Nothing
        Console.WriteLine(s.ToString())
        Await Task.Delay(1)
    End Function
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={csHelper})
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_CannotLiftRestrictedTypeResumable1, "T").WithArguments("T"),
                Diagnostic(ERRID.ERR_RestrictedAccess, "s.ToString()").WithArguments("T"))
        End Sub

        <Fact>
        Public Sub H8b_RefLikeCapableTypeParameterAsyncParamRejectedAtDeclaration()
            ' A ref-like capable T parameter in an async/iterator method is rejected at declaration
            ' (BC36932) so the async/iterator capture walker never runs on it (the walker would
            ' otherwise crash on the parameter's null syntax entry).
            Dim csHelper = CreateAllowsRefStructHelperReference()

            Dim asyncSource =
"Imports System.Threading.Tasks
Public Class Impl(Of T)
    Implements IThing(Of T)
    Public Sub Use(v As T) Implements IThing(Of T).Use
    End Sub
    Async Function G(x As T) As Task
        Dim y = x.ToString()
        Await Task.Delay(1)
    End Function
End Class"
            Dim asyncComp = CreateCompilation(asyncSource, targetFramework:=TargetFramework.NetLatest, references:={csHelper})
            asyncComp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedResumableType1, "T").WithArguments("T"),
                Diagnostic(ERRID.ERR_RestrictedAccess, "x.ToString()").WithArguments("T"))

            Dim iteratorSource =
"Imports System.Collections.Generic
Public Class Impl(Of T)
    Implements IThing(Of T)
    Public Sub Use(v As T) Implements IThing(Of T).Use
    End Sub
    Iterator Function G(x As T) As IEnumerable(Of Integer)
        Yield x.ToString().Length
    End Function
End Class"
            Dim iteratorComp = CreateCompilation(iteratorSource, targetFramework:=TargetFramework.NetLatest, references:={csHelper})
            iteratorComp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedResumableType1, "T").WithArguments("T"),
                Diagnostic(ERRID.ERR_RestrictedAccess, "x.ToString()").WithArguments("T"))

            ' Emitting must not crash for either variant (regression guard for the walker assertion defect).
            Dim emitStream As New IO.MemoryStream()
            Assert.False(asyncComp.Emit(emitStream).Success)
            Dim emitStream2 As New IO.MemoryStream()
            Assert.False(iteratorComp.Emit(emitStream2).Success)
        End Sub

        <Fact>
        Public Sub H9_RefLikeCapableTypeParameterLegalUses()
            Dim csHelper = CreateAllowsRefStructHelperReference()
            Dim source =
"Public Class Impl(Of T)
    Implements IThing(Of T)
    Public Sub Use(v As T) Implements IThing(Of T).Use
        Dim local As T = v
    End Sub
    Public Function F() As T
        Return Nothing
    End Function
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={csHelper})
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub H10_ConstrainedCallThroughRefLikeCapableTypeParameter()
            Dim csHelper = CreateAllowsRefStructHelperReference()
            Dim source =
"Public Class Impl2(Of T As IThing(Of T))
    Implements IThing(Of T)
    Public Sub Use(v As T) Implements IThing(Of T).Use
    End Sub
    Public Sub G(v As T)
        v.Use(v)
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={csHelper}, options:=TestOptions.ReleaseDll)
            CompileAndVerify(comp, verify:=If(ExecutionConditionUtil.IsMonoOrCoreClr, Verification.Passes, Verification.Skipped)).
                VerifyIL("Impl2(Of T).G", <![CDATA[
{
  // Code size       15 (0xf)
  .maxstack  2
  IL_0000:  ldarga.s   V_1
  IL_0002:  ldarg.1
  IL_0003:  constrained. "T"
  IL_0009:  callvirt   "Sub IThing(Of T).Use(T)"
  IL_000e:  ret
}
]]>)
        End Sub

        <Fact>
        Public Sub H11_RefLikeTypeArgumentInstantiationAllowed()
            Dim csHelper = CreateAllowsRefStructHelperReference()
            Dim source =
"Imports System
Public Class Impl(Of T)
    Implements IThing(Of T)
    Public Sub Use(v As T) Implements IThing(Of T).Use
    End Sub
End Class
Public Class Test
    Sub F()
        Dim inst As New Impl(Of Span(Of Integer))
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={csHelper})
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub H12_UnionPropagationMultipleInterfaces()
            Dim csHelper = CreateAllowsRefStructHelperReference()
            Dim source =
"Public Class Impl(Of T)
    Implements IThing(Of T)
    Implements INormal(Of T)
    Public Sub Use(v As T) Implements IThing(Of T).Use, INormal(Of T).Use
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={csHelper})
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub H13_BaseClassInheritedInterfaceTypeParameter()
            ' Class-level T acquires allows-ref-like capability through an interface implemented
            ' by a base class (propagated via AllInterfaces).
            Dim csHelper = CreateAllowsRefStructHelperReference()
            Dim source =
"Imports System
Public Class BaseImpl(Of T)
    Implements IThing(Of T)
    Public Sub Use(v As T) Implements IThing(Of T).Use
    End Sub
End Class
Public Class DerivedImpl(Of T)
    Inherits BaseImpl(Of T)
End Class
Public Class Test
    Sub F()
        Dim inst As New DerivedImpl(Of Span(Of Integer))
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={csHelper})
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub H13b_BaseClassInheritedTypeParameterFieldRejected()
            Dim csHelper = CreateAllowsRefStructHelperReference()
            Dim source =
"Public Class BaseImpl(Of T)
    Implements IThing(Of T)
    Public Sub Use(v As T) Implements IThing(Of T).Use
    End Sub
End Class
Public Class DerivedImpl(Of T)
    Inherits BaseImpl(Of T)
    Private _f As T
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={csHelper})
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedType1, "T").WithArguments("T"))
        End Sub

        <Fact>
        Public Sub H14_NonGenericConstructedImplements()
            ' A non-generic class can implement a C# interface constructed with a concrete
            ' ref-like type argument (A1 allows ref-like args for allows-ref-like type params).
            Dim csHelper = CreateAllowsRefStructHelperReference()
            Dim source =
"Imports System
Public Class ImplSpan
    Implements IThing(Of Span(Of Integer))
    Public Sub Use(v As Span(Of Integer)) Implements IThing(Of Span(Of Integer)).Use
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={csHelper})
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub H15_PropertyOfRefLikeCapableTypeParameterRejected()
            ' A property whose type is a ref-like capable T is rejected (needs a backing field).
            Dim csHelper = CreateAllowsRefStructHelperReference()
            Dim source =
"Public Class Impl(Of T)
    Implements IThing(Of T)
    Public Sub Use(v As T) Implements IThing(Of T).Use
    End Sub
    Public Property Prop As T
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={csHelper})
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedType1, "T").WithArguments("T"))
        End Sub

        <Fact>
        Public Sub H16_MultiLevelOverrideChain()
            ' Method-level passthrough works through a multi-level override chain (C2 overrides C1).
            Dim csHelper = CreateAllowsRefStructHelperReference()
            Dim source =
"Public Class B
    Inherits C2
    Public Overrides Sub M(Of T)(x As T)
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={csHelper})
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub H17_ArrayParameterOfRefLikeCapableTypeParameterRejected()
            Dim csHelper = CreateAllowsRefStructHelperReference()
            Dim source =
"Public Class Impl(Of T)
    Implements IThing(Of T)
    Public Sub Use(v As T) Implements IThing(Of T).Use
    End Sub
    Public Sub G(a() As T)
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={csHelper})
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedType1, "T").WithArguments("T"))
        End Sub

        <Fact>
        Public Sub H18_LambdaCaptureOfRefLikeCapableTypeParameterRejected()
            ' The lambda captures the ref-like-capable T parameter v (referencing it in a local of
            ' type T, which is legal outside a capture), so only the capture is rejected.
            Dim csHelper = CreateAllowsRefStructHelperReference()
            Dim source =
"Public Class Impl(Of T)
    Implements IThing(Of T)
    Public Sub Use(v As T) Implements IThing(Of T).Use
        Dim f As System.Func(Of T) = Function() v
        Dim r = f()
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={csHelper})
            comp.VerifyEmitDiagnostics(
                Diagnostic(ERRID.ERR_CannotLiftRestrictedTypeLambda, "v").WithArguments("T"))
        End Sub

        <Fact>
        Public Sub H18b_LambdaParameterOfRefLikeCapableTypeParameterIsUsable()
            ' A lambda parameter (and ByVal return) of a ref-like capable T is legal, matching the
            ' method-level ByVal/return rules (H9). Only CAPTURE of a T variable is rejected (H18).
            Dim csHelper = CreateAllowsRefStructHelperReference()
            Dim source =
"Public Class Impl(Of T)
    Implements IThing(Of T)
    Public Sub Use(v As T) Implements IThing(Of T).Use
        Dim f As System.Func(Of T, T) = Function(x As T) x
        Dim r = f(v)
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={csHelper})
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub H19_AnonymousTypeOfRefLikeCapableTypeParameterRejected()
            Dim csHelper = CreateAllowsRefStructHelperReference()
            Dim source =
"Public Class Impl(Of T)
    Implements IThing(Of T)
    Public Sub Use(v As T) Implements IThing(Of T).Use
        Dim a = New With {.P = v}
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={csHelper})
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedType1, "v").WithArguments("T"))
        End Sub

        <Fact>
        Public Sub H20_AllowsRefLikeViaInheritedInterface()
            Dim csHelper = CreateAllowsRefStructHelperReference()
            Dim source =
"Public Class Impl(Of T)
    Implements IDerived(Of T)
    Public Sub BaseUse(v As T) Implements IDerived(Of T).BaseUse
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={csHelper})
            comp.AssertNoDiagnostics()
        End Sub

        ' --------------------------------------------------------------------------
        ' K-series: external coverage mirror (task #25).
        ' VBRefStructHelper BCX diagnostics + roslyn C# allows-ref-struct consumption tests,
        ' mapped to local VB compiler behavior. See tmp\vortex-logs\11-inventory-external-tests.md.
        ' --------------------------------------------------------------------------

        <Fact>
        Public Sub K1_UsingDisposableRefStructSingleRejected()
            ' BCX31394 Using series: VB lowers Using to an IDisposable conversion; a ref struct
            ' resource cannot be boxed, so Using reports the dispose-pattern error (BC36010).
            Dim refA = CreateDisposableAndDimHelperReference()
            Dim source =
"Imports System
Class C
    Sub F()
        Using x = New DisposableRefStruct()
            Console.WriteLine(1)
        End Using
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={refA})
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_UsingRequiresDisposePattern, "x = New DisposableRefStruct()").WithArguments("DisposableRefStruct"))
        End Sub

        <Fact>
        Public Sub K1b_UsingDisposableRefStructMultipleRejected()
            ' Multiple resources: only the ref struct resource is rejected; the normal disposable
            ' resource in the same Using is fine.
            Dim refA = CreateDisposableAndDimHelperReference()
            Dim source =
"Imports System
Class C
    Sub F()
        Using x = New DisposableRefStruct(), y = New NormalDisposable()
            Console.WriteLine(1)
        End Using
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={refA})
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_UsingRequiresDisposePattern, "x = New DisposableRefStruct()").WithArguments("DisposableRefStruct"))
        End Sub

        <Fact>
        Public Sub K1c_UsingDisposableRefStructExistingVariableRejected()
            Dim refA = CreateDisposableAndDimHelperReference()
            Dim source =
"Imports System
Class C
    Sub F()
        Dim existing As New DisposableRefStruct()
        Using existing
            Console.WriteLine(1)
        End Using
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={refA})
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_UsingRequiresDisposePattern, "existing").WithArguments("DisposableRefStruct"))
        End Sub

        <Fact>
        Public Sub K1d_UsingDisposableRefStructExplicitTypeRejected()
            Dim refA = CreateDisposableAndDimHelperReference()
            Dim source =
"Imports System
Class C
    Sub F()
        Using x As DisposableRefStruct = New DisposableRefStruct()
            Console.WriteLine(1)
        End Using
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={refA})
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_UsingRequiresDisposePattern, "x As DisposableRefStruct = New DisposableRefStruct()").WithArguments("DisposableRefStruct"))
        End Sub

        <Fact>
        Public Sub K1e_UsingNormalDisposableIsUsable()
            ' Negative for the Using series: a normal IDisposable resource works.
            Dim refA = CreateDisposableAndDimHelperReference()
            Dim source =
"Imports System
Class C
    Sub F()
        Using y = New NormalDisposable()
            Console.WriteLine(1)
        End Using
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={refA})
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub K2_StaticRefStructLocalRejected()
            ' BCX31396 variant: a Static local of a ref struct type (Static locals are field-like).
            Dim source =
"Imports System
Class C
    Sub F()
        Static s As New Span(Of Integer)(1)
        Console.WriteLine(1)
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedType1, "Span(Of Integer)").WithArguments("System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub K3_RefStructFieldAndPropertyInPlainStructRejected()
            ' BCX31396 variant: a non-ref-struct struct cannot contain Span fields or properties.
            Dim source =
"Imports System
Structure P
    Dim f As Span(Of Integer)
    Public Property Q As Span(Of Integer)
End Structure"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedType1, "Span(Of Integer)").WithArguments("System.Span(Of Integer)"),
                Diagnostic(ERRID.ERR_RestrictedType1, "Span(Of Integer)").WithArguments("System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub K4_OperatorReturningRefStructIsUsable()
            ' A conversion operator returning a ref struct by value is legal, matching the
            ' by-value return rule (S7/G2). The inventory lists this under BC31396, but this fork
            ' allows scalar by-value ref-struct returns (as C# does for conversion operators).
            Dim source =
"Imports System
Class C
    Public Shared Narrowing Operator CType(v As C) As Span(Of Integer)
        Return New Span(Of Integer)(1)
    End Operator
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub K5_ConstrainedClassConstraintsRejected()
            ' BC32061: Object/ValueType/Enum/Delegate/Array cannot be used as class constraints.
            ' (The bare Enum/Delegate keywords are not valid VB type keywords in a constraint, so
            ' they are tested via their System-qualified forms.)
            Dim source =
"Imports System
Class C1(Of T As Object)
End Class
Class C2(Of T As ValueType)
End Class
Class C3(Of T As System.Enum)
End Class
Class C4(Of T As System.Delegate)
End Class
Class C5(Of T As System.Array)
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_ConstraintIsRestrictedType1, "Object").WithArguments("Object"),
                Diagnostic(ERRID.ERR_ConstraintIsRestrictedType1, "ValueType").WithArguments("System.ValueType"),
                Diagnostic(ERRID.ERR_ConstraintIsRestrictedType1, "System.Enum").WithArguments("System.Enum"),
                Diagnostic(ERRID.ERR_ConstraintIsRestrictedType1, "System.Delegate").WithArguments("System.Delegate"),
                Diagnostic(ERRID.ERR_ConstraintIsRestrictedType1, "System.Array").WithArguments("System.Array"))
        End Sub

        <Fact>
        Public Sub K6_ChainedAsSpanLengthIsUsable()
            ' BCX37052 negative: Return x.AsSpan().Length creates no closure, so no error.
            Dim source =
"Imports System
Class C
    Function F(a() As Integer) As Integer
        Return a.AsSpan().Length
    End Function
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub K7_PlainLinqIsUsable()
            ' BCX36598 negative: a plain LINQ query over value types is fine.
            Dim source =
"Imports System
Imports System.Linq
Class C
    Function F() As Object
        Dim arr = New Integer() {1, 2}
        Dim q = From i In arr Where i > 0 Select i
        Return q
    End Function
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub K8_LambdaDeclaringLocalRefStructIsUsable()
            ' BCX36640 negative: a lambda declaring a local ref struct (and not capturing one from
            ' outside) is legal.
            Dim source =
"Imports System
Class C
    Sub F()
        Dim act As System.Action = Sub()
            Dim s As New Span(Of Integer)(1)
            Dim x = s.Length
        End Sub
        act()
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub K9_TypeOfIsOnRefStruct()
            ' C# is/as pattern equivalent: TypeOf ... Is against a ref struct is rejected because
            ' the operand would need to be a reference type (BC31416-family).
            Dim source =
"Imports System
Class C
    Sub F()
        Dim s As New Span(Of Integer)(1)
        Dim b = TypeOf s Is IDisposable
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_TypeOfRequiresReferenceType1, "s").WithArguments("System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub K10_IsNothingOnRefLikeCapableTypeParameter()
            ' C# `x == null` box sequence equivalent: `v Is Nothing` on a ref-like capable T boxes
            ' T to Object, reported via the generic type-mismatch error (BC30311).
            Dim csHelper = CreateAllowsRefStructHelperReference()
            Dim source =
"Public Class Impl(Of T)
    Implements IThing(Of T)
    Public Sub Use(v As T) Implements IThing(Of T).Use
        Dim b = v Is Nothing
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={csHelper})
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_TypeMismatch2, "v").WithArguments("T", "Object"))
        End Sub

        <Fact>
        Public Sub K11_IteratorReturningEnumerableOfRefStructIsUsable()
            ' Net10.0's IEnumerable(Of T) declares `allows ref struct` (verified: type parameter
            ' AllowsRefLikeType=True), so IEnumerable(Of Span) is a legal iterator return type and
            ' NO diagnostic fires. The C# CS9266 "iterator element of ref struct" error scenario is
            ' not reachable on this TFM; see K11b for the same shape on an older TFM.
            Dim source =
"Imports System
Imports System.Collections.Generic
Class C
    Iterator Function F() As IEnumerable(Of Span(Of Integer))
        Yield New Span(Of Integer)(1)
    End Function
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub K11b_IteratorReturningEnumerableOfRefStructOldFrameworkRejected()
            ' C# CS9266 equivalent on net70 (before `allows ref struct` was added to BCL generic
            ' interfaces): IEnumerable(Of Span) carries Span as a restricted type argument (BC31396).
            Dim source =
"Imports System
Imports System.Collections.Generic
Class C
    Iterator Function F() As IEnumerable(Of Span(Of Integer))
        Yield New Span(Of Integer)(1)
    End Function
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.Net70)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedType1, "IEnumerable(Of Span(Of Integer))").WithArguments("System.Span(Of Integer)"))
        End Sub

        <Fact>
        Public Sub K12_NullableOfUnconstrainedTypeParameterRejected()
            ' Nullable(Of T) requires T to be constrained to Structure. BC32105 fires for ANY
            ' unconstrained type parameter T, so this does NOT isolate the allows-ref-struct aspect;
            ' it is included to document that `T?` is rejected at all (the C# CS9244/`T?` family).
            Dim csHelper = CreateAllowsRefStructHelperReference()
            Dim source =
"Imports System
Public Class Impl(Of T)
    Implements IThing(Of T)
    Public Sub Use(v As T) Implements IThing(Of T).Use
    End Sub
    Public Sub G()
        Dim n As Nullable(Of T)
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={csHelper})
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.WRN_UnusedLocal, "n").WithArguments("n"),
                Diagnostic(ERRID.ERR_BadTypeArgForStructConstraintNull, "T").WithArguments("T"))
        End Sub

        <Fact>
        Public Sub K13_DefaultInterfaceMemberOnRefLikeCapableTypeParameter()
            ' C# CS9246 (DIM on allows T) equivalent. This fork's VB has no default-interface-member
            ' support: a source class implementing an interface must implement every interface
            ' member (BC30149), and bare T has no Default member (BC30456). Documented as a VB
            ' limitation (the Edge-D diagnostic is plan-deferred).
            Dim refA = CreateDisposableAndDimHelperReference()
            Dim source =
"Public Class Impl(Of T)
    Implements IHasDefault(Of T)
    Public Sub M(v As T) Implements IHasDefault(Of T).M
    End Sub
    Public Sub G(v As T)
        v.Default(v)
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={refA})
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_UnimplementedMember3, "IHasDefault(Of T)").WithArguments("Class", "Impl", "Sub [Default](v As T)", "IHasDefault(Of T)"),
                Diagnostic(ERRID.ERR_NameNotMember2, "v.Default").WithArguments("Default", "T"))
        End Sub

        <Fact>
        Public Sub K13b_DefaultInterfaceMemberOnNormalInterface()
            ' Confirms the DIM limitation is general VB behavior (not allows-ref-struct specific):
            ' a source class must implement every interface member, including default interface
            ' methods, so BC30149 fires even for a normal DIM interface.
            Dim csSource =
"using System;
public interface INormalDim { void M(); void D() { } }"
            Dim csComp = CreateCSharpCompilation(GetUniqueName(), csSource,
                parseOptions:=CSharp.CSharpParseOptions.Default.WithLanguageVersion(CSharp.LanguageVersion.Preview),
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.NetLatest)).EmitToImageReference()
            Dim source =
"Public Class Impl
    Implements INormalDim
    Public Sub M() Implements INormalDim.M
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, references:={csComp})
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_UnimplementedMember3, "INormalDim").WithArguments("Class", "Impl", "Sub D()", "INormalDim"))
        End Sub

        <Fact>
        Public Sub K15_StringInterpolationOfRefStructRejected()
            ' BCX31393 inventory lists $"..." but locally the interpolation hole cannot be lowered:
            ' String.Format(ReadOnlySpan(Of Object)) cannot take a Span hole, so the interpolated
            ' string factory reports an error (BC32017 + factory error) on the whole string.
            Dim source =
"Imports System
Class C
    Sub F()
        Dim s As New Span(Of Integer)(1)
        Dim str = $""Span: {s}""
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            ' The String.Format overload candidate message embeds a multiline signature, so assert
            ' error codes directly rather than message args.
            Dim diags = comp.GetDiagnostics()
            Assert.Equal(2, diags.Length)
            Assert.Equal(CInt(ERRID.ERR_NoCallableOverloadCandidates2), diags(0).Code)
            Assert.Equal(CInt(ERRID.ERR_InterpolatedStringFactoryError), diags(1).Code)
        End Sub

        <Fact>
        Public Sub K14_RefStructOwnMembersAreUsable()
            ' BCX31393 negative: span.Length/Slice/indexer are the ref struct's own members and
            ' do not require boxing.
            Dim source =
"Imports System
Class C
    Sub F()
        Dim s As New Span(Of Integer)(New Integer() {1, 2, 3})
        Dim a = s.Length
        Dim b = s.Slice(1)
        Dim c = s(0)
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub K16_AddressOfObjectMemberOnRefStructRejected()
            ' D1 fix: AddressOf on a ref-like receiver targeting a member inherited from
            ' Object/ValueType would box the receiver at delegate-creation time (emits `box` on a
            ' ref-like type -> InvalidProgramException). BC31393 is reported.
            Dim sourceA = "public ref struct R { }"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA,
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.Net70))
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim source =
"Class C
    Sub F()
        Dim r As New R()
        Dim h As System.Func(Of Integer) = AddressOf r.GetHashCode
        Dim s As System.Func(Of String) = AddressOf r.ToString
    End Sub
End Class"
            Dim comp = CreateCompilation(source, references:={refA})
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedAccess, "AddressOf r.GetHashCode").WithArguments("R"),
                Diagnostic(ERRID.ERR_RestrictedAccess, "AddressOf r.ToString").WithArguments("R"))
        End Sub

        <Fact>
        Public Sub K16b_AddressOfOwnInstanceMemberOnRefStructRejected()
            ' E1 fix: AddressOf on a ref-like receiver targeting ANY instance member (including the
            ' ref struct's OWN methods) boxes the receiver at delegate-creation time (a delegate must
            ' capture the receiver as an object reference -> `box R` -> InvalidProgramException at
            ' runtime). BC31393 is reported.
            Dim sourceA = "public ref struct R { public int M() { return 42; } }"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA,
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.Net70))
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim source =
"Class C
    Sub F()
        Dim r As New R()
        Dim d As System.Func(Of Integer) = AddressOf r.M
        d()
    End Sub
End Class"
            Dim comp = CreateCompilation(source, references:={refA})
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedAccess, "AddressOf r.M").WithArguments("R"))
        End Sub

        <Fact>
        Public Sub K16c_AddressOfObjectMemberOnPlainStructIsUsable()
            ' Negative for the AddressOf path: a plain (non-ref-like) struct boxing to call an
            ' inherited Object member is legal.
            Dim source =
"Structure P
End Structure
Class C
    Sub F()
        Dim p As New P()
        Dim s As System.Func(Of String) = AddressOf p.ToString
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub K16d_AddressOfSharedMemberOnRefStructIsUsable()
            ' Negative for the E1 rule: a Shared member captures no receiver, so AddressOf does not
            ' box and is legal even on a ref struct.
            Dim sourceA = "public ref struct R { public static int SharedM() { return 42; } }"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA,
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.Net70))
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim source =
"Class C
    Sub F()
        Dim d As System.Func(Of Integer) = AddressOf R.SharedM
    End Sub
End Class"
            Dim comp = CreateCompilation(source, references:={refA})
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub K16e_AddressOfInstanceMemberOnPlainStructIsUsable()
            ' Negative for the E1 rule: a plain (non-ref-like) struct can box, so AddressOf on its
            ' instance member is legal.
            Dim source =
"Structure P
    Public Function M() As Integer
        Return 42
    End Function
End Structure
Class C
    Sub F()
        Dim p As New P()
        Dim d As System.Func(Of Integer) = AddressOf p.M
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub K16f_AddressOfInstanceMemberOnReferenceTypeIsUsable()
            ' Negative for the E1 rule: a reference-type receiver is never boxed, so AddressOf on an
            ' instance member is legal.
            Dim source =
"Class O
    Public Function M() As Integer
        Return 42
    End Function
End Class
Class C
    Sub F()
        Dim o As New O()
        Dim d As System.Func(Of Integer) = AddressOf o.M
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub K16g_NewDelegateWithAddressOfOwnMemberOnRefStructRejected()
            ' Explicit delegate construction `New Func(Of Integer)(AddressOf r.M)` routes through the
            ' same conversion path and is rejected (BC31393) for a ref struct own instance member.
            Dim sourceA = "public ref struct R { public int M() { return 42; } }"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA,
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.Net70))
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim source =
"Class C
    Sub F()
        Dim r As New R()
        Dim d As New System.Func(Of Integer)(AddressOf r.M)
    End Sub
End Class"
            Dim comp = CreateCompilation(source, references:={refA})
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RestrictedAccess, "AddressOf r.M").WithArguments("R"))
        End Sub

    End Class

End Namespace
