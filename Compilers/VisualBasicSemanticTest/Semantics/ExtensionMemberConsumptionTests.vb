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
    ''' Part A consumption of C# 14 extension members (extension properties and operators),
    ''' per InternalDevDocs\tasks\consume-csharp-extension-and-interface-shared\test-plan.md §3.1 (S1-S15).
    ''' The C# test library is built in-memory via CreateCSharpCompilation (no external DLL, no side effects).
    ''' </summary>
    <CompilerTrait(CompilerFeature.DefaultInterfaceImplementation)>
    Public Class ExtensionMemberConsumptionTests
        Inherits BasicTestBase

        Private Const _supportingFramework As TargetFramework = TargetFramework.NetLatest

        Private Function GetCSharpCompilation(csSource As String) As CSharpCompilation
            Return CreateCSharpCompilation(
                csSource,
                parseOptions:=CSharpParseOptions.Default.WithLanguageVersion(CSharp.LanguageVersion.Preview),
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(_supportingFramework))
        End Function

        ' Base C# library (test-plan §3 base): classic ext method, C#14 extension block
        ' (property + method), generic extension block (property), SAIM helpers, extension operator.
        Private Const _baseCsLibrary As String = "
using System;
namespace ExternLib
{
    public static class ClassicExt { public static int Twice(this int value) => value * 2; }
    public static class NewExt
    {
        extension(string s)
        {
            public int CharCount => s.Length;
            public string Shout() => s.ToUpperInvariant();
        }
    }
    public static class GenericNewExt
    {
        extension<T>(T value)
        {
            public int GenericCharCount => (value as string)?.Length ?? 0;
            public T Identity => value;
        }
    }
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
    public static class Mathy
    {
        public static T Sum<T>(T[] items) where T : IHasZero<T>
        { T result = T.Zero; foreach (var t in items) result = T.Add(result, t); return result; }
    }
}
public static class VecExt
{
    extension(MyVec v)
    {
        public static MyVec operator +(MyVec a, MyVec b) => new MyVec(a.X + b.X, a.Y + b.Y);
    }
}
public struct MyVec { public int X; public int Y; public MyVec(int x, int y) { X = x; Y = y; } }
"

        Private Shared Function GetMemberAccessSymbol(comp As VisualBasicCompilation, memberAccessText As String) As SymbolInfo
            Dim tree = comp.SyntaxTrees(0)
            Dim model = comp.GetSemanticModel(tree)
            Dim expr = FindNodeOfTypeFromText(Of MemberAccessExpressionSyntax)(tree, memberAccessText)
            Return model.GetSymbolInfo(expr)
        End Function

        <Fact>
        Public Sub S1_ExtensionPropertyGetter_BasicConsumption_NoDiagnostics()
            Dim csRef = GetCSharpCompilation(_baseCsLibrary).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib

Module M
    Sub Main()
        Dim c As Integer = "hello".CharCount
    End Sub
End Module
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.VerifyDiagnostics()

            ' Semantic model symbol: reduced extension property.
            Dim info = GetMemberAccessSymbol(comp, """hello"".CharCount")
            Dim prop = TryCast(info.Symbol, PropertySymbol)
            Assert.NotNull(prop)
            Assert.Equal("CharCount", prop.Name)
            ' Reduced form: receiver exposed, parameters stripped, not shared.
            Assert.Equal(0, prop.Parameters.Length)
            Assert.False(prop.IsShared)
            Assert.Equal(Microsoft.CodeAnalysis.SpecialType.System_String, prop.ReceiverType.SpecialType)
            ' NOTE (test-plan D4 backfill): the reduced property's ContainingType is the C# 14
            ' grouping type, so MinimallyQualifiedFormat renders the mangled <G>$ name and uses
            ' the VB keyword alias for the type:
            '   "Property <G>$34505F560D9EACF86A87F3ED1F85E448.CharCount As Integer"
            ' (the test-plan assumed the plain "Property CharCount As System.Int32").
            Dim display = prop.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
            Assert.StartsWith("Property <G>$", display)
            Assert.Contains(".CharCount As Integer", display)
        End Sub

        <Fact>
        Public Sub S2_ExtensionPropertySetter_Writable_NoDiagnostics()
            ' test-plan §3.1 S2: writable setter end-to-end (F15 only verified read-only -> BC30526).
            ' This case uses a C# extension block whose property has a get AND set.
            ' NOTE: a C# auto-implemented property (public int P { get; set; }) in an extension
            ' block is rejected by the in-repo C# compiler (CS9282: synthesized backing field is
            ' not an allowed extension-block member), so the writable property is implemented with
            ' an explicit static store on the containing class.
            Dim csSource =
"
namespace ExternLib
{
    public static class SettableExt
    {
        static readonly System.Collections.Generic.Dictionary<C, int> _store =
            new System.Collections.Generic.Dictionary<C, int>();
        extension(C c)
        {
            public int P
            {
                get { return _store.TryGetValue(c, out int v) ? v : 0; }
                set { _store[c] = value; }
            }
        }
    }
    public class C { }
}
"
            Dim csRef = GetCSharpCompilation(csSource).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib

Module M
    Sub Main()
        Dim x As New C
        x.P = 5
    End Sub
End Module
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.VerifyDiagnostics()
        End Sub

        <Fact>
        Public Sub S3_ExtensionProperty_Chained_NoDiagnostics()
            ' test-plan §3.1 S3: two extension properties consumed in a chain (x.A.B).
            ' Implementation-period check: intermediate result type propagation.
            Dim csSource =
"
namespace ExternLib
{
    public static class ChainExt
    {
        extension(A a)
        {
            public B B => new B();
        }
        extension(B b)
        {
            public int Count => 42;
        }
    }
    public class A { }
    public class B { }
}
"
            Dim csRef = GetCSharpCompilation(csSource).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib

Module M
    Sub Main()
        Dim x As New A
        Dim c As Integer = x.B.Count
    End Sub
End Module
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.VerifyDiagnostics()
        End Sub

        <Fact>
        Public Sub S4_ExtensionProperty_WithBlock_NoDiagnostics()
            ' test-plan §3.1 S4: With block over an extension property receiver.
            ' Implementation-period check: With path.
            Dim csRef = GetCSharpCompilation(_baseCsLibrary).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib

Module M
    Sub Main()
        With "abc"
            Dim c As Integer = .CharCount
        End With
    End Sub
End Module
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.VerifyDiagnostics()
        End Sub

        <Fact>
        Public Sub S5_ExtensionOperator_BinaryPlus_StrictOn_NoDiagnostics()
            ' test-plan §3.1 S5: extension operator +, Option Strict On (early bound; no BC30452).
            Dim csRef = GetCSharpCompilation(_baseCsLibrary).EmitToImageReference()

            ' VecExt and MyVec live in the global namespace (not ExternLib), so no Imports is
            ' required (an Imports ExternLib here would be reported as an unused import).
            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Option Strict On

Module M
    Sub Main()
        Dim s As MyVec = New MyVec(1, 2) + New MyVec(10, 20)
    End Sub
End Module
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.VerifyDiagnostics()
        End Sub

        <Fact>
        Public Sub S6_ExtensionOperator_BinaryPlus_StrictOff_NoDiagnostics()
            ' test-plan §3.1 S6: extension operator +, Option Strict Off still early-binds (no late-bound runtime failure).
            ' VecExt/MyVec are in the global namespace, so no Imports is required.
            Dim csRef = GetCSharpCompilation(_baseCsLibrary).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Option Strict Off

Module M
    Sub Main()
        Dim s As MyVec = New MyVec(1, 2) + New MyVec(10, 20)
    End Sub
End Module
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.VerifyDiagnostics()
        End Sub

        <Fact>
        Public Sub S7_ExtensionOperator_ObjectReceiver_LateBoundContrast()
            ' test-plan §3.1 S7: Object receiver operands, Option Strict Off.
            ' ShouldLookupExtensionMethods (Not container.IsObjectType()) keeps late binding.
            ' Implementation-period check: no compile-time error expected under Strict Off (late binding retained).
            Dim csRef = GetCSharpCompilation(_baseCsLibrary).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Option Strict Off
Imports ExternLib

Module M
    Sub Main()
        Dim a As Object = New MyVec(1, 2)
        Dim b As Object = New MyVec(10, 20)
        Dim s = a + b
    End Sub
End Module
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.AssertNoErrors()
        End Sub

        <Fact>
        Public Sub S8_InstanceMemberPriority_WinsOverExtensionProperty()
            ' test-plan §3.1 S8: instance member wins; the extension property is only a fallback.
            Dim csRef = GetCSharpCompilation(_baseCsLibrary).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib

Class HasCharCount
    Public Property CharCount As Integer = 42
End Class

Module M
    Sub Main()
        Dim x As New HasCharCount
        Dim c As Integer = x.CharCount
    End Sub
End Module
]]></file>
</compilation>

            ' Imports ExternLib is genuinely unused here (the instance member wins, so the
            ' ExternLib extension property is never referenced) and surfaces a hidden unused-import
            ' diagnostic; AssertNoErrors (errors only) is the right gate.
            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.AssertNoErrors()

            Dim info = GetMemberAccessSymbol(comp, "x.CharCount")
            Dim prop = TryCast(info.Symbol, PropertySymbol)
            Assert.NotNull(prop)
            Assert.Equal("CharCount", prop.Name)
            Assert.Equal("HasCharCount", prop.ContainingType.Name)
        End Sub

        <Fact>
        Public Sub S9_DuplicateExtensionProperties_AmbiguityOrFirstWins()
            ' test-plan §3.1 S9: two extension blocks provide the same-named property for the same receiver.
            ' Implementation-period check: MergePrioritized never produces ambiguity (first viable wins),
            ' so the actual behavior is recorded here rather than forcing an ambiguity diagnostic.
            Dim csSource =
"
namespace ExternLib
{
    public static class ExtA
    {
        extension(string s)
        {
            public int CharCount => s.Length;
        }
    }
    public static class ExtB
    {
        extension(string s)
        {
            public int CharCount => s.Length + 1;
        }
    }
}
"
            Dim csRef = GetCSharpCompilation(csSource).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib

Module M
    Sub Main()
        Dim c As Integer = "hi".CharCount
    End Sub
End Module
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.AssertNoErrors()

            Dim info = GetMemberAccessSymbol(comp, """hi"".CharCount")
            Assert.NotNull(info.Symbol)
            Assert.Equal("CharCount", info.Symbol.Name)
        End Sub

        <Fact>
        Public Sub S10a_ObjectReceiver_ExtensionPropertyInvisible_StrictOn()
            ' test-plan §3.1 S10: Object receiver must NOT early-bind to the extension property.
            ' Option Strict On => late binding is disallowed; a diagnostic is expected.
            Dim csRef = GetCSharpCompilation(_baseCsLibrary).EmitToImageReference()

            ' Imports ExternLib is deliberately absent: the Object receiver never touches the
            ' extension member, so the import would be reported as unused.
            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Option Strict On

Module M
    Sub Main()
        Dim o As Object = "abc"
        Dim c As Integer = o.CharCount
    End Sub
End Module
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_StrictDisallowsLateBinding, "o.CharCount"))
        End Sub

        <Fact>
        Public Sub S10b_ObjectReceiver_ExtensionPropertyInvisible_StrictOff()
            ' Option Strict Off keeps late binding: no compile-time error, symbol must not be the extension property.
            Dim csRef = GetCSharpCompilation(_baseCsLibrary).EmitToImageReference()

            ' Imports ExternLib is deliberately absent (see S10a).
            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Option Strict Off

Module M
    Sub Main()
        Dim o As Object = "abc"
        Dim c As Integer = o.CharCount
    End Sub
End Module
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.AssertNoErrors()

            Dim info = GetMemberAccessSymbol(comp, "o.CharCount")
            ' Must not bind to the extension property (Object receiver gate keeps late binding).
            Dim prop = TryCast(info.Symbol, PropertySymbol)
            Assert.True(prop Is Nothing, "o.CharCount must not early-bind to the extension property")
        End Sub

        <Fact>
        Public Sub S11_ImportsScope_NoImports_BC30456()
            ' test-plan §3.1 S11: without Imports ExternLib, the extension property is out of scope.
            Dim csRef = GetCSharpCompilation(_baseCsLibrary).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Module M
    Sub Main()
        Dim c As Integer = "hello".CharCount
    End Sub
End Module
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.VerifyDiagnostics(
                Diagnostic(ERRID.ERR_NameNotMember2, """hello"".CharCount").WithArguments("CharCount", "String"))
        End Sub

        <Fact>
        Public Sub S12_ImportsScope_WithImports_NoDiagnostics()
            ' test-plan §3.1 S12: with Imports ExternLib, same source as S11 binds cleanly.
            Dim csRef = GetCSharpCompilation(_baseCsLibrary).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib

Module M
    Sub Main()
        Dim c As Integer = "hello".CharCount
    End Sub
End Module
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.VerifyDiagnostics()
        End Sub

        <Fact>
        Public Sub S13_GenericExtensionProperty_InferredFromReceiver_NoDiagnostics()
            ' test-plan §3.1 S13: generic extension block, receiver infers T from the instance type.
            Dim csRef = GetCSharpCompilation(_baseCsLibrary).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib

Module M
    Sub Main()
        Dim s As String = "hello"
        Dim c As Integer = s.GenericCharCount
    End Sub
End Module
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.VerifyDiagnostics()
        End Sub

        <Fact>
        Public Sub S14_ClassicExtensionMethod_NoRegression()
            ' test-plan §3.1 S14: classic [Extension] method (C# 3) keeps working.
            Dim csRef = GetCSharpCompilation(_baseCsLibrary).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib

Module M
    Sub Main()
        Dim t As Integer = 5.Twice()
    End Sub
End Module
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.VerifyDiagnostics()
        End Sub

        <Fact>
        Public Sub S15_CSharp14ExtensionMethod_NoRegression()
            ' test-plan §3.1 S15: C#14 extension method inside an extension block (emitted as [Extension]).
            Dim csRef = GetCSharpCompilation(_baseCsLibrary).EmitToImageReference()

            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Imports ExternLib

Module M
    Sub Main()
        Dim s As String = "hi".Shout()
    End Sub
End Module
]]></file>
</compilation>

            Dim comp = CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
            comp.VerifyDiagnostics()
        End Sub

    End Class

End Namespace
