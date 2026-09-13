' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable
Imports System.IO
Imports Microsoft.CodeAnalysis.Emit
Imports Roslyn.Test.Utilities

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests.Emit

    ''' <summary>
    ''' An <c>Event</c> or a <c>WithEvents</c> field declared in a submission class synthesizes members whose
    ''' attributes are added by <c>AddSynthesizedAttributes</c>. Those used to assert that the containing type is
    ''' not implicitly declared - which a submission class always is - so emitting a script with such a member
    ''' terminated the process. The submission class is what user code is written in, so the assertion is narrowed
    ''' to the case it was meant for: a genuinely implicit class of a regular compilation.
    ''' </summary>
    Public Class SubmissionEventMemberTests
        Inherits BasicTestBase

        Private Shared ReadOnly RaiserSource As String =
            "Class Raiser" & vbCrLf &
            "    Event SomethingHappened As System.EventHandler" & vbCrLf &
            "End Class"

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

        Private Shared Sub AssertEmitsWithoutErrors(compilation As VisualBasicCompilation)
            Dim result = EmitToMemory(compilation)

            Assert.True(result.Success,
                        String.Join(" | ", result.Diagnostics.Select(Function(d) d.ToString())))
        End Sub

        <Fact>
        Public Sub InstanceEvent_Emits()
            AssertEmitsWithoutErrors(CreateSubmissionCompilation(
                "Event E As System.EventHandler" & vbCrLf &
                "AddHandler E, Sub(s As Object, e As System.EventArgs)" & vbCrLf &
                "              End Sub"))
        End Sub

        <Fact>
        Public Sub SharedEvent_Emits()
            AssertEmitsWithoutErrors(CreateSubmissionCompilation(
                "Shared Event E As System.EventHandler" & vbCrLf &
                "AddHandler E, Sub(s As Object, e As System.EventArgs)" & vbCrLf &
                "              End Sub"))
        End Sub

        <Fact>
        Public Sub InstanceWithEventsField_Emits()
            AssertEmitsWithoutErrors(CreateSubmissionCompilation(
                RaiserSource & vbCrLf &
                "WithEvents r As New Raiser"))
        End Sub

        <Fact>
        Public Sub SharedWithEventsField_Emits()
            AssertEmitsWithoutErrors(CreateSubmissionCompilation(
                RaiserSource & vbCrLf &
                "Shared WithEvents r As New Raiser"))
        End Sub

        ''' <summary>The same members in a nested (regular) type keep emitting as they did.</summary>
        <Fact>
        Public Sub NestedTypeEventMembers_StillEmit()
            AssertEmitsWithoutErrors(CreateSubmissionCompilation(
                "Class Container" & vbCrLf &
                "    Class Raiser" & vbCrLf &
                "        Event SomethingHappened As System.EventHandler" & vbCrLf &
                "    End Class" & vbCrLf &
                "    Public Event E As System.EventHandler" & vbCrLf &
                "    Public WithEvents r As New Raiser" & vbCrLf &
                "End Class"))
        End Sub

        ''' <summary>
        ''' The narrowed assertion still covers what it was written for: the implicit class a regular compilation
        ''' wraps namespace-invalid members in. Such a container is <c>IsImplicitClass</c> (and not a script
        ''' class), while the submission class is the other way round.
        ''' </summary>
        <Fact>
        Public Sub ImplicitClassContainer_IsStillImplicitClassAndIsNotAScriptClass()
            Dim compilation = CompilationUtils.CreateCompilationWithMscorlib40AndVBRuntime(
<compilation name="ImplicitClassContainer">
    <file name="a.vb">
Dim x As Integer
    </file>
</compilation>)

            Dim implicitClass = compilation.Assembly.GlobalNamespace.GetTypeMembers().Single()

            Assert.True(implicitClass.IsImplicitClass)
            Assert.False(implicitClass.IsScriptClass)
        End Sub

        <Fact>
        Public Sub SubmissionClass_IsAScriptClassAndNotAnImplicitClass()
            Dim scriptClass = CreateSubmissionCompilation("Dim x As Integer").ScriptClass

            Assert.True(scriptClass.IsScriptClass)
            Assert.False(scriptClass.IsImplicitClass)
        End Sub
    End Class
End Namespace
