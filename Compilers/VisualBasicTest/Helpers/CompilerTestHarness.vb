' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable
Imports System.IO
Imports System.Text.RegularExpressions
Imports System.Xml.Linq
Imports Microsoft.CodeAnalysis.Emit
Imports Microsoft.CodeAnalysis.Test.Utilities
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Xunit

Imports Microsoft.CodeAnalysis

Namespace Basic.Reference.Assemblies
    Public NotInheritable Class Net40
        Public Shared ReadOnly Property References As New Net40References()

        Public NotInheritable Class Net40References
            Public ReadOnly Property mscorlib As MetadataReference = Microsoft.CodeAnalysis.VisualBasic.UnitTests.ReferenceAssemblies.SystemPrivateCoreLib
            Public ReadOnly Property SystemCore As MetadataReference = Microsoft.CodeAnalysis.VisualBasic.UnitTests.ReferenceAssemblies.SystemLinq
        End Class
    End Class

    Public NotInheritable Class Net461
        Public Shared ReadOnly Property ExtraReferences As New Net461ExtraReferences()
        Public Shared ReadOnly Property Resources As New Net461Resources()

        Public NotInheritable Class Net461ExtraReferences
            Public ReadOnly Property SystemValueTuple As MetadataReference = Microsoft.CodeAnalysis.VisualBasic.UnitTests.ReferenceAssemblies.ValueTuple
        End Class

        Public NotInheritable Class Net461Resources
            Public ReadOnly Property mscorlib As Byte() = File.ReadAllBytes(GetType(Object).Assembly.Location)
        End Class
    End Class
End Namespace

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests

    Public MustInherit Class BasicTestBase
        Protected Shared ReadOnly Property MscorlibRef As MetadataReference = ReferenceAssemblies.SystemPrivateCoreLib
        Protected Shared ReadOnly Property SystemRef As MetadataReference = ReferenceAssemblies.SystemRuntime
        Protected Shared ReadOnly Property MsvbRef As MetadataReference = ReferenceAssemblies.MicrosoftVisualBasic
        Protected Shared ReadOnly Property SystemRuntimeFacadeRef As MetadataReference = ReferenceAssemblies.SystemRuntime
        Protected Shared ReadOnly Property ValueTupleRef As MetadataReference = ReferenceAssemblies.ValueTuple
        Protected Shared ReadOnly Property XmlReferences As MetadataReference() = {ReferenceAssemblies.SystemXmlLinq}

        Public Shared Function GetUniqueName() As String
            Return "TestAssembly" & Guid.NewGuid().ToString("N")
        End Function

        Protected Shared Function CreateCompilation(source As String, Optional references As IEnumerable(Of MetadataReference) = Nothing, Optional options As VisualBasicCompilationOptions = Nothing, Optional parseOptions As VisualBasicParseOptions = Nothing, Optional assemblyName As String = Nothing) As VisualBasicCompilation
            Return CompilationUtils.CreateCompilationWithMscorlib40(source, references, options, parseOptions, assemblyName)
        End Function

        Protected Shared Function CreateCompilation(source As XElement, Optional references As IEnumerable(Of MetadataReference) = Nothing, Optional options As VisualBasicCompilationOptions = Nothing, Optional parseOptions As VisualBasicParseOptions = Nothing, Optional assemblyName As String = Nothing) As VisualBasicCompilation
            Return CompilationUtils.CreateCompilationWithMscorlib40(source, references, options, parseOptions, assemblyName)
        End Function

        Protected Shared Function CreateEmptyCompilation(source As String, Optional references As IEnumerable(Of MetadataReference) = Nothing, Optional options As VisualBasicCompilationOptions = Nothing, Optional parseOptions As VisualBasicParseOptions = Nothing, Optional assemblyName As String = Nothing) As VisualBasicCompilation
            Return CompilationUtils.CreateCompilationWithMscorlib40(source, references, options, parseOptions, assemblyName)
        End Function

        Protected Shared Function CreateCompilationWithMscorlib40(source As String, Optional references As IEnumerable(Of MetadataReference) = Nothing, Optional options As VisualBasicCompilationOptions = Nothing, Optional parseOptions As VisualBasicParseOptions = Nothing, Optional assemblyName As String = Nothing) As VisualBasicCompilation
            Return CompilationUtils.CreateCompilationWithMscorlib40(source, references, options, parseOptions, assemblyName)
        End Function

        Protected Shared Function CreateCompilationWithMscorlib40(source As XElement, Optional references As IEnumerable(Of MetadataReference) = Nothing, Optional options As VisualBasicCompilationOptions = Nothing, Optional parseOptions As VisualBasicParseOptions = Nothing, Optional assemblyName As String = Nothing) As VisualBasicCompilation
            Return CompilationUtils.CreateCompilationWithMscorlib40(source, references, options, parseOptions, assemblyName)
        End Function

        Protected Shared Function CreateCompilationWithMscorlib40(source As IEnumerable(Of String), Optional references As IEnumerable(Of MetadataReference) = Nothing, Optional options As VisualBasicCompilationOptions = Nothing, Optional parseOptions As VisualBasicParseOptions = Nothing, Optional assemblyName As String = Nothing) As VisualBasicCompilation
            Return CompilationUtils.CreateCompilationWithMscorlib40(source, references, options, parseOptions, assemblyName)
        End Function

        Protected Shared Function CreateCompilationWithMscorlib40(source As IEnumerable(Of SyntaxTree), Optional references As IEnumerable(Of MetadataReference) = Nothing, Optional options As VisualBasicCompilationOptions = Nothing, Optional parseOptions As VisualBasicParseOptions = Nothing, Optional assemblyName As String = Nothing) As VisualBasicCompilation
            Return CompilationUtils.CreateCompilationWithMscorlib40(source, references, options, assemblyName)
        End Function

        Protected Shared Function CreateCompilationWithMscorlib40AndReferences(source As XElement, references As IEnumerable(Of MetadataReference), Optional options As VisualBasicCompilationOptions = Nothing, Optional parseOptions As VisualBasicParseOptions = Nothing) As VisualBasicCompilation
            Return CompilationUtils.CreateCompilationWithMscorlib40(source, references, options, parseOptions)
        End Function

        Protected Shared Function CreateCompilationWithMscorlib40AndVBRuntime(source As XElement, Optional additionalRefs As MetadataReference() = Nothing, Optional options As VisualBasicCompilationOptions = Nothing, Optional parseOptions As VisualBasicParseOptions = Nothing) As VisualBasicCompilation
            Return CompilationUtils.CreateCompilationWithMscorlib40(source, additionalRefs, options, parseOptions)
        End Function

        Protected Shared Function CreateCompilationWithMscorlib40AndVBRuntime(source As XElement, options As VisualBasicCompilationOptions, Optional parseOptions As VisualBasicParseOptions = Nothing) As VisualBasicCompilation
            Return CompilationUtils.CreateCompilationWithMscorlib40(source, Nothing, options, parseOptions)
        End Function

        Protected Shared Sub CompileAndVerify(compilation As VisualBasicCompilation, Optional expectedOutput As String = Nothing)
            CompilationUtils.CompileAndVerify(compilation, expectedOutput)
        End Sub

#If INCLUDE_CSHARP_SYMBOL_DISPLAY_TESTS Then

        Protected Shared Function CreateCSharpCompilation(source As String, Optional parseOptions As CSharp.CSharpParseOptions = Nothing) As CSharp.CSharpCompilation
            Return CreateCSharpCompilation(GetUniqueName(), source, parseOptions:=parseOptions)
        End Function

        Protected Shared Function CreateCSharpCompilation(source As XCData, Optional parseOptions As CSharp.CSharpParseOptions = Nothing) As CSharp.CSharpCompilation
            Return CreateCSharpCompilation(source.Value, parseOptions)
        End Function

        Protected Shared Function CreateCSharpCompilation(assemblyName As String, code As String, Optional parseOptions As CSharp.CSharpParseOptions = Nothing, Optional referencedAssemblies As IEnumerable(Of MetadataReference) = Nothing) As CSharp.CSharpCompilation
            Dim refs = ReferenceAssemblies.DefaultReferences()
            If referencedAssemblies IsNot Nothing Then
                refs = refs.Concat(referencedAssemblies)
            End If

            Return CSharp.CSharpCompilation.Create(
                assemblyName,
                {CSharp.CSharpSyntaxTree.ParseText(SourceText.From(code), If(parseOptions, CSharp.CSharpParseOptions.Default))},
                refs,
                New CSharp.CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
        End Function

        Protected Shared Function CreateCSharpCompilation(assemblyName As String, source As XElement, Optional parseOptions As CSharp.CSharpParseOptions = Nothing, Optional referencedAssemblies As IEnumerable(Of MetadataReference) = Nothing) As CSharp.CSharpCompilation
            Return CreateCSharpCompilation(assemblyName, source.Value, parseOptions, referencedAssemblies)
        End Function

        Protected Shared Function CreateCSharpCompilation(assemblyName As String, source As XCData, Optional parseOptions As CSharp.CSharpParseOptions = Nothing, Optional referencedAssemblies As IEnumerable(Of MetadataReference) = Nothing) As CSharp.CSharpCompilation
            Return CreateCSharpCompilation(assemblyName, source.Value, parseOptions, referencedAssemblies)
        End Function
#End If
    End Class

    Friend Module CompilationUtils
        Public Function CreateCompilationWithMscorlib40(source As XElement, Optional references As IEnumerable(Of MetadataReference) = Nothing, Optional options As VisualBasicCompilationOptions = Nothing, Optional parseOptions As VisualBasicParseOptions = Nothing, Optional assemblyName As String = Nothing) As VisualBasicCompilation
            Dim files = source.Descendants("file").ToArray()
            If files.Length = 0 Then
                Return CreateCompilationWithMscorlib40(source.Value, references, options, parseOptions, assemblyName)
            End If

            Dim trees = files.Select(Function(file) VisualBasicSyntaxTree.ParseText(SourceText.From(file.Value), If(parseOptions, VisualBasicParseOptions.Default), path:=If(CType(file.@name, String), String.Empty)))
            Return CreateCompilationWithMscorlib40(trees, references, options, assemblyName)
        End Function

        Public Function CreateCompilationWithMscorlib40(source As String, Optional references As IEnumerable(Of MetadataReference) = Nothing, Optional options As VisualBasicCompilationOptions = Nothing, Optional parseOptions As VisualBasicParseOptions = Nothing, Optional assemblyName As String = Nothing) As VisualBasicCompilation
            Dim tree = VisualBasicSyntaxTree.ParseText(SourceText.From(source), If(parseOptions, VisualBasicParseOptions.Default))
            Return CreateCompilationWithMscorlib40({tree}, references, options, assemblyName)
        End Function

        Public Function CreateCompilationWithMscorlib40(source As IEnumerable(Of String), Optional references As IEnumerable(Of MetadataReference) = Nothing, Optional options As VisualBasicCompilationOptions = Nothing, Optional parseOptions As VisualBasicParseOptions = Nothing, Optional assemblyName As String = Nothing) As VisualBasicCompilation
            Dim trees = source.Select(Function(text) VisualBasicSyntaxTree.ParseText(SourceText.From(text), If(parseOptions, VisualBasicParseOptions.Default)))
            Return CreateCompilationWithMscorlib40(trees, references, options, assemblyName)
        End Function

        Public Function CreateCompilationWithMscorlib40(source As IEnumerable(Of SyntaxTree), Optional references As IEnumerable(Of MetadataReference) = Nothing, Optional options As VisualBasicCompilationOptions = Nothing, Optional assemblyName As String = Nothing) As VisualBasicCompilation
            Dim allReferences As IEnumerable(Of MetadataReference) = {Microsoft.CodeAnalysis.VisualBasic.UnitTests.ReferenceAssemblies.SystemPrivateCoreLib}
            If references IsNot Nothing Then
                allReferences = allReferences.Concat(references)
            End If

            Return VisualBasicCompilation.Create(
                If(assemblyName, BasicTestBase.GetUniqueName()),
                source,
                allReferences,
                If(options, New VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary)))
        End Function

        Public Function CreateCompilationWithMscorlib40AndReferences(source As XElement, references As IEnumerable(Of MetadataReference), Optional options As VisualBasicCompilationOptions = Nothing, Optional parseOptions As VisualBasicParseOptions = Nothing) As VisualBasicCompilation
            Return CreateCompilationWithMscorlib40(source, references, options, parseOptions)
        End Function

        Public Function CreateCompilationWithMscorlib40AndVBRuntimeAndReferences(source As XElement, Optional references As IEnumerable(Of MetadataReference) = Nothing, Optional options As VisualBasicCompilationOptions = Nothing, Optional parseOptions As VisualBasicParseOptions = Nothing) As VisualBasicCompilation
            Return CreateCompilationWithMscorlib40(source, references, options, parseOptions)
        End Function

        Public Function CreateCompilationWithMscorlib40AndVBRuntime(source As XElement, options As VisualBasicCompilationOptions, Optional parseOptions As VisualBasicParseOptions = Nothing) As VisualBasicCompilation
            Return CreateCompilationWithMscorlib40(source, Nothing, options, parseOptions)
        End Function

        Public Function CreateCompilationWithMscorlib40AndVBRuntime(source As XElement, Optional references As IEnumerable(Of MetadataReference) = Nothing, Optional options As VisualBasicCompilationOptions = Nothing, Optional parseOptions As VisualBasicParseOptions = Nothing) As VisualBasicCompilation
            Return CreateCompilationWithMscorlib40(source, references, options, parseOptions)
        End Function

        Public Sub AssertTheseParseDiagnostics(compilation As VisualBasicCompilation, expected As XElement, Optional suppressInfos As Boolean = True)
            AssertTheseDiagnostics(compilation.GetParseDiagnostics(), expected.Value, suppressInfos)
        End Sub

        Public Sub AssertTheseDiagnostics(compilation As Compilation, expected As XElement, Optional suppressInfos As Boolean = True)
            AssertTheseDiagnostics(DirectCast(compilation, VisualBasicCompilation).GetDiagnostics(), expected.Value, suppressInfos)
        End Sub

        Public Sub AssertTheseDiagnostics(compilation As Compilation, expected As XCData, Optional suppressInfos As Boolean = True)
            AssertTheseDiagnostics(DirectCast(compilation, VisualBasicCompilation).GetDiagnostics(), expected.Value, suppressInfos)
        End Sub

        Public Sub AssertTheseDiagnostics(diagnostics As ImmutableArray(Of Diagnostic), expected As XElement, Optional suppressInfos As Boolean = True)
            AssertTheseDiagnostics(diagnostics, expected.Value, suppressInfos)
        End Sub

        Public Sub AssertTheseDiagnostics(diagnostics As IEnumerable(Of Diagnostic), expectedText As String, Optional suppressInfos As Boolean = True)
            Dim expectedIds = Regex.Matches(expectedText, "BC\d{5}").Cast(Of Match)().Select(Function(match) match.Value).OrderBy(Function(id) id).ToArray()
            Dim filteredDiagnostics = If(suppressInfos, diagnostics.Where(Function(diagnostic) diagnostic.Severity <> DiagnosticSeverity.Hidden AndAlso diagnostic.Severity <> DiagnosticSeverity.Info), diagnostics)
            Dim actualIds = filteredDiagnostics.Select(Function(diagnostic) diagnostic.Id).OrderBy(Function(id) id).ToArray()
            Assert.Equal(expectedIds, actualIds)
        End Sub

        Public Sub AssertNoErrors(compilation As VisualBasicCompilation)
            compilation.AssertNoErrors()
        End Sub

        Public Sub CompileAndVerify(compilation As VisualBasicCompilation, Optional expectedOutput As String = Nothing)
            Using peStream As New MemoryStream()
                Dim emitResult = compilation.Emit(peStream)
                Assert.True(emitResult.Success, String.Join(Environment.NewLine, emitResult.Diagnostics.Select(Function(diagnostic) diagnostic.ToString())))

                ' Parser tests use CompileAndVerify as a compilation gate; running emitted IL
                ' in-process would require dynamic assembly loading under local application control.
            End Using
        End Sub

    End Module

    Friend Module SymbolTestExtensions
        <Runtime.CompilerServices.Extension>
        Public Function GetMember(compilation As Compilation, qualifiedName As String) As Symbol
            Return DirectCast(compilation, VisualBasicCompilation).GlobalNamespace.GetMember(qualifiedName)
        End Function

        <Runtime.CompilerServices.Extension>
        Public Function GetMember(Of T As Symbol)(compilation As Compilation, qualifiedName As String) As T
            Return DirectCast(DirectCast(compilation, VisualBasicCompilation).GlobalNamespace.GetMember(qualifiedName), T)
        End Function

        <Runtime.CompilerServices.Extension>
        Public Function GetMember(container As NamespaceOrTypeSymbol, qualifiedName As String) As Symbol
            Dim parts = qualifiedName.Split("."c)
            Dim current As NamespaceOrTypeSymbol = container

            For i = 0 To parts.Length - 2
                current = DirectCast(current.GetMembers(parts(i)).Single(), NamespaceOrTypeSymbol)
            Next

            Return current.GetMembers(parts.Last()).Single()
        End Function

        <Runtime.CompilerServices.Extension>
        Public Function GetMember(Of T As Symbol)(container As NamespaceOrTypeSymbol, qualifiedName As String) As T
            Return DirectCast(container.GetMember(qualifiedName), T)
        End Function

        <Runtime.CompilerServices.Extension>
        Public Function GetTypeMember(container As NamespaceOrTypeSymbol, name As String) As NamedTypeSymbol
            Return container.GetTypeMembers(name).Single()
        End Function

        <Runtime.CompilerServices.Extension>
        Public Function ReduceExtensionMethod(method As MethodSymbol, instanceType As TypeSymbol) As MethodSymbol
            Return method.ReduceExtensionMethod(instanceType, CompoundUseSiteInfo(Of AssemblySymbol).Discarded, LanguageVersion.Latest)
        End Function

        <Runtime.CompilerServices.Extension>
        Public Function BaseType(symbol As TypeSymbol) As NamedTypeSymbol
            Return symbol.BaseTypeNoUseSiteDiagnostics
        End Function

        <Runtime.CompilerServices.Extension>
        Public Function ToTestDisplayString(symbol As Symbol) As String
            Dim format = New SymbolDisplayFormat(
                typeQualificationStyle:=SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                memberOptions:=SymbolDisplayMemberOptions.IncludeContainingType Or SymbolDisplayMemberOptions.IncludeParameters,
                kindOptions:=SymbolDisplayKindOptions.IncludeMemberKeyword,
                parameterOptions:=SymbolDisplayParameterOptions.IncludeName Or SymbolDisplayParameterOptions.IncludeType Or SymbolDisplayParameterOptions.IncludeDefaultValue Or SymbolDisplayParameterOptions.IncludeOptionalBrackets)

            Return symbol.ToDisplayString(format)
        End Function

        <Runtime.CompilerServices.Extension>
        Public Function ToTestDisplayString(symbol As ISymbol) As String
            Return symbol.ToDisplayString()
        End Function
    End Module

    Friend Module CompilationExtensions
        <Runtime.CompilerServices.Extension>
        Public Function EmitToImageReference(compilation As Compilation) As MetadataReference
            Using peStream As New MemoryStream()
                Dim result = compilation.Emit(peStream)
                Assert.True(result.Success, String.Join(Environment.NewLine, result.Diagnostics.Select(Function(d) d.ToString())))
                peStream.Position = 0
                Return MetadataReference.CreateFromImage(peStream.ToArray().ToImmutableArray())
            End Using
        End Function

        <Runtime.CompilerServices.Extension>
        Public Function EmitToArray(compilation As Compilation) As ImmutableArray(Of Byte)
            Using peStream As New MemoryStream()
                Dim result = compilation.Emit(peStream)
                Assert.True(result.Success, String.Join(Environment.NewLine, result.Diagnostics.Select(Function(d) d.ToString())))
                Return peStream.ToArray().ToImmutableArray()
            End Using
        End Function

        <Runtime.CompilerServices.Extension>
        Public Function VerifyDiagnostics(Of T As Compilation)(compilation As T, ParamArray expected As DiagnosticDescription()) As T
            compilation.GetDiagnostics().Verify(expected)
            Return compilation
        End Function

        <Runtime.CompilerServices.Extension>
        Public Function AssertNoErrors(Of T As Compilation)(compilation As T) As T
            Dim diagnostics = compilation.GetDiagnostics().Where(Function(diagnostic) diagnostic.Severity = DiagnosticSeverity.Error).ToArray()
            Assert.Empty(diagnostics)
            Return compilation
        End Function

        <Runtime.CompilerServices.Extension>
        Public Function AssertNoDiagnostics(Of T As Compilation)(compilation As T) As T
            Assert.Empty(compilation.GetDiagnostics())
            Return compilation
        End Function

        <Runtime.CompilerServices.Extension>
        Public Function AssertTheseDiagnostics(Of T As Compilation)(compilation As T, expected As XElement, Optional suppressInfos As Boolean = True) As T
            CompilationUtils.AssertTheseDiagnostics(compilation, expected, suppressInfos)
            Return compilation
        End Function
    End Module

    Friend Module DiagnosticDescriptionFactory
        Public Function Diagnostic(id As Integer, squiggledText As String) As Microsoft.CodeAnalysis.Test.Utilities.DiagnosticDescription
            Return Roslyn.Test.Utilities.TestHelpers.Diagnostic("CS" & id.ToString("0000", Globalization.CultureInfo.InvariantCulture), squiggledText)
        End Function
    End Module

    Friend NotInheritable Class MetadataReferenceComparer
        Implements IEqualityComparer(Of MetadataReference)

        Public Shared ReadOnly Instance As New MetadataReferenceComparer()

        Private Sub New()
        End Sub

        Public Overloads Function Equals(x As MetadataReference, y As MetadataReference) As Boolean Implements IEqualityComparer(Of MetadataReference).Equals
            Return String.Equals(x.Display, y.Display, StringComparison.OrdinalIgnoreCase)
        End Function

        Public Overloads Function GetHashCode(obj As MetadataReference) As Integer Implements IEqualityComparer(Of MetadataReference).GetHashCode
            Return StringComparer.OrdinalIgnoreCase.GetHashCode(If(obj.Display, String.Empty))
        End Function
    End Class

    Friend Module ReferenceAssemblies
        Public ReadOnly Property SystemPrivateCoreLib As MetadataReference = MetadataReference.CreateFromFile(GetType(Object).Assembly.Location)
        Public ReadOnly Property SystemRuntime As MetadataReference = MetadataReference.CreateFromFile(GetType(System.Runtime.GCSettings).Assembly.Location)
        Public ReadOnly Property SystemLinq As MetadataReference = MetadataReference.CreateFromFile(GetType(Enumerable).Assembly.Location)
        Public ReadOnly Property SystemConsole As MetadataReference = MetadataReference.CreateFromFile(GetType(Console).Assembly.Location)
        Public ReadOnly Property SystemXmlLinq As MetadataReference = MetadataReference.CreateFromFile(GetType(XDocument).Assembly.Location)
        Public ReadOnly Property MicrosoftVisualBasic As MetadataReference = MetadataReference.CreateFromFile(GetType(Microsoft.VisualBasic.Constants).Assembly.Location)
        Public ReadOnly Property ValueTuple As MetadataReference = MetadataReference.CreateFromFile(GetType(ValueTuple).Assembly.Location)

        Public Function DefaultReferences() As IEnumerable(Of MetadataReference)
            Dim trustedPlatformAssemblies = DirectCast(AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"), String)
            If trustedPlatformAssemblies IsNot Nothing Then
                Return trustedPlatformAssemblies.
                    Split(Path.PathSeparator).
                    Select(Function(path) MetadataReference.CreateFromFile(path)).
                    Distinct(MetadataReferenceComparer.Instance)
            End If

            Return {
                SystemPrivateCoreLib,
                SystemRuntime,
                SystemLinq,
                SystemConsole,
                SystemXmlLinq,
                MicrosoftVisualBasic,
                ValueTuple
            }
        End Function
    End Module
End Namespace
