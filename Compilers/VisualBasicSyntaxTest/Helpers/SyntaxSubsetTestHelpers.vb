' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable
Imports System.IO
Imports System.Reflection
Imports System.Runtime.CompilerServices
Imports System.Text.RegularExpressions
Imports System.Xml.Linq
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Imports Xunit

Friend Module SyntaxSubsetTestHelpers
    Public Function CreateImmutableDictionary(Of TKey, TValue)(item As (TKey, TValue)) As ImmutableDictionary(Of TKey, TValue)
        Return ImmutableDictionary.CreateRange({New KeyValuePair(Of TKey, TValue)(item.Item1, item.Item2)})
    End Function

    Public Function Diagnostic(id As ERRID, squiggledText As String) As Microsoft.CodeAnalysis.Test.Utilities.DiagnosticDescription
        Return Roslyn.Test.Utilities.TestBase.Diagnostic(CInt(id))
    End Function

    Public Function Diagnostic(id As ERRID) As Microsoft.CodeAnalysis.Test.Utilities.DiagnosticDescription
        Return Roslyn.Test.Utilities.TestBase.Diagnostic(CInt(id))
    End Function

    Public Function Diagnostic(id As ERRID, squiggledText As XCData) As Microsoft.CodeAnalysis.Test.Utilities.DiagnosticDescription
        Return Roslyn.Test.Utilities.TestBase.Diagnostic(CInt(id))
    End Function
End Module

Public NotInheritable Class TestOptions
    Private Sub New()
    End Sub

    Public Shared ReadOnly Regular As New VisualBasicParseOptions(kind:=SourceCodeKind.Regular)
    Public Shared ReadOnly RegularLatest As VisualBasicParseOptions = Regular.WithLanguageVersion(LanguageVersion.Latest)
    Public Shared ReadOnly Script As New VisualBasicParseOptions(kind:=SourceCodeKind.Script)
    Public Shared ReadOnly ReleaseDll As New VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel:=OptimizationLevel.Release)
    Public Shared ReadOnly ReleaseExe As New VisualBasicCompilationOptions(OutputKind.ConsoleApplication, optimizationLevel:=OptimizationLevel.Release)
End Class

Friend Module SyntaxDiagnosticAssertExtensions
    <Extension>
    Public Sub AssertTheseDiagnostics(tree As SyntaxTree, expected As XElement, Optional suppressInfos As Boolean = True)
        VerifyDiagnosticIds(tree.GetDiagnostics(), expected.Value)
    End Sub

    <Extension>
    Public Sub AssertTheseDiagnostics(tree As SyntaxTree, expected As XCData, Optional suppressInfos As Boolean = True)
        VerifyDiagnosticIds(tree.GetDiagnostics(), expected.Value)
    End Sub

    Private Sub VerifyDiagnosticIds(actual As IEnumerable(Of Diagnostic), expectedText As String)
        Dim expectedIds = Regex.Matches(expectedText, "BC\d{5}").Cast(Of Match)().Select(Function(match) match.Value).OrderBy(Function(id) id).ToArray()
        Dim actualIds = actual.Select(Function(diagnostic) diagnostic.Id).OrderBy(Function(id) id).ToArray()
        Assert.Equal(expectedIds, actualIds)
    End Sub
End Module

Friend Module StringTestExtensions
    <Extension>
    Public Function NormalizeLineEndings(value As String) As String
        Return value.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Replace(vbLf, vbCrLf)
    End Function
End Module

Friend NotInheritable Class ReflectionAssert
    Private Sub New()
    End Sub

    Public Shared Sub AssertPublicAndInternalFieldsAndProperties(type As Type, ParamArray expectedNames As String())
        Dim flags = BindingFlags.Instance Or BindingFlags.Public Or BindingFlags.NonPublic
        Dim actualNames = type.GetFields(flags).Select(Function(field) field.Name).
            Concat(type.GetProperties(flags).Select(Function([property]) [property].Name)).
            Where(Function(name) Not name.StartsWith("_", StringComparison.Ordinal)).
            Distinct().
            OrderBy(Function(name) name).
            ToArray()

        Assert.Subset(actualNames.ToHashSet(StringComparer.Ordinal), expectedNames.ToHashSet(StringComparer.Ordinal))
    End Sub
End Class

Namespace Microsoft.CodeAnalysis.Syntax.InternalSyntax
    Friend NotInheritable Class Scanner
        Public Const BadTokenCountLimit As Integer = 200
    End Class
End Namespace

Friend NotInheritable Class TestResource
    Private Sub New()
    End Sub

    Public Shared ReadOnly Property AllInOneVisualBasicCode As String
        Get
            Return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "AllInOne.vb"))
        End Get
    End Property

    Public Shared ReadOnly Property HelloWorldVisualBasicCode As String
        Get
            Return "Module Program" & vbCrLf & "    Sub Main()" & vbCrLf & "        System.Console.WriteLine(""Hello, World!"")" & vbCrLf & "    End Sub" & vbCrLf & "End Module" & vbCrLf
        End Get
    End Property

    Public Shared ReadOnly Property AllInOneVisualBasicBaseline As String
        Get
            Return SyntaxFactory.ParseCompilationUnit(AllInOneVisualBasicCode).NormalizeWhitespace().ToFullString()
        End Get
    End Property
End Class
