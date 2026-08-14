' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Test.Utilities
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Roslyn.Test.Utilities

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests

    Public Class ByRefLikeDisplayTests
        Inherits BasicTestBase

        Private Shared Function RefLikeDisplayFormat() As SymbolDisplayFormat
            Return New SymbolDisplayFormat(
                typeQualificationStyle:=SymbolDisplayTypeQualificationStyle.NameOnly,
                genericsOptions:=SymbolDisplayGenericsOptions.IncludeTypeParameters,
                kindOptions:=SymbolDisplayKindOptions.IncludeTypeKeyword,
                miscellaneousOptions:=SymbolDisplayMiscellaneousOptions.UseSpecialTypes)
        End Function

        <Fact>
        Public Sub D1_SpanOfIntegerDisplaysByRefLikeStructure()
            Dim comp = CreateCompilation("", targetFramework:=TargetFramework.NetLatest)
            Dim spanDef = comp.GetTypeByMetadataName("System.Span`1")
            Dim span = DirectCast(spanDef.Construct(comp.GetSpecialType(CodeAnalysis.SpecialType.System_Int32)), INamedTypeSymbol)
            Assert.True(span.IsRefLikeType)

            Dim text = SymbolDisplay.ToDisplayString(span, RefLikeDisplayFormat())
            AssertEx.Equal("ByRef Like Structure Span(Of Integer)", text)
        End Sub

        <Fact>
        Public Sub D2_RegularStructureHasNoByRefLikePrefix()
            Dim comp = CreateCompilation("Structure Point
End Structure")
            Dim point = comp.GetTypeByMetadataName("Point")
            Assert.False(point.IsRefLikeType)

            Dim text = SymbolDisplay.ToDisplayString(point, RefLikeDisplayFormat())
            AssertEx.Equal("Structure Point", text)
            Assert.DoesNotContain("ByRef Like", text)
        End Sub

        <Fact>
        Public Sub D3_ByRefLikeIsNotDeclarableSyntax()
            Dim comp = CreateCompilation("ByRef Like Structure X")
            Assert.True(comp.GetParseDiagnostics().Any(Function(d) d.Severity = DiagnosticSeverity.Error))
        End Sub

        <Fact>
        Public Sub D4_TypedReferenceHasNoByRefLikePrefix()
            ' .NET Framework-era mscorlib marks TypedReference as a plain struct; the .NET Core
            ' reference set (NetLatest) converts it to a ref struct, so use mscorlib to observe
            ' the legacy RestrictedType shape (IsRefLikeType=False).
            Dim comp = CreateCompilation("", targetFramework:=TargetFramework.Mscorlib40)
            Dim tr = comp.GetTypeByMetadataName("System.TypedReference")
            Assert.False(tr.IsRefLikeType)

            Dim text = SymbolDisplay.ToDisplayString(tr, RefLikeDisplayFormat())
            AssertEx.Equal("Structure TypedReference", text)
            Assert.DoesNotContain("ByRef Like", text)
        End Sub

    End Class

End Namespace
