' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports Microsoft.CodeAnalysis.CSharp
Imports Microsoft.CodeAnalysis.Test.Utilities
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Imports Roslyn.Test.Utilities

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests

    ''' <summary>
    ''' Part B consumption of C# 11 interface shared members (SAIM) through a type parameter,
    ''' per InternalDevDocs\tasks\consume-csharp-extension-and-interface-shared\test-plan.md §3.2 (S16-S28) and §6 (G3/G4).
    ''' The C# test library is built in-memory via CreateCSharpCompilation (no external DLL, no side effects).
    ''' </summary>
    <CompilerTrait(CompilerFeature.DefaultInterfaceImplementation)>
    Public Class InterfaceSharedMemberConsumptionTests
        Inherits BasicTestBase

        Private Const _supportingFramework As TargetFramework = TargetFramework.NetLatest

        Private Function GetCSharpCompilation(csSource As String) As CSharpCompilation
            Return CreateCSharpCompilation(
                csSource,
                parseOptions:=CSharpParseOptions.Default.WithLanguageVersion(CSharp.LanguageVersion.Preview),
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(_supportingFramework))
        End Function

        ' C# base library for the main `T.Zero`/`T.Add` generic algorithm (test-plan §3 base).
        Private Const _baseCsLibrary As String = "
namespace ExternLib
{
    public interface IHasZero<T> where T : IHasZero<T>
    {
        static abstract T Zero { get; }
        static abstract T Add(T a, T b);
    }
    public struct MyNum : IHasZero<MyNum>
    {
        public int Value;
        public MyNum(int v) { Value = v; }
        public static MyNum Zero => new MyNum(0);
        public static MyNum Add(MyNum a, MyNum b) => new MyNum(a.Value + b.Value);
    }
}
"

        <Fact>
        Public Sub S16_TZero_GenericAlgorithm_NoDiagnostics()
            Dim csRef = GetCSharpCompilation(_baseCsLibrary).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib

Module M
    Function VBSum(Of T As IHasZero(Of T))(items() As T) As T
        Dim result As T = T.Zero
        For Each item In items
            result = T.Add(result, item)
        Next
        Return result
    End Function

    Sub Main()
        Dim r As Integer = VBSum(Of MyNum)({New MyNum(10), New MyNum(20)}).Value
    End Sub
End Module
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.VerifyDiagnostics()
        End Sub

        <Fact>
        Public Sub S17_TAdd_BinaryMethodViaTypeParameter_NoDiagnostics()
            Dim csRef = GetCSharpCompilation(_baseCsLibrary).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib

Class C(Of T As IHasZero(Of T))
    Shared Function F(a As T, b As T) As T
        Return T.Add(a, b)
    End Function
End Class
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.VerifyDiagnostics()
        End Sub

        <Fact>
        Public Sub S18_TMember_Unconstrained_BC32098()
            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Class C(Of T)
    Public Sub F()
        Dim x As Integer = T.goo
    End Sub
End Class
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, targetFramework:=_supportingFramework)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_TypeParamQualifierDisallowed, "T.goo"))
        End Sub

        <Fact>
        Public Sub S19_TMember_ClassConstraint_BC32098()
            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Class C1(Of T As DataHolder)
    Public Sub F()
        T.Var1 = 4
    End Sub
End Class

Class DataHolder
    Public Var1 As Integer
End Class
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, targetFramework:=_supportingFramework)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_TypeParamQualifierDisallowed, "T.Var1"))
        End Sub

        <Fact>
        Public Sub S20_TMember_EffectiveInterfaceSet_NoDiagnostics()
            Dim csSource =
"
namespace ExternLib
{
    public interface I2 { static abstract int M(); }
    public interface I1 : I2 { }
}
"
            Dim csRef = GetCSharpCompilation(csSource).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib

Class C(Of T As I1)
    Shared Function F() As Integer
        Return T.M()
    End Function
End Class
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.VerifyDiagnostics()
        End Sub

        <Fact>
        Public Sub S21_I1M01_InterfaceNameDirectStaticAbstract_BC37314()
            Dim csSource =
"
namespace ExternLib
{
    public interface I1 { static abstract void M01(); }
}
"
            Dim csRef = GetCSharpCompilation(csSource).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib

Class Test
    Shared Sub F()
        I1.M01()
    End Sub
End Class
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_BadAbstractStaticMemberAccess, "I1.M01()"))
        End Sub

        <Fact>
        Public Sub S22_StaticVirtualViaInterfaceName_BC37314_Boundary()
            ' Test-plan §3.2 S22 expected "no diagnostics" for static virtual via interface name,
            ' but the actual VB behavior reports BC37314 (a shared abstract OR virtual interface member
            ' cannot be accessed via the interface name). This is the pre-existing interface-name path
            ' (not the F10 type-parameter path) and is out of scope for the F10 minimal prototype.
            ' Recorded as a boundary / test-plan discrepancy.
            Dim csSource =
"
namespace ExternLib
{
    public interface I1 { static virtual int Foo() => 42; }
}
"
            Dim csRef = GetCSharpCompilation(csSource).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib

Class Test
    Shared Function F() As Integer
        Return I1.Foo()
    End Function
End Class
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_BadAbstractStaticMemberAccess, "I1.Foo()"))
        End Sub

        <Fact>
        Public Sub S24_NameOfTypeParameterSharedMember_NoDiagnostics()
            Dim csRef = GetCSharpCompilation(_baseCsLibrary).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib

Class C(Of T As IHasZero(Of T))
    Shared Sub S()
        Dim s As String = NameOf(T.Zero)
    End Sub
End Class
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.VerifyDiagnostics()
        End Sub

        <Fact>
        Public Sub S25_AddressOfTypeParameterSharedMethod_NoDiagnostics()
            ' F19: delegate creation from a SAIM through a constrained type parameter
            ' (AddressOf T.Add) now binds cleanly and emits 'constrained.' + 'ldftn'.
            Dim csRef = GetCSharpCompilation(_baseCsLibrary).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib
Imports System

Class C(Of T As IHasZero(Of T))
    Shared _d As Func(Of T, T, T) = AddressOf T.Add
End Class
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.VerifyDiagnostics()
        End Sub

        <Fact>
        Public Sub S25b_AddressOfTypeParameterSharedMethod_RuntimeValue()
            ' Runtime: the delegate created via AddressOf T.Add must dispatch to the actual
            ' type argument's implementation and return the correct value.
            Dim csRef = GetCSharpCompilation(_baseCsLibrary).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib
Imports System

Class C(Of T As IHasZero(Of T))
    Shared _d As Func(Of T, T, T) = AddressOf T.Add

    Shared Function Sum(items() As T) As T
        Dim result As T = T.Zero
        For Each item In items
            result = _d(result, item)
        Next
        Return result
    End Function
End Class

Module M
    Sub Main()
        Dim r As MyNum = C(Of MyNum).Sum({New MyNum(10), New MyNum(20)})
        Console.WriteLine(r.Value)
    End Sub
End Module
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework, options:=TestOptions.ReleaseExe)
            comp.VerifyDiagnostics()
            ' SAIM emit patterns (constrained.+call / constrained.+ldftn) are rejected by Roslyn's
            ' internal ILVerify; the .NET runtime executes them (mirror of the C# SAIM tests).
            CompileAndVerify(comp, expectedOutput:="30", verify:=Verification.Skipped)
        End Sub

        <Fact>
        Public Sub S29_ExpressionTreeContainsAbstractStaticMember_BC37340()
            ' F21 (legacy item 1): SAIM inside an expression tree (Function() AddressOf T.M01 /
            ' Sub() T.P = 1) reports BC37340, mirroring C# CS8927
            ' (ERR_ExpressionTreeContainsAbstractStaticMemberAccess). Non-expression-tree
            ' AddressOf T.M and T.P = v remain diagnostic-free (F19 behavior).
            Dim csSource =
"
namespace ExternLib
{
    public interface I1
    {
        static abstract void M01();
        static abstract int P { get; set; }
    }
    public struct MyNum : I1
    {
        private static int _p;
        public static void M01() { }
        public static int P { get => _p; set => _p = value; }
    }
}
"
            Dim csRef = GetCSharpCompilation(csSource).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib
Imports System
Imports System.Linq.Expressions

Class C(Of T As I1)
    Shared _d As Action
    Shared _i As Integer

    Shared Sub MT1()
        _d = AddressOf T.M01
        _i = T.P
        T.P = 1
        T.P += 1
    End Sub

    Shared Sub MT2()
        Dim e1 = CType(Function() AddressOf T.M01, Expression(Of Func(Of Action)))
        Dim e2 = CType(Sub() T.P = 1, Expression(Of Action))
        Dim e3 = CType(Sub() T.P.ToString(), Expression(Of Action))
    End Sub
End Class
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.AssertTheseDiagnostics(
<errors>
BC37340: An expression tree may not contain an access of static virtual or abstract interface member.
        Dim e1 = CType(Function() AddressOf T.M01, Expression(Of Func(Of Action)))
                                  ~~~~~~~~~~~~~~~
BC37340: An expression tree may not contain an access of static virtual or abstract interface member.
        Dim e2 = CType(Sub() T.P = 1, Expression(Of Action))
                             ~~~
BC37340: An expression tree may not contain an access of static virtual or abstract interface member.
        Dim e3 = CType(Sub() T.P.ToString(), Expression(Of Action))
                             ~~~
</errors>
            )
        End Sub

        ' C# library with a static abstract interface operator (test-plan §3.2 S26).
        Private Const _operatorCsLibrary As String = "
namespace ExternLib
{
    public interface IV<T> where T : IV<T>
    {
        static abstract T operator +(T left, T right);
    }
    public struct MyVec : IV<MyVec>
    {
        public int Value;
        public MyVec(int v) { Value = v; }
        public static MyVec operator +(MyVec a, MyVec b) => new MyVec(a.Value + b.Value);
    }
}
"

        <Fact>
        Public Sub S26_TypeParameterSharedOperator_NoDiagnostics()
            ' a + b where a and b are a type parameter T constrained to an interface declaring a
            ' static abstract operator + resolves to the interface's shared operator.
            Dim csRef = GetCSharpCompilation(_operatorCsLibrary).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib

Class C(Of T As IV(Of T))
    Shared Function Add(a As T, b As T) As T
        Return a + b
    End Function
End Class
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.VerifyDiagnostics()
        End Sub

        <Fact>
        Public Sub S26b_TypeParameterSharedOperator_RuntimeValue()
            ' Runtime: a + b through the constrained type parameter dispatches to the actual type
            ' argument's operator implementation (emitted as constrained. + call).
            Dim csRef = GetCSharpCompilation(_operatorCsLibrary).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib
Imports System

Class C(Of T As IV(Of T))
    Shared Function Add(a As T, b As T) As T
        Return a + b
    End Function
End Class

Module M
    Sub Main()
        Console.WriteLine(C(Of MyVec).Add(New MyVec(10), New MyVec(20)).Value)
    End Sub
End Module
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework, options:=TestOptions.ReleaseExe)
            comp.VerifyDiagnostics()
            ' SAIM emit pattern (constrained.+call) is rejected by Roslyn's internal ILVerify;
            ' the .NET runtime executes it (mirror of the C# SAIM tests).
            CompileAndVerify(comp, expectedOutput:="30", verify:=Verification.Skipped)
        End Sub

        <Fact>
        Public Sub S26c_UnconstrainedTypeParameterOperator_BC30452()
            ' An unconstrained type parameter has no interface constraint set, so a + b must not
            ' bind to anything: it stays an undefined-operator error (no mis-binding).
            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Class C(Of T)
    Shared Function Add(a As T, b As T) As T
        Return a + b
    End Function
End Class
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, targetFramework:=_supportingFramework)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_BinaryOperands3, "a + b").WithArguments("+", "T", "T").WithLocation(3, 16))
        End Sub

        <Fact>
        Public Sub S26d_ConcreteTypeOperator_NoDiagnostics_Regression()
            ' Classic MyVec + MyVec on the concrete struct must keep resolving to its own operator.
            Dim csRef = GetCSharpCompilation(_operatorCsLibrary).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib

Module M
    Sub Main()
        Dim s As MyVec = New MyVec(1) + New MyVec(10)
    End Sub
End Module
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.VerifyDiagnostics()
        End Sub

        <Fact>
        Public Sub S26e_UnaryTypeParameterSharedOperator_RuntimeValue()
            ' Unary: -a through the constrained type parameter dispatches to the actual type
            ' argument's unary operator implementation (constrained. + call).
            Dim csSource =
"
namespace ExternLib
{
    public interface IV2<T> where T : IV2<T>
    {
        static abstract T operator -(T value);
    }
    public struct MyNeg : IV2<MyNeg>
    {
        public int Value;
        public MyNeg(int v) { Value = v; }
        public static MyNeg operator -(MyNeg a) => new MyNeg(-a.Value);
    }
}
"
            Dim csRef = GetCSharpCompilation(csSource).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib
Imports System

Class C(Of T As IV2(Of T))
    Shared Function Neg(a As T) As T
        Return -a
    End Function
End Class

Module M
    Sub Main()
        Console.WriteLine(C(Of MyNeg).Neg(New MyNeg(10)).Value)
    End Sub
End Module
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework, options:=TestOptions.ReleaseExe)
            comp.VerifyDiagnostics()
            ' SAIM emit pattern (constrained.+call) is rejected by Roslyn's internal ILVerify;
            ' the .NET runtime executes it (mirror of the C# SAIM tests).
            CompileAndVerify(comp, expectedOutput:="-10", verify:=Verification.Skipped)
        End Sub

        <Fact>
        Public Sub S28_VbDeclarationOfSharedInterfaceMember_BC30270()
            ' Declaration side is out of scope (consumption only); VB still rejects it.
            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Interface I(Of T)
    Shared Sub M1()
End Interface
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, targetFramework:=_supportingFramework)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_BadInterfaceMethodFlags1, "Shared").WithArguments("Shared"))
        End Sub

        <Fact>
        Public Sub Setter_TypeParameterSharedPropertySet_NoDiagnostics()
            ' F19: the SAIM property setter through a constrained type parameter (T.P = v)
            ' now binds cleanly (Binder_Statements.vb passes receiverIsTypeParameter).
            Dim csSource =
"
namespace ExternLib
{
    public interface I1 { static abstract int P { get; set; } }
}
"
            Dim csRef = GetCSharpCompilation(csSource).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib

Class C(Of T As I1)
    Shared Sub F()
        T.P = 1
    End Sub
End Class
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.VerifyDiagnostics()
        End Sub

        <Fact>
        Public Sub Setter_TypeParameterSharedPropertySet_RuntimeValue()
            ' Runtime: T.P = v writes through the type parameter; T.P reads it back.
            Dim csSource =
"
namespace ExternLib
{
    public interface I1 { static abstract int P { get; set; } }
    public struct MyNum : I1
    {
        private static int _p;
        public static int P { get => _p; set => _p = value; }
    }
}
"
            Dim csRef = GetCSharpCompilation(csSource).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib
Imports System

Class C(Of T As I1)
    Shared Function ReadWrite() As Integer
        T.P = 42
        Return T.P
    End Function
End Class

Module M
    Sub Main()
        Console.WriteLine(C(Of MyNum).ReadWrite())
    End Sub
End Module
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework, options:=TestOptions.ReleaseExe)
            comp.VerifyDiagnostics()
            ' SAIM emit pattern (constrained.+call) is rejected by Roslyn's internal ILVerify;
            ' the .NET runtime executes it (mirror of the C# SAIM tests).
            CompileAndVerify(comp, expectedOutput:="42", verify:=Verification.Skipped)
        End Sub

        <Fact>
        Public Sub Setter_TypeParameterSharedPropertyCompound_RuntimeValue()
            ' F21 (legacy item 3): compound assignment T.P += v lowers to get + add + set through
            ' the constrained type parameter; this asserts the end-to-end runtime value.
            Dim csSource =
"
namespace ExternLib
{
    public interface I1 { static abstract int P { get; set; } }
    public struct MyNum : I1
    {
        private static int _p;
        public static int P { get => _p; set => _p = value; }
    }
}
"
            Dim csRef = GetCSharpCompilation(csSource).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib
Imports System

Class C(Of T As I1)
    Shared Function ReadWrite() As Integer
        T.P = 1
        T.P += 5
        Return T.P
    End Function
End Class

Module M
    Sub Main()
        Console.WriteLine(C(Of MyNum).ReadWrite())
    End Sub
End Module
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework, options:=TestOptions.ReleaseExe)
            comp.VerifyDiagnostics()
            ' Same constrained.+call emit pattern as the setter test.
            CompileAndVerify(comp, expectedOutput:="6", verify:=Verification.Skipped)
        End Sub

        <Fact>
        Public Sub G3_RegularMode_TZero_GenericAlgorithm_EmitSucceeds()
            Dim csRef = GetCSharpCompilation(_baseCsLibrary).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib
Imports System

Module M
    Function VBSum(Of T As IHasZero(Of T))(items() As T) As T
        Dim result As T = T.Zero
        For Each item In items
            result = T.Add(result, item)
        Next
        Return result
    End Function

    Sub Main()
        Console.WriteLine(VBSum(Of MyNum)({New MyNum(10), New MyNum(20)}).Value)
    End Sub
End Module
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.VerifyDiagnostics()
            ' The emitted SAIM call uses `constrained.` + `call` (mirroring C# EmitStaticCallExpression).
            ' Roslyn's internal ILVerify rejects that pattern ("Missing callvirt following constrained prefix"),
            ' so verification is skipped -- same as the C# SAIM tests (StaticAbstractMembersInInterfacesTests.cs
            ' uses verify:=Verification.Skipped). The .NET runtime executes the pattern (empirically vbsum=30).
            CompileAndVerify(comp, verify:=Verification.Skipped)
        End Sub

        <Fact>
        Public Sub G4_RegularMode_RuntimeNotSupported_BC32134()
            ' The VB compilation targets a runtime without RuntimeFeature.VirtualStaticsInInterfaces,
            ' so the SAIM type-parameter access reports BC32134 (F10 改动D).
            ' The C# library is still emitted against NetLatest (its metadata carries static abstract);
            ' the gating is decided by the VB compilation's own assembly runtime capability.
            Dim csRef = GetCSharpCompilation(_baseCsLibrary).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib

Class C(Of T As IHasZero(Of T))
    Shared Function F() As T
        Return T.Zero
    End Function
End Class
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=TargetFramework.Mscorlib40AndVBRuntime)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_RuntimeDoesNotSupportStaticAbstractMembersInInterfaces, "T.Zero"))
        End Sub

    End Class

End Namespace
