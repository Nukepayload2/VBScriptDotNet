' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' modreq(In) 白名单豁免（导入层）：required modreq(In) 的 readonly 返回/参数成员可导入，
' 覆盖索引器与方法读取（S1/S2）、in 参数虚方法/委托调用（S18/S19）无诊断。
' modreq(Out) 不豁免：Out 仅函数指针参数允许、VB 不消费，故仍判 unsupported；
' 无 modreq(Out) 的 out 参数调用路径不受影响。

Imports Microsoft.CodeAnalysis.Test.Utilities
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Imports Roslyn.Test.Utilities

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests

    Public Class ConsumeRefReadonlyTests
        Inherits BasicTestBase

        <Fact>
        Public Sub S1_ReadIndexerOfReadOnlySpan()
            Dim source =
"Imports System
Class C
    Sub F()
        Dim x = ""abc"".AsSpan()(0)
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub S2_ReadGetPinnableReference()
            Dim source =
"Imports System
Class C
    Sub F()
        Dim p = ""abc"".AsSpan().GetPinnableReference()
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub S18_InvokeVirtualMethodWithInParameter()
            Dim sourceA =
"public abstract class Base
{
    public abstract void Virt(in int x);
}
public class Derived : Base
{
    public override void Virt(in int x) { }
}"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA)
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim sourceB =
"Class C
    Sub F()
        Dim o As New Derived()
        o.Virt(5)
    End Sub
End Class"
            Dim compB = CreateCompilation(sourceB, references:={refA})
            compB.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub S19_InvokeDelegateWithInParameter()
            Dim sourceA =
"public delegate void Del(in int x);"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA)
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim sourceB =
"Class C
    Sub M(ByRef x As Integer)
    End Sub
    Sub F()
        Dim d As New Del(AddressOf M)
        d(5)
    End Sub
End Class"
            Dim compB = CreateCompilation(sourceB, references:={refA})
            compB.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub Out_ParameterCallNotRegressed()
            ' C# 的 out 参数在元数据中不带 modreq(Out)，不属豁免范围，调用路径不变。
            Dim sourceA =
"public class COut
{
    public void M(out int x) { x = 5; }
}"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA)
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim sourceB =
"Class C
    Sub F()
        Dim o As New COut()
        Dim v As Integer
        o.M(v)
    End Sub
End Class"
            Dim compB = CreateCompilation(sourceB, references:={refA})
            compB.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub Out_RequiredOutModifierStillUnsupported()
            ' 手工 IL 造出带 modreq(OutAttribute) 的 ByRef 参数；Out 不豁免 → 仍判 unsupported。
            ' C# 仅在函数指针参数允许 Out modreq，VB 不消费函数指针，因此 Out 必须保持拒绝。
            Dim ilSource =
".class public auto ansi beforefieldinit COutModreq extends [mscorlib]System.Object
{
  .method public hidebysig specialname rtspecialname instance void .ctor() cil managed
  {
    ldarg.0
    call instance void [mscorlib]System.Object::.ctor()
    ret
  }
  .method public hidebysig instance void M(int32 modreq([mscorlib]System.Runtime.InteropServices.OutAttribute) & x) cil managed
  {
    ret
  }
}"
            Dim refIL = CompileIL(ilSource)

            Dim source =
"Class C
    Sub F()
        Dim o As New COutModreq()
        Dim v As Integer
        o.M(v)
    End Sub
End Class"
            Dim comp = CreateCompilation(source, references:={refIL})
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_UnsupportedMethod1, "M").WithArguments("M"))
        End Sub

        <Fact>
        Public Sub S4_SemanticModelReadonlyIndexerFlag()
            ' ReadOnlySpan(Of T).Item（ref readonly 返回）应暴露 ReturnsByRefReadonly=True、RefKind.RefReadOnly。
            Dim source =
"Imports System
Class C
    Sub F()
        Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10})
        Dim x = s(0)
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()

            Dim tree = comp.SyntaxTrees.Single()
            Dim model = comp.GetSemanticModel(tree)
            Dim root = tree.GetCompilationUnitRoot()
            Dim invocation = root.FindToken(root.ToFullString().IndexOf("s(0)", StringComparison.Ordinal)).Parent.FirstAncestorOrSelf(Of InvocationExpressionSyntax)()
            Dim prop = TryCast(model.GetSymbolInfo(invocation).Symbol, IPropertySymbol)
            Assert.NotNull(prop)
            Assert.True(prop.ReturnsByRef)
            Assert.True(prop.ReturnsByRefReadonly)
            Assert.Equal(Microsoft.CodeAnalysis.RefKind.RefReadOnly, prop.RefKind)
        End Sub

        <Fact>
        Public Sub S5_SemanticModelReadonlyMethodFlag()
            ' GetPinnableReference()（ref readonly 返回）应暴露 ReturnsByRefReadonly=True、RefKind.RefReadOnly。
            Dim source =
"Imports System
Class C
    Sub F()
        Dim p = ""abc"".AsSpan().GetPinnableReference()
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()

            Dim tree = comp.SyntaxTrees.Single()
            Dim model = comp.GetSemanticModel(tree)
            Dim root = tree.GetCompilationUnitRoot()
            Dim invocation = root.FindToken(root.ToFullString().IndexOf("GetPinnableReference", StringComparison.Ordinal)).Parent.FirstAncestorOrSelf(Of InvocationExpressionSyntax)()
            Dim method = TryCast(model.GetSymbolInfo(invocation).Symbol, IMethodSymbol)
            Assert.NotNull(method)
            Assert.True(method.ReturnsByRef)
            Assert.True(method.ReturnsByRefReadonly)
            Assert.Equal(Microsoft.CodeAnalysis.RefKind.RefReadOnly, method.RefKind)
        End Sub

        <Fact>
        Public Sub S21_PureRefReturnNotRegressed()
            ' 纯 ref 返回：ReturnsByRefReadonly=False、RefKind.Ref（不得误标 readonly）。
            Dim sourceA =
"public class TestRef
{
    private int value = 0;
    public ref int GetValue() { return ref value; }
    public ref int P => ref value;
}"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA)
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim source =
"Class C
    Sub F()
        Dim o As New TestRef()
        Dim a = o.GetValue()
        Dim b = o.P
    End Sub
End Class"
            Dim comp = CreateCompilation(source, references:={refA})
            comp.AssertNoDiagnostics()

            Dim tree = comp.SyntaxTrees.Single()
            Dim model = comp.GetSemanticModel(tree)
            Dim root = tree.GetCompilationUnitRoot()

            Dim methodInvocation = root.FindToken(root.ToFullString().IndexOf("GetValue", StringComparison.Ordinal)).Parent.FirstAncestorOrSelf(Of InvocationExpressionSyntax)()
            Dim method = TryCast(model.GetSymbolInfo(methodInvocation).Symbol, IMethodSymbol)
            Assert.NotNull(method)
            Assert.True(method.ReturnsByRef)
            Assert.False(method.ReturnsByRefReadonly)
            Assert.Equal(Microsoft.CodeAnalysis.RefKind.Ref, method.RefKind)

            Dim propAccess = root.FindToken(root.ToFullString().IndexOf("o.P", StringComparison.Ordinal)).Parent.FirstAncestorOrSelf(Of MemberAccessExpressionSyntax)()
            Dim prop = TryCast(model.GetSymbolInfo(propAccess).Symbol, IPropertySymbol)
            Assert.NotNull(prop)
            Assert.True(prop.ReturnsByRef)
            Assert.False(prop.ReturnsByRefReadonly)
            Assert.Equal(Microsoft.CodeAnalysis.RefKind.Ref, prop.RefKind)
        End Sub

        <Fact>
        Public Sub S20_Consistency_NonByRefReturnWithInModreq_Method()
            ' 一致性校验：非 ByRef 返回却带 modreq(In)（第三方乱写元数据）→ unsupported。
            ' 解码后该 modreq 落在返回类型 CustomModifiers（非 RefCustomModifiers），
            ' 由 DeriveUseSiteInfoFromCustomModifiers(ReturnTypeCustomModifiers) 判 ERR_UnsupportedMethod1。
            Dim ilSource =
".class public auto ansi beforefieldinit CInModNonByRef extends [mscorlib]System.Object
{
  .method public hidebysig instance int32 modreq([mscorlib]System.Runtime.InteropServices.InAttribute) M() cil managed
  {
    ldc.i4.0
    ret
  }
  .method public hidebysig specialname rtspecialname instance void .ctor() cil managed
  {
    ldarg.0
    call instance void [mscorlib]System.Object::.ctor()
    ret
  }
}"
            Dim refIL = CompileIL(ilSource)

            Dim source =
"Class C
    Sub F()
        Dim o As New CInModNonByRef()
        Dim x = o.M()
    End Sub
End Class"
            Dim comp = CreateCompilation(source, references:={refIL})
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_UnsupportedMethod1, "M").WithArguments("M"))
        End Sub

        <Fact>
        Public Sub S20_Consistency_NonByRefReturnWithInModreq_Property()
            ' 一致性校验（属性路径）：属性签名非 ByRef 返回却带 modreq(In) → unsupported。
            ' PEPropertySymbol 构造检查 propertyParams(...).CustomModifiers.AnyRequired() 判 ERR_UnsupportedProperty1。
            Dim ilSource =
".class public auto ansi beforefieldinit CInModNonByRefProp extends [mscorlib]System.Object
{
  .field private int32 'value'
  .method public hidebysig specialname instance int32 modreq([mscorlib]System.Runtime.InteropServices.InAttribute) get_P() cil managed
  {
    ldarg.0
    ldfld int32 CInModNonByRefProp::'value'
    ret
  }
  .method public hidebysig specialname rtspecialname instance void .ctor() cil managed
  {
    ldarg.0
    call instance void [mscorlib]System.Object::.ctor()
    ret
  }
  .property instance int32 modreq([mscorlib]System.Runtime.InteropServices.InAttribute) P()
  {
    .get instance int32 modreq([mscorlib]System.Runtime.InteropServices.InAttribute) CInModNonByRefProp::get_P()
  }
}"
            Dim refIL = CompileIL(ilSource)

            Dim source =
"Class C
    Sub F()
        Dim o As New CInModNonByRefProp()
        Dim x = o.P
    End Sub
End Class"
            Dim comp = CreateCompilation(source, references:={refIL})
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_UnsupportedProperty1, "P").WithArguments("CInModNonByRefProp.P"))
        End Sub

        <Fact>
        Public Sub S9_ByRefArgumentCopyOutDiscardsWriteBack()
            ' readonly-lvalue（s(0)）作可变 ByRef 实参 → copy-out 传副本、写回丢弃。
            ' M 内 v = 99 只改副本；s(0) 仍是 10（无写穿）。
            Dim source =
"Imports System
Module Program
    Sub M(ByRef v As Integer)
        v = 99
    End Sub
    Sub Main()
        Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10})
        M(s(0))
        Console.WriteLine(s(0))
    End Sub
End Module"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, options:=TestOptions.ReleaseExe)
            comp.AssertNoDiagnostics()
            CompileAndVerify(comp, expectedOutput:="10")
        End Sub

        <Fact>
        Public Sub S10_ByRefArgumentInReceiver()
            ' readonly-lvalue（s(0)）作 C# 'in' 实参（VB 映射 RefKind.Ref）→ copy-out 传副本、
            ' 值正确、无写穿（多一次拷贝，零正确性风险）。
            Dim sourceA =
"public class CIn
{
    public int Last;
    public void M(in int x) { Last = x; }
}"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA,
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.NetLatest))
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim source =
"Imports System
Module Program
    Sub Main()
        Dim o As New CIn()
        Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10})
        o.M(s(0))
        Console.WriteLine(o.Last)
        Console.WriteLine(s(0))
    End Sub
End Module"
            Dim comp = CreateCompilation(source, references:={refA}, targetFramework:=TargetFramework.NetLatest, options:=TestOptions.ReleaseExe)
            comp.AssertNoDiagnostics()
            CompileAndVerify(comp, expectedOutput:=<![CDATA[
10
10]]>)
        End Sub

        <Fact>
        Public Sub S6_DirectAssignmentRejected()
            ' readonly-lvalue（s(0)）作直接赋值目标 → ERR_LValueRequired(30068)。
            ' 错误族纪律：不送 30064（'ReadOnly' variable 措辞失准）/30098/30643/30657。
            Dim source =
"Imports System
Class C
    Sub F()
        Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10})
        s(0) = 5
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_LValueRequired, "s(0)"))
        End Sub

        <Fact>
        Public Sub S7_CompoundAssignmentRejected()
            ' 复合赋值 s(0) += 5 经 BindCompoundAssignment → AdjustAssignmentTarget 自动覆盖。
            Dim source =
"Imports System
Class C
    Sub F()
        Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10})
        s(0) += 5
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_LValueRequired, "s(0)"))
        End Sub

        <Fact>
        Public Sub S8_MidAssignmentRejected()
            ' Mid(o.S, 1) = "x"（C# emit 库 ref readonly String 属性）经 Mid 语句 → AdjustAssignmentTarget 自动覆盖。
            Dim sourceA =
"public class CMid
{
    private string _s = ""hello"";
    public ref readonly string S => ref _s;
}"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA,
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.NetLatest))
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim source =
"Class C
    Sub F()
        Dim o As New CMid()
        Mid(o.S, 1) = ""x""
    End Sub
End Class"
            ' C# emit 库按 NetLatest 编译，InAttribute 落在 System.Runtime；VB 侧须同用 NetLatest 引用集解析 modreq 类型。
            Dim comp = CreateCompilation(source, references:={refA}, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_LValueRequired, "o.S"))
        End Sub

        <Fact>
        Public Sub S22_ErrorCodeIsLValueRequiredNotReadonlyFamily()
            ' 错误码不复用纪律：触发 30068（ERR_LValueRequired），不是 30098/30064/30643/30657。
            ' 30064 显式否决：'ReadOnly' variable 对 s(0)（ref-返回属性访问）措辞失准。
            Dim source =
"Imports System
Class C
    Sub F()
        Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10})
        s(0) = 5
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            Dim diagnostics = comp.GetDiagnostics()
            Assert.Single(diagnostics, Function(d) d.Id = "BC30068")
            Assert.DoesNotContain(diagnostics, Function(d) d.Id = "BC30098" OrElse
                                                               d.Id = "BC30064" OrElse
                                                               d.Id = "BC30643" OrElse
                                                               d.Id = "BC30657")
        End Sub

        ' S22b（备用，未启用）：当 IDE 快速信息格式含 IncludeRef（用户看得到 byref 上下文）时，
        ' 30068 的「is a value」对 readonly-lvalue 措辞突兀；切换点在 AdjustAssignmentTarget/Binder_Expressions
        ' 一处，改判 ERR_ReadOnlyAssignment(30064)。启用时把下面方法标 <Fact> 并把断言码切为 30064。
        Public Sub S22b_30064SwitchExitAlternative()
            Dim source =
"Imports System
Class C
    Sub F()
        Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10})
        s(0) = 5
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_ReadOnlyAssignment, "s(0)"))
        End Sub

        <Fact>
        Public Sub S21b_PureRefReturnAssignmentWriteBackNotRegressed()
            ' 纯 ref 返回（方法调用 + 索引器作赋值目标）→ 写回仍生效（可变内存）。
            ' 同时覆盖 AdjustAssignmentTarget 的 BoundCall 兜底分支：ReturnsByRefReadOnly=False 必须放行。
            Dim sourceA =
"public class TestRef
{
    private int[] arr = new int[] { 10, 20, 30 };
    public ref int GetItem(int i) { return ref arr[i]; }
    public ref int this[int i] => ref arr[i];
}"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA,
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.NetLatest))
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim source =
"Imports System
Module Program
    Sub Main()
        Dim o As New TestRef()
        o.GetItem(1) = 42
        o(2) = 84
        Console.WriteLine(o.GetItem(1))
        Console.WriteLine(o(2))
    End Sub
End Module"
            Dim comp = CreateCompilation(source, references:={refA}, targetFramework:=TargetFramework.NetLatest, options:=TestOptions.ReleaseExe)
            comp.AssertNoDiagnostics()
            CompileAndVerify(comp, expectedOutput:=<![CDATA[
42
84]]>)
        End Sub

        <Fact>
        Public Sub S23_CallReturningRefReadonlyAssignmentRejected()
            ' readonly ByRef-返回方法调用（GetPinnableReference()）作赋值目标 → ERR_LValueRequired(30068)。
            ' 与 S21b（纯 ref 放行）互补，覆盖 AdjustAssignmentTarget Case BoundKind.Call 的
            ' ReturnsByRefReadOnly=True 侧：若该安全网放行，GetPinnableReference() 写穿只读内存将静默通过。
            ' 两处必要变形：
            '   1) 不能写 "abc".AsSpan().GetPinnableReference() = ...（语句以字符串字面量开头 → 解析层 ERR_Syntax，不达绑定），
            '      须先把 span 绑到局部变量 s，再 s.GetPinnableReference() = ... 才进 AdjustAssignmentTarget 的 Call 分支。
            '   2) RHS 不能用 5（Integer→Char 无隐式转换 → 多一条 ERR_IntegralToCharTypeMismatch1），改用 Char 字面量 "x"c。
            ' VerifyDiagnostics 只给一条期望 → 恰一个 30068，且无 30064/30098/30643/30657。
            Dim source =
"Imports System
Class C
    Sub F()
        Dim s = ""abc"".AsSpan()
        s.GetPinnableReference() = ""x""c
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_LValueRequired, "s.GetPinnableReference()"))

            ' 方法路径只读标志：GetPinnableReference() ReturnsByRefReadOnly=True，本分支依赖该标志触发。
            Dim tree = comp.SyntaxTrees.Single()
            Dim model = comp.GetSemanticModel(tree)
            Dim root = tree.GetCompilationUnitRoot()
            Dim invocation = root.FindToken(root.ToFullString().IndexOf("GetPinnableReference", StringComparison.Ordinal)).Parent.FirstAncestorOrSelf(Of InvocationExpressionSyntax)()
            Dim method = TryCast(model.GetSymbolInfo(invocation).Symbol, IMethodSymbol)
            Assert.NotNull(method)
            Assert.True(method.ReturnsByRef)
            Assert.True(method.ReturnsByRefReadonly)
            Assert.Equal(Microsoft.CodeAnalysis.RefKind.RefReadOnly, method.RefKind)
        End Sub

        <Fact>
        Public Sub S13_WithBlockReadonlyLvalueAssignment()
            Dim source =
"Imports System
Class C
    Sub F()
        Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10})
        With s
            .Item(0) = 5
        End With
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_LValueRequired, ".Item(0)"))
        End Sub

        <Fact>
        Public Sub S12_WithBlockReadonlyReceiverRead()
            ' With 块 readonly ByRef-返回 receiver 走值捕获，.Member 读取副本。
            ' With h(0)（C# emit 库 ref readonly Row 索引器）→ 无诊断 + y 读到副本的 X = 10。
            Dim sourceA =
"public struct Row
{
    public int X;
}
public class Holder
{
    private Row[] _rows = new Row[] { new Row { X = 10 } };
    public ref readonly Row this[int i] => ref _rows[i];
}"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA,
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.NetLatest))
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim source =
"Imports System
Module Program
    Sub Main()
        Dim h As New Holder()
        With h(0)
            Dim y = .X
            Console.WriteLine(y)
        End With
    End Sub
End Module"
            Dim comp = CreateCompilation(source, references:={refA}, targetFramework:=TargetFramework.NetLatest, options:=TestOptions.ReleaseExe)
            comp.AssertNoDiagnostics()
            CompileAndVerify(comp, expectedOutput:="10")
        End Sub

        <Fact>
        Public Sub S13b_WithBlockReadonlyReceiverMemberWrite()
            ' With h(0)（ref readonly Row 属性索引器）内 .X = 5 与链式 h(0).X = 5 是同一成员写，
            ' 判 BC30068 拒绝。readonly-lvalue receiver 走 RValue placeholder → .X 落非 lvalue →
            ' 恰一 ERR_LValueRequired(30068) @ .X。
            Dim sourceA =
"public struct Row
{
    public int X;
}
public class Holder
{
    private Row[] _rows = new Row[] { new Row { X = 10 } };
    public ref readonly Row this[int i] => ref _rows[i];
}"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA,
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.NetLatest))
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim source =
"Class C
    Sub F()
        Dim h As New Holder()
        With h(0)
            .X = 5
        End With
    End Sub
End Class"
            Dim comp = CreateCompilation(source, references:={refA}, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_LValueRequired, ".X"))
        End Sub

        <Fact>
        Public Sub S13c_WithBlockReadonlyMethodCallReceiverMemberWrite()
            ' With o.S(0)（ref readonly Row 方法调用）内 .X = 5 与链式 o.S(0).X = 5 是同一成员写，
            ' 判 BC30068 拒绝。readonly 方法调用 receiver 走 RValue placeholder
            ' （IsReadOnlyLValue 的 Call 分支）→ 恰一 30068 @ .X。
            ' 同时确认 AdjustAssignmentTarget 的 FieldAccess 拒绝检查不误伤 With 占位符（占位符非 readonly-lvalue）。
            Dim sourceA =
"public struct Row
{
    public int X;
}
public class Outer
{
    private Row[] _rows = new Row[] { new Row { X = 10 } };
    public ref readonly Row S(int i) => ref _rows[i];
}"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA,
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.NetLatest))
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim source =
"Class C
    Sub F()
        Dim o As New Outer()
        With o.S(0)
            .X = 5
        End With
    End Sub
End Class"
            Dim comp = CreateCompilation(source, references:={refA}, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_LValueRequired, ".X"))
        End Sub

        <Fact>
        Public Sub S15c_MemberChainPropertyIndexerWrite()
            ' h(0).X = 5（h(0) 是 ref readonly Row 属性索引器）：
            ' 链式行走器 IsReadOnlyLValueOrMemberOfReadOnlyLValue 的 PropertyAccess 根路径 → 30068。
            Dim sourceA =
"public struct Row
{
    public int X;
}
public class Holder
{
    private Row[] _rows = new Row[] { new Row { X = 10 } };
    public ref readonly Row this[int i] => ref _rows[i];
}"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA,
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.NetLatest))
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim source =
"Class C
    Sub F()
        Dim h As New Holder()
        h(0).X = 5
    End Sub
End Class"
            Dim comp = CreateCompilation(source, references:={refA}, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_LValueRequired, "h(0).X"))
        End Sub

        <Fact>
        Public Sub S13d_WithBlockReadonlyReceiverInIterator()
            ' readonly ByRef-返回 receiver 在 iterator 中（DoNotUseByRefLocal=True）。
            ' readonly 值捕获总是安全，故不设 _capturedLvalueByRefCallOrProperty → 不误报 BC37326
            ' （ERR_UnsupportedRefReturningCallInWithStatement），且 Debug.Assert(state.DoNotUseByRefLocal) 不触发。
            ' 与 CodeGenRefReturnTests 的「regular ByRef 在 iterator/async → BC37326」对照（readonly 放行）。
            Dim sourceA =
"public struct Row
{
    public int X;
}
public class Holder
{
    private Row[] _rows = new Row[] { new Row { X = 10 } };
    public ref readonly Row this[int i] => ref _rows[i];
}"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA,
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.NetLatest))
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim source =
"Imports System
Class C
    Iterator Function F() As System.Collections.IEnumerable
        Dim h As New Holder()
        With h(0)
            Yield .X
        End With
    End Function
End Class"
            Dim comp = CreateCompilation(source, references:={refA}, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub S13e_WithBlockReadonlyReceiverMemberChainWrite()
            ' With o.S(0).Inner（o.S(0) 返回 ref readonly Row，Inner 是 readonly-lvalue 的成员访问）
            ' 内 .X = 5 → 判 BC30068 拒绝。
            ' 判定必须用成员链版 helper IsReadOnlyLValueOrMemberOfReadOnlyLValue（覆盖 With o.S(0).Inner 形态），
            ' readonly-lvalue 成员链 receiver 走 RValue placeholder → 恰一 30068 @ .X。
            Dim sourceA =
"public struct InnerStruct
{
    public int X;
}
public struct Row
{
    public InnerStruct Inner;
}
public class Outer
{
    private Row[] _rows = new Row[] { new Row { Inner = new InnerStruct { X = 10 } } };
    public ref readonly Row S(int i) => ref _rows[i];
}"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA,
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.NetLatest))
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim source =
"Class C
    Sub F()
        Dim o As New Outer()
        With o.S(0).Inner
            .X = 5
        End With
    End Sub
End Class"
            Dim comp = CreateCompilation(source, references:={refA}, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_LValueRequired, ".X"))
        End Sub

        <Fact>
        Public Sub S13g_NestedWriteInWithBlock()
            ' With h(0)（ref readonly Row）内 .Inner.X = 5（.Inner 是 readonly-lvalue 的成员访问）：
            ' readonly-lvalue receiver 走 RValue placeholder → .Inner 落非 lvalue → .Inner.X 落非 lvalue
            ' → 恰一 ERR_LValueRequired(30068)。
            Dim sourceA =
"public struct InnerStruct
{
    public int X;
}
public struct Row
{
    public InnerStruct Inner;
}
public class Holder
{
    private Row[] _rows = new Row[] { new Row { Inner = new InnerStruct { X = 10 } } };
    public ref readonly Row this[int i] => ref _rows[i];
}"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA,
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.NetLatest))
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim source =
"Class C
    Sub F()
        Dim h As New Holder()
        With h(0)
            .Inner.X = 5
        End With
    End Sub
End Class"
            Dim comp = CreateCompilation(source, references:={refA}, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_LValueRequired, ".Inner.X"))
        End Sub

        <Fact>
        Public Sub S13f_WithBlockReadonlyReceiverMemberChainRead()
            ' 读形态：With o.S(0).Inner : Dim y = .X 读取副本。
            ' 与 S13e 写形态互补：draft（debug 绑定）路径不抛 Not value.IsLValue，读到的 X = 10。
            Dim sourceA =
"public struct InnerStruct
{
    public int X;
}
public struct Row
{
    public InnerStruct Inner;
}
public class Outer
{
    private Row[] _rows = new Row[] { new Row { Inner = new InnerStruct { X = 10 } } };
    public ref readonly Row S(int i) => ref _rows[i];
}"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA,
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.NetLatest))
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim source =
"Imports System
Module Program
    Sub Main()
        Dim o As New Outer()
        With o.S(0).Inner
            Dim y = .X
            Console.WriteLine(y)
        End With
    End Sub
End Module"
            Dim comp = CreateCompilation(source, references:={refA}, targetFramework:=TargetFramework.NetLatest, options:=TestOptions.ReleaseExe)
            comp.AssertNoDiagnostics()
            CompileAndVerify(comp, expectedOutput:="10")
        End Sub

        <Fact>
        Public Sub S15b_MemberChainMemberWriteOnReadonlyReceiver()
            ' 成员链 o.S(0).X = 5（o.S 返回 ref readonly Row）：目标是 FieldAccess，基 receiver 为
            ' readonly-lvalue → ERR_LValueRequired(30068)，与 s(0) = 5 直接赋值拒绝同一语义，
            ' 不写穿只读内存。值捕获操作副本与拒绝二选一时，本路径取拒绝。
            Dim sourceA =
"public struct Row
{
    public int X;
}
public class Outer
{
    private Row[] _rows = new Row[] { new Row { X = 10 } };
    public ref readonly Row S(int i) => ref _rows[i];
}"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA,
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.NetLatest))
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim source =
"Imports System
Module Program
    Sub Main()
        Dim o As New Outer()
        o.S(0).X = 5
        Console.WriteLine(o.S(0).X)
    End Sub
End Module"
            Dim comp = CreateCompilation(source, references:={refA}, targetFramework:=TargetFramework.NetLatest, options:=TestOptions.ReleaseExe)
            ' 恰一个 30068 @ o.S(0).X；无 30064/30098/30643/30657（VerifyDiagnostics 单期望自动覆盖错误族纪律）。
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_LValueRequired, "o.S(0).X"))
        End Sub

        <Fact>
        Public Sub G4_WithBlockRegularModeRead()
            ' With s（ReadOnlySpan 局部）内读取 .Item(0) → 无诊断；Regular 模式与脚本同 kind 语义一致。
            Dim source =
"Imports System
Class C
    Sub F()
        Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10})
        With s
            Dim x = .Item(0)
        End With
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub G5_ForEachRegularMode()
            ' Regular 模式 For Each over ReadOnlySpan → 无诊断；脚本模式同 kind 语义一致；
            ' 枚举值正确由 S24/S16 的运行断言覆盖（输出 a/b）。
            ' ReadOnlySpan(Of T).Enumerator.Current 是 ref readonly 属性（modreq(In)），豁免后
            ' 可导入；For Each pattern 的 Current 谓词只查 IsReadable（无 ByRef 排除），值读走 auto-deref。
            Dim source =
"Imports System
Class C
    Sub F()
        Dim s As New ReadOnlySpan(Of Char)(""ab"".ToCharArray())
        For Each c As Char In s
            Console.WriteLine(c)
        Next
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub S9b_PureRefReturnAsByRefArgumentWriteBackNotRegressed()
            ' 纯 ref 返回（C# ref int 方法）作 ByRef 实参 → 写回仍生效（改的是原内存）。
            Dim sourceA =
"public class TestRef
{
    private int[] arr = new int[] { 10 };
    public ref int GetItem(int i) { return ref arr[i]; }
    public ref int P => ref arr[0];
}"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA,
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.NetLatest))
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim source =
"Imports System
Module Program
    Sub M(ByRef v As Integer)
        v = 99
    End Sub
    Sub Main()
        Dim o As New TestRef()
        M(o.GetItem(0))
        Console.WriteLine(o.P)
    End Sub
End Module"
            Dim comp = CreateCompilation(source, references:={refA}, targetFramework:=TargetFramework.NetLatest, options:=TestOptions.ReleaseExe)
            comp.AssertNoDiagnostics()
            CompileAndVerify(comp, expectedOutput:="99")
        End Sub

        ' L4（Regular）矩阵说明：语义层用例经 CreateCompilation 走 SourceCodeKind.Regular
        ' （TestOptions.ReleaseDll），与 G 系列（L4 Regular 变体）为同一编译路径；
        ' G4/G5 在此显式标注 L4。For Each（S16/G5/ByRefLikeTests.S24）同场景，枚举值由运行断言覆盖。

        <Fact>
        Public Sub S3_ReadLengthPropertyOfReadOnlySpan()
            ' 普通属性（Length，非 ByRef 返回）不回归——无诊断 + 运行值 3。
            Dim source =
"Imports System
Module Program
    Sub Main()
        Console.WriteLine(""abc"".AsSpan().Length)
    End Sub
End Module"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest, options:=TestOptions.ReleaseExe)
            comp.AssertNoDiagnostics()
            CompileAndVerify(comp, expectedOutput:="3")
        End Sub

        <Fact>
        Public Sub S11_TypeInferenceReturnsElementType()
            ' 类型推断 Dim x = s(0) 褪 ByRef + auto-deref → x 为元素类型 Integer（非 ByRef）。
            Dim source =
"Imports System
Class C
    Sub F()
        Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10})
        Dim x = s(0)
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()

            Dim tree = comp.SyntaxTrees.Single()
            Dim model = comp.GetSemanticModel(tree)
            Dim root = tree.GetCompilationUnitRoot()
            Dim localDecl = root.FindToken(root.ToFullString().IndexOf("Dim x = s(0)", StringComparison.Ordinal)).Parent.FirstAncestorOrSelf(Of LocalDeclarationStatementSyntax)()
            Dim local = TryCast(model.GetDeclaredSymbol(localDecl.Declarators.Single().Names.Single()), ILocalSymbol)
            Assert.NotNull(local)
            Assert.Equal(SpecialType.System_Int32, local.Type.SpecialType)
            Assert.Equal(Microsoft.CodeAnalysis.RefKind.None, local.RefKind)
        End Sub

        <Fact>
        Public Sub S14_MemberChainReadNoDiagnostics()
            ' 成员链读取（C# emit 库 ref readonly Row）：o.S(0) 与 o.S(0).X 均无诊断。
            Dim sourceA =
"public struct Row
{
    public int X;
}
public class Outer
{
    private Row[] _rows = new Row[] { new Row { X = 10 } };
    public ref readonly Row S(int i) => ref _rows[i];
}"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA,
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.NetLatest))
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim source =
"Imports System
Class C
    Sub F()
        Dim o As New Outer()
        Dim r = o.S(0)
        Dim x = o.S(0).X
    End Sub
End Class"
            Dim comp = CreateCompilation(source, references:={refA}, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()
        End Sub

        <Fact>
        Public Sub S15a_MemberChainDirectAssignmentRejected()
            ' 成员链直接赋值 o.S(0) = newRow（o.S 返回 ref readonly Row）→ store-through-ref
            ' 直写 readonly-lvalue → ERR_LValueRequired(30068)。
            ' 与 S15b（成员链成员写）互补：此处目标是被返回的整个 readonly 结构。
            Dim sourceA =
"public struct Row
{
    public int X;
}
public class Outer
{
    private Row[] _rows = new Row[] { new Row { X = 10 } };
    public ref readonly Row S(int i) => ref _rows[i];
}"
            Dim compA = CreateCSharpCompilation(GetUniqueName(), sourceA,
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.NetLatest))
            compA.VerifyDiagnostics()
            Dim refA = compA.EmitToImageReference()

            Dim source =
"Class C
    Sub F()
        Dim o As New Outer()
        Dim newRow As New Row()
        o.S(0) = newRow
    End Sub
End Class"
            Dim comp = CreateCompilation(source, references:={refA}, targetFramework:=TargetFramework.NetLatest)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_LValueRequired, "o.S(0)"))
        End Sub

        <Fact>
        Public Sub S17_ExpressionLambdaReadsRefReadonlyValue()
            ' 表达式 lambda 内读取 s(0) → 无诊断（读取解锁在 lambda 值捕获上下文同样生效）。
            Dim source =
"Imports System
Class C
    Sub F()
        Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10})
        Dim f = Function() s(0)
    End Sub
End Class"
            Dim comp = CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)
            comp.AssertNoDiagnostics()
        End Sub
    End Class
End Namespace
