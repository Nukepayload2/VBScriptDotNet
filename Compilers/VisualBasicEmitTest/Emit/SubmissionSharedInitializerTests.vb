' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable
Imports System.IO
Imports Microsoft.CodeAnalysis.Emit
Imports Roslyn.Test.Utilities

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests.Emit

    ''' <summary>
    ''' A submission class with <c>Shared</c> members that need a constructor takes the synthesized shared
    ''' constructor (<c>SourceMemberContainerTypeSymbol.EnsureCtor</c>), not the submission constructor. The
    ''' submission constructor always carries the <c>submissionArray As Object()</c> parameter
    ''' (<c>Symbols\Source\SynthesizedSubmissionConstructorSymbol.vb:31-38</c>), which a shared constructor may not
    ''' have: the emitted <c>.cctor</c> was unloadable, and the host reported it as a
    ''' <c>TypeLoadException</c> for the submission type (the host-visible half of this shape is covered in
    ''' <c>Scripting\VisualBasicTest\ScriptTopLevelCrashTests.vb</c>).
    ''' </summary>
    Public Class SubmissionSharedInitializerTests
        Inherits BasicTestBase

        Private Shared Function CreateSubmissionCompilation(source As String) As VisualBasicCompilation
            Return CreateSubmission(source, options:=TestOptions.DebugDll)
        End Function

        Private Shared Function EmitToMemory(compilation As VisualBasicCompilation) As EmitResult
            Using assemblyStream As New MemoryStream()
                Using pdbStream As New MemoryStream()
                    Return compilation.Emit(assemblyStream, pdbStream)
                End Using
            End Using
        End Function

        Private Shared Function GetSharedConstructors(compilation As VisualBasicCompilation) As ImmutableArray(Of MethodSymbol)
            Return compilation.ScriptClass.SharedConstructors
        End Function

        ''' <summary>
        ''' The shared initializers have to end up in one parameterless shared constructor. Before the submission
        ''' branch used this constructor the type got a shared constructor carrying the submission array parameter.
        ''' </summary>
        Private Shared Sub AssertSingleParameterlessSharedConstructor(compilation As VisualBasicCompilation)
            Dim sharedConstructors = GetSharedConstructors(compilation)

            Assert.Equal(1, sharedConstructors.Length)
            Assert.Equal(MethodKind.SharedConstructor, sharedConstructors(0).MethodKind)
            Assert.Equal(WellKnownMemberNames.StaticConstructorName, sharedConstructors(0).Name)
            Assert.True(sharedConstructors(0).Parameters.IsEmpty)
        End Sub

        Private Shared Sub AssertEmitsWithoutErrors(compilation As VisualBasicCompilation)
            Dim result = EmitToMemory(compilation)

            Assert.True(result.Success, String.Join(" | ", result.Diagnostics.Select(Function(d) d.ToString())))
            Assert.DoesNotContain(result.Diagnostics, Function(d) d.Severity = DiagnosticSeverity.Error)
        End Sub

        <Fact>
        Public Sub SharedFieldWithInitializer_GetsParameterlessSharedConstructor()
            Dim compilation = CreateSubmissionCompilation(
                "Shared sx As Integer = 5" & vbCrLf &
                "System.Console.WriteLine(sx)")

            AssertSingleParameterlessSharedConstructor(compilation)
            AssertEmitsWithoutErrors(compilation)
        End Sub

        <Fact>
        Public Sub SharedReadOnlyFieldWithInitializer_GetsParameterlessSharedConstructor()
            Dim compilation = CreateSubmissionCompilation(
                "Shared ReadOnly sx As Integer = 5" & vbCrLf &
                "System.Console.WriteLine(sx)")

            AssertSingleParameterlessSharedConstructor(compilation)
            AssertEmitsWithoutErrors(compilation)
        End Sub

        ''' <summary>
        ''' An array field with an implicit upper bound is a shared initializer without an <c>=</c>
        ''' (<c>Symbols\Source\SourceMemberFieldSymbol.vb:615-639</c>).
        ''' </summary>
        <Fact>
        Public Sub SharedArrayFieldWithUpperBound_GetsParameterlessSharedConstructor()
            Dim compilation = CreateSubmissionCompilation(
                "Shared arr(2) As Integer" & vbCrLf &
                "System.Console.WriteLine(arr.Length)")

            AssertSingleParameterlessSharedConstructor(compilation)
            AssertEmitsWithoutErrors(compilation)
        End Sub

        ''' <summary>
        ''' <c>Const</c> fields of type <c>Date</c>/<c>Decimal</c> need a shared constructor as well, but they are
        ''' injected by <c>CreateSharedConstructorsForConstFieldsIfRequired</c>, which stands down when the member
        ''' list already has a <c>.cctor</c>. Both initializers therefore have to share that one constructor.
        ''' </summary>
        <Fact>
        Public Sub ConstDateFieldAndSharedField_ShareASingleSharedConstructor()
            Dim compilation = CreateSubmissionCompilation(
                "Const d As Date = #1/1/2020#" & vbCrLf &
                "Shared x As Integer = 5" & vbCrLf &
                "System.Console.WriteLine(x)")

            AssertSingleParameterlessSharedConstructor(compilation)
            AssertEmitsWithoutErrors(compilation)
        End Sub

        ''' <summary>
        ''' A <c>Const</c> field of type <c>Date</c> on its own keeps going through the separate synthesized shared
        ''' constructor, which is not a member of the type.
        ''' </summary>
        <Fact>
        Public Sub ConstDateFieldAlone_KeepsItsSeparateSharedConstructor()
            Dim compilation = CreateSubmissionCompilation(
                "Const d As Date = #1/1/2020#" & vbCrLf &
                "System.Console.WriteLine(d.Year)")

            Assert.True(GetSharedConstructors(compilation).IsEmpty)
            AssertEmitsWithoutErrors(compilation)
        End Sub

        ''' <summary>
        ''' The instance path is deliberately untouched: a submission keeps its instance submission constructor
        ''' with the submission array parameter and gains no shared constructor from an instance initializer.
        ''' </summary>
        <Fact>
        Public Sub InstanceInitializer_KeepsTheSubmissionConstructorAndAddsNoSharedConstructor()
            Dim compilation = CreateSubmissionCompilation(
                "Dim iy As Integer = 7" & vbCrLf &
                "System.Console.WriteLine(iy)")

            Assert.True(GetSharedConstructors(compilation).IsEmpty)

            Dim scriptConstructor = compilation.ScriptClass.InstanceConstructors.Single()
            Assert.Equal(1, scriptConstructor.ParameterCount)
            Assert.Equal(WellKnownMemberNames.InstanceConstructorName, scriptConstructor.Name)

            AssertEmitsWithoutErrors(compilation)
        End Sub
    End Class
End Namespace
