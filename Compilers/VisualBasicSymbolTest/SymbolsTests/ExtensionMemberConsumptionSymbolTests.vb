' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable
Imports Microsoft.CodeAnalysis.CSharp
Imports Microsoft.CodeAnalysis.Test.Utilities
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols.Metadata.PE
Imports Roslyn.Test.Utilities

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests

    ''' <summary>
    ''' Part A L3 symbol/metadata layer tests for C# 14 extension member consumption,
    ''' per InternalDevDocs\tasks\consume-csharp-extension-and-interface-shared\test-plan.md §5 (D1-D6).
    ''' The C# test library is built in-memory via CreateCSharpCompilation (no external DLL, no side effects).
    ''' </summary>
    <CompilerTrait(CompilerFeature.DefaultInterfaceImplementation)>
    Public Class ExtensionMemberConsumptionSymbolTests
        Inherits BasicTestBase

        Private Const _supportingFramework As TargetFramework = TargetFramework.NetLatest

        Private Function GetCSharpCompilation(csSource As String) As CSharpCompilation
            Return CreateCSharpCompilation(
                csSource,
                parseOptions:=CSharpParseOptions.Default.WithLanguageVersion(CSharp.LanguageVersion.Preview),
                referencedAssemblies:=TargetFrameworkUtil.GetReferences(_supportingFramework))
        End Function

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
public static class VecExt
{
    extension(MyVec v)
    {
        public static MyVec operator +(MyVec a, MyVec b) => new MyVec(a.X + b.X, a.Y + b.Y);
    }
}
public struct MyVec { public int X; public int Y; public MyVec(int x, int y) { X = x; Y = y; } }
"

        Private Shared Function GetGroupingType(container As NamedTypeSymbol) As NamedTypeSymbol
            For Each nested In container.GetTypeMembers()
                If nested.IsExtensionGroupingType Then
                    Return nested
                End If
            Next
            Return Nothing
        End Function

        Private Shared Function CreateConsumerCompilation(csRef As MetadataReference) As VisualBasicCompilation
            Dim source =
<compilation>
    <file name="a.vb"><![CDATA[
Module M
End Module
]]></file>
</compilation>
            Return CreateCompilation(source, references:={csRef}, targetFramework:=_supportingFramework)
        End Function

        <Fact>
        Public Sub D1_IsExtensionMember_GroupingPropertyAndMethod_True_OrdinaryMember_False()
            Dim csRef = GetCSharpCompilation(_baseCsLibrary).EmitToImageReference()
            Dim comp = CreateConsumerCompilation(csRef)

            Dim newExt = comp.GetMember(Of NamedTypeSymbol)("ExternLib.NewExt")
            Dim grouping = GetGroupingType(newExt)
            Assert.NotNull(grouping)
            Assert.True(grouping.IsExtensionGroupingType)

            Dim charCount = grouping.GetMembers("CharCount").OfType(Of PropertySymbol).Single()
            Assert.True(charCount.IsExtensionMember)

            Dim shout = grouping.GetMembers("Shout").OfType(Of MethodSymbol).Single()
            Assert.True(shout.IsExtensionMember)

            ' Ordinary member (MyNum.Zero static property) is NOT an extension member.
            Dim zero = comp.GetMember(Of PropertySymbol)("ExternLib.MyNum.Zero")
            Assert.False(zero.IsExtensionMember)
        End Sub

        <Fact>
        Public Sub D2_HasExtensionMarkerAttribute_RawRead()
            Dim csRef = GetCSharpCompilation(_baseCsLibrary).EmitToImageReference()
            Dim comp = CreateConsumerCompilation(csRef)

            Dim newExt = comp.GetMember(Of NamedTypeSymbol)("ExternLib.NewExt")
            Dim grouping = GetGroupingType(newExt)
            Dim charCount = grouping.GetMembers("CharCount").OfType(Of PropertySymbol).Single()
            Dim shout = grouping.GetMembers("Shout").OfType(Of MethodSymbol).Single()

            ' Raw metadata read: the grouping-type property/method carries [ExtensionMarker].
            Dim peProp = DirectCast(charCount, PEPropertySymbol)
            Dim peModule = DirectCast(peProp.ContainingType, PENamedTypeSymbol).ContainingPEModule.Module
            Dim propMarker As String = Nothing
            Assert.True(peModule.HasExtensionMarkerAttribute(peProp.Handle, propMarker))
            Assert.False(String.IsNullOrEmpty(propMarker))

            Dim peMethod = DirectCast(shout, PEMethodSymbol)
            Dim methodMarker As String = Nothing
            Assert.True(peModule.HasExtensionMarkerAttribute(peMethod.Handle, methodMarker))
            Assert.False(String.IsNullOrEmpty(methodMarker))

            ' Non-extension member (MyNum.Zero) returns False.
            Dim zero = DirectCast(comp.GetMember(Of PropertySymbol)("ExternLib.MyNum.Zero"), PEPropertySymbol)
            Dim zeroMarker As String = Nothing
            Assert.False(peModule.HasExtensionMarkerAttribute(zero.Handle, zeroMarker))
        End Sub

        <Fact>
        Public Sub D3_ReducedExtensionProperty_ReductionShape()
            Dim csRef = GetCSharpCompilation(_baseCsLibrary).EmitToImageReference()
            Dim comp = CreateConsumerCompilation(csRef)

            Dim newExt = comp.GetMember(Of NamedTypeSymbol)("ExternLib.NewExt")
            Dim grouping = GetGroupingType(newExt)
            Dim charCount = grouping.GetMembers("CharCount").OfType(Of PropertySymbol).Single()

            Dim instanceType = comp.GetMember(Of NamedTypeSymbol)("System.String")
            Dim useSiteInfo = CompoundUseSiteInfo(Of AssemblySymbol).Discarded
            Dim reduced = TryCast(ReducedExtensionMemberReducer.ReduceExtensionMember(instanceType, charCount, useSiteInfo, LanguageVersion.Latest, proximity:=0), PropertySymbol)
            Assert.NotNull(reduced)
            Assert.Equal("CharCount", reduced.Name)
            Assert.Equal(0, reduced.Parameters.Length)
            Assert.False(reduced.IsShared)
            Assert.Equal(SpecialType.System_String, reduced.ReceiverType.SpecialType)
            Assert.Same(charCount, reduced.ReducedFrom)
            ' Accessors go through ReduceAccessorIfAny: GetMethod is a reduced accessor whose
            ' AssociatedSymbol is the reduced property; setter is Nothing for a get-only property.
            Assert.NotNull(reduced.GetMethod)
            Assert.Same(reduced, reduced.GetMethod.AssociatedSymbol)
            Assert.True(reduced.SetMethod Is Nothing)
        End Sub

        <Fact>
        Public Sub D4_ReducedExtensionProperty_Display()
            Dim csRef = GetCSharpCompilation(_baseCsLibrary).EmitToImageReference()
            Dim comp = CreateConsumerCompilation(csRef)

            Dim newExt = comp.GetMember(Of NamedTypeSymbol)("ExternLib.NewExt")
            Dim grouping = GetGroupingType(newExt)
            Dim charCount = grouping.GetMembers("CharCount").OfType(Of PropertySymbol).Single()

            Dim instanceType = comp.GetMember(Of NamedTypeSymbol)("System.String")
            Dim useSiteInfo = CompoundUseSiteInfo(Of AssemblySymbol).Discarded
            Dim reduced = TryCast(ReducedExtensionMemberReducer.ReduceExtensionMember(instanceType, charCount, useSiteInfo, LanguageVersion.Latest, proximity:=0), PropertySymbol)

            ' test-plan §5 D4 assumed "Property CharCount As System.Int32". Actual behavior: the
            ' reduced property's ContainingType is the C# 14 grouping type, so MinimallyQualifiedFormat
            ' renders the mangled <G>$ name and uses the VB keyword alias for the type:
            '   "Property <G>$34505F560D9EACF86A87F3ED1F85E448.CharCount As Integer"
            Dim display = reduced.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
            Assert.StartsWith("Property <G>$", display)
            Assert.Contains(".CharCount As Integer", display)
        End Sub

        <Fact>
        Public Sub D5_ReducedExtensionOperator_Display()
            Dim csRef = GetCSharpCompilation(_baseCsLibrary).EmitToImageReference()
            Dim comp = CreateConsumerCompilation(csRef)

            Dim vecExt = comp.GetMember(Of NamedTypeSymbol)("VecExt")
            Dim grouping = GetGroupingType(vecExt)
            Assert.NotNull(grouping)
            Dim opAddition = grouping.GetMembers("op_Addition").OfType(Of MethodSymbol).Single()
            Assert.True(opAddition.IsExtensionMember)

            Dim instanceType = comp.GetMember(Of NamedTypeSymbol)("MyVec")
            Dim useSiteInfo = CompoundUseSiteInfo(Of AssemblySymbol).Discarded
            Dim reduced = TryCast(ReducedExtensionMemberReducer.ReduceExtensionMember(instanceType, opAddition, useSiteInfo, LanguageVersion.Latest, proximity:=0), MethodSymbol)
            Assert.NotNull(reduced)
            Assert.Equal(MethodKind.UserDefinedOperator, reduced.MethodKind)
            Assert.Equal("op_Addition", reduced.Name)

            ' The operator keeps both operands (receiver is the first parameter), so Parameters = 2.
            Assert.Equal(2, reduced.Parameters.Length)
            ' test-plan §5 D5 assumed an "Operator +(MyVec, MyVec)" form. Actual behavior:
            ' MinimallyQualifiedFormat includes the containing type (the [Extension] container,
            ' not the grouping type) and parameter names:
            '   "Operator VecExt.+(a As MyVec, b As MyVec) As MyVec"
            Dim display = reduced.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
            Assert.Equal("Operator VecExt.+(a As MyVec, b As MyVec) As MyVec", display)
        End Sub

        <Fact>
        Public Sub D6_OrdinarySharedMembers_NotMislabeled()
            Dim csRef = GetCSharpCompilation(_baseCsLibrary).EmitToImageReference()
            Dim comp = CreateConsumerCompilation(csRef)

            Dim zero = comp.GetMember(Of PropertySymbol)("ExternLib.MyNum.Zero")
            Assert.False(zero.IsExtensionMember)

            Dim add = comp.GetMember(Of MethodSymbol)("ExternLib.MyNum.Add")
            Assert.False(add.IsExtensionMember)
        End Sub

        <Fact>
        Public Sub D7_NameableExtensionMember_ExcludesAccessors()
            Dim csSrc = "
using System;
namespace ExternLib
{
    public static class NewExt
    {
        extension(string s)
        {
            public int CharCount { get { return s.Length; } }
            public string Shout() => s.ToUpperInvariant();
        }
    }
    public static class NewExt2
    {
        extension(string s)
        {
            public int Length2 { get { return s.Length; } set { } }
        }
    }
}"
            Dim csRef = GetCSharpCompilation(csSrc).EmitToImageReference()
            Dim comp = CreateConsumerCompilation(csRef)

            ' Getter-only extension property: get_ accessor is not nameable; the property is.
            Dim newExt = comp.GetMember(Of NamedTypeSymbol)("ExternLib.NewExt")
            Dim grouping = GetGroupingType(newExt)
            Dim getCharCount = grouping.GetMembers("get_CharCount").OfType(Of MethodSymbol).SingleOrDefault()
            Assert.NotNull(getCharCount)
            Assert.True(getCharCount.IsAccessor())
            Assert.False(NamedTypeSymbol.IsNameableExtensionMember(getCharCount))

            Dim charCount = grouping.GetMembers("CharCount").OfType(Of PropertySymbol).Single()
            Assert.True(NamedTypeSymbol.IsNameableExtensionMember(charCount))

            Dim shout = grouping.GetMembers("Shout").OfType(Of MethodSymbol).Single()
            Assert.True(NamedTypeSymbol.IsNameableExtensionMember(shout))

            ' Read-write extension property: set_ accessor is also not nameable.
            Dim newExt2 = comp.GetMember(Of NamedTypeSymbol)("ExternLib.NewExt2")
            Dim grouping2 = GetGroupingType(newExt2)
            Dim setLength2 = grouping2.GetMembers("set_Length2").OfType(Of MethodSymbol).SingleOrDefault()
            Assert.NotNull(setLength2)
            Assert.True(setLength2.IsAccessor())
            Assert.False(NamedTypeSymbol.IsNameableExtensionMember(setLength2))

            Dim length2 = grouping2.GetMembers("Length2").OfType(Of PropertySymbol).Single()
            Assert.True(NamedTypeSymbol.IsNameableExtensionMember(length2))
        End Sub

    End Class

End Namespace
