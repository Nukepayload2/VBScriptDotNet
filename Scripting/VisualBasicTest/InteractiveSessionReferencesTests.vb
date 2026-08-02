' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.IO
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Xunit

Public Class InteractiveSessionReferencesTests

    Private Shared Function CreateIsolatedTempDirectory() As String
        Dim directory = Path.Combine(AppContext.BaseDirectory, "TestTemp", Guid.NewGuid().ToString("N"))
        System.IO.Directory.CreateDirectory(directory)
        Return directory
    End Function

    Private Shared Function CreateLibraryAssembly(directory As String, assemblyName As String, source As String, Optional references As IEnumerable(Of MetadataReference) = Nothing) As String
        Dim assemblyPath = Path.Combine(directory, assemblyName + ".dll")
        Dim syntaxTree = SyntaxFactory.ParseSyntaxTree(source)
        Dim metadataReferences = New List(Of MetadataReference) From {
            MetadataReference.CreateFromFile(GetType(Object).Assembly.Location),
            MetadataReference.CreateFromFile(GetType(Strings).Assembly.Location)
        }

        If references IsNot Nothing Then
            metadataReferences.AddRange(references)
        End If

        Dim compilation = VisualBasicCompilation.Create(
            assemblyName,
            {syntaxTree},
            metadataReferences,
            New VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, rootNamespace:=""))

        Dim result = compilation.Emit(assemblyPath)
        Assert.True(result.Success, String.Join(Environment.NewLine, result.Diagnostics))
        Return assemblyPath
    End Function

    <Fact>
    Public Async Function ReferenceDirective_IsVisibleInSubsequentSubmissions() As Task
        Dim directory = CreateIsolatedTempDirectory()
        Dim libraryPath = CreateLibraryAssembly(directory, "InteractiveReferenceLibrary", "
Public Class C
    Public ReadOnly X As Integer = 1
End Class")

        Dim script = VisualBasicScript.Create("
#r """ & libraryPath & """
Function F(c As C) As Integer
    Return c.X
End Function").ContinueWith("? F(New C())")

        Assert.DoesNotContain(script.GetCompilation().GetDiagnostics(), Function(d) d.Severity = DiagnosticSeverity.Error)
        Assert.Equal(1, Await script.EvaluateAsync())
    End Function

    <Fact>
    Public Async Function ReferenceDirective_DuplicateReferenceIsAllowed() As Task
        Dim directory = CreateIsolatedTempDirectory()
        Dim libraryPath = CreateLibraryAssembly(directory, "InteractiveDuplicateReferenceLibrary", "
Public Class C
    Public ReadOnly X As Integer = 2
End Class")

        Dim script = VisualBasicScript.Create("
#r """ & libraryPath & """
#r """ & libraryPath & """
? New C().X")

        Assert.DoesNotContain(script.GetCompilation().GetDiagnostics(), Function(d) d.Severity = DiagnosticSeverity.Error)
        Assert.Equal(2, Await script.EvaluateAsync())
    End Function

    <Fact>
    Public Sub ReferenceDirective_MissingReferenceReportsDiagnostic()
        Dim directory = CreateIsolatedTempDirectory()
        Dim missingPath = Path.Combine(directory, "MissingInteractiveReference.dll")
        Dim diagnostics = VisualBasicScript.Create("#r """ & missingPath & """").GetCompilation().GetDiagnostics()

        Assert.Contains(diagnostics, Function(d) d.Id = "BC2017" AndAlso d.Severity = DiagnosticSeverity.Error)
    End Sub

    <Fact>
    Public Async Function ReferenceDirective_ResolvesDependencyFromReferencedAssemblyDirectory() As Task
        Dim directory = CreateIsolatedTempDirectory()
        Dim dependencyPath = CreateLibraryAssembly(directory, "InteractiveReferenceDependency", "
Public Class D
    Public Shared ReadOnly Y As Integer = 3
End Class")
        Dim libraryPath = CreateLibraryAssembly(directory, "InteractiveReferenceWithDependency", "
Public Class C
    Public ReadOnly X As Integer = D.Y
End Class", {MetadataReference.CreateFromFile(dependencyPath)})

        Dim script = VisualBasicScript.Create("
#r """ & libraryPath & """
? New C().X")

        Assert.DoesNotContain(script.GetCompilation().GetDiagnostics(), Function(d) d.Severity = DiagnosticSeverity.Error)
        Assert.Equal(3, Await script.EvaluateAsync())
    End Function
End Class
