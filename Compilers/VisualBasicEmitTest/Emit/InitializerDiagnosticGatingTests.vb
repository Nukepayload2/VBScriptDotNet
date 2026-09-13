' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable
Imports System.IO
Imports Microsoft.CodeAnalysis.Emit
Imports Roslyn.Test.Utilities

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests.Emit

    ''' <summary>
    ''' A binding error reported while a field/property initializer is bound must stop code generation for the
    ''' type whose initializer carries it.
    ''' <para>
    ''' The initializers are bound by <c>Binder.BindFieldAndPropertyInitializers</c> into the compilation-wide
    ''' bag (<c>Compilation\MethodCompiler.vb:599-623</c>), while the per-method emit gate
    ''' (<c>MethodCompiler.vb:1288</c>) used to look only at bound-node error flags. A diagnostic that does not
    ''' mark its bound node - BC36937 from <c>Binder_Expressions.vb:4751-4752</c> is the canonical one - was
    ''' therefore reported and then ignored: code generation ran on the un-lowered <c>AwaitOperator</c> and
    ''' <c>CodeGen\EmitExpression.vb:207</c> asserted ("Code gen should not be invoked if there are errors.").
    ''' In the test host that assertion surfaces as an <c>InvalidOperationException</c> ("Unexpected value
    ''' 'AwaitOperator'") thrown out of <c>Emit</c>, where the shipped compiler terminates the process.
    ''' </para>
    ''' <para>
    ''' Both buckets of a type are bound into a diagnostic bag of their own and merged into the shared bag
    ''' afterwards, so "did this binding report an error" no longer depends on errors other members put into the
    ''' shared bag (<c>MethodCompiler.CompileNamedType</c>). The two buckets are decided independently: an error
    ''' in one must neither disable nor force the gate of the other.
    ''' </para>
    ''' <para>
    ''' <c>Binder.BindFieldAndPropertyInitializers</c> also binds the <b>top-level statements</b> of a script class
    ''' (<c>Binder_Initializers.vb:212-236</c> <c>BindGlobalStatement</c>) and files them into the instance
    ''' bucket together with the instance field/property initializers. Diagnostics those statements report -
    ''' again several of them without marking the bound tree - gate the <c>&lt;Initialize&gt;</c> method for
    ''' exactly the same reason, so statements are covered here as the second statement kind.
    ''' </para>
    ''' <para>
    ''' The cases here differ in how much they say about the gate, and the difference matters when one of them is
    ''' changed. A case fails when the gate does not fire only if the bucket holds a shape code generation cannot
    ''' survive - then the ungateable compilation throws out of <c>Emit</c> instead of returning a failed result.
    ''' Note that a failed result on its own proves nothing: any error in the bag makes <c>Emit</c> report failure
    ''' whether or not code generation ran (<c>Core\Portable\Compilation\Compilation.cs:3030-3033</c> sets
    ''' "success = false" for any unsuppressed error), so <c>Assert.False(result.Success)</c> cannot distinguish
    ''' the gate. The cases built on shapes that emit fine are regression locks: they pin the diagnostic set,
    ''' which is worth pinning, but the gate itself is discriminated elsewhere in this file.
    ''' </para>
    ''' <para>
    ''' The shapes below are script (submission) compilations: only there is the whole file parsed as an async
    ''' context (<c>Parser\BlockContexts\CompilationUnitContext.vb:32-36</c> returns
    ''' <c>Parser.IsScript</c>), so <c>Await</c> inside a nested type's initializer is a valid expression. In a
    ''' regular compilation the same text is a parse error (BC30205) which already stopped code generation, so
    ''' it cannot exercise this gate.
    ''' </para>
    ''' <para>
    ''' Emit targets are in-memory streams only; no files, processes or network access are involved.
    ''' </para>
    ''' </summary>
    Public Class InitializerDiagnosticGatingTests
        Inherits BasicTestBase

        Private Shared Function CreateSubmissionCompilation(source As String,
                                                             Optional options As VisualBasicCompilationOptions = Nothing) As VisualBasicCompilation
            Return CreateSubmission(source, options:=If(options, TestOptions.DebugDll))
        End Function

        Private Shared Function CreateClassCompilation(source As XElement,
                                                        Optional options As VisualBasicCompilationOptions = Nothing) As VisualBasicCompilation
            Return CompilationUtils.CreateCompilationWithMscorlib40AndVBRuntime(
                source,
                options:=If(options, TestOptions.DebugDll))
        End Function

        Private Shared Function EmitToMemory(compilation As VisualBasicCompilation) As EmitResult
            Using assemblyStream As New MemoryStream()
                Using pdbStream As New MemoryStream()
                    Return compilation.Emit(assemblyStream, pdbStream)
                End Using
            End Using
        End Function

        Private Shared Function GetErrors(diagnostics As ImmutableArray(Of Diagnostic)) As String()
            Return diagnostics.
                Where(Function(d) d.Severity = DiagnosticSeverity.Error).
                Select(Function(d) d.Id & ": " & d.GetMessage() & " @" & d.Location.GetLineSpan().StartLinePosition.Line).
                ToArray()
        End Function

        ''' <summary>
        ''' The initializer diagnostic is the only error in the compilation, so the failed emit (and the absence
        ''' of a code-gen crash) is attributable to it alone.
        ''' </summary>
        Private Shared Sub AssertOnlyErrorIs(diagnostics As ImmutableArray(Of Diagnostic), expectedId As String)
            Dim errors = GetErrors(diagnostics)
            Assert.True(errors.Length = 1 AndAlso errors(0).StartsWith(expectedId), String.Join(" | ", errors))
        End Sub

        ''' <summary>
        ''' Asserts the whole error set of the compilation, so a case can neither pass with fewer errors than the
        ''' shape produces nor with more.
        ''' </summary>
        Private Shared Sub AssertErrorsAre(diagnostics As ImmutableArray(Of Diagnostic), ParamArray expectedIds As String())
            Dim errors = GetErrors(diagnostics)
            Assert.True(errors.Length = expectedIds.Length, String.Join(" | ", errors))
            For Each expectedId In expectedIds
                Assert.True(errors.Any(Function(e) e.StartsWith(expectedId)), String.Join(" | ", errors))
            Next
        End Sub

        ''' <summary>
        ''' A top level statement that a bucket must never hand to code generation: an <c>Await</c> inside a
        ''' <c>Catch</c> block.
        ''' <para>
        ''' The walker that reports BC36943 for it runs on top level statements only, because their synthesized
        ''' host body is a stub (<c>SynthesizedInteractiveInitializerMethod.vb:135-142</c>, the check is re-run
        ''' from <c>Binder_Initializers.vb:238-248</c>), and it reports the diagnostic without marking the bound
        ''' tree. The per-bucket diagnostic bag is therefore the only thing that can keep the
        ''' bucket out of code generation. Emitting the shape is not possible: with the gate out of the way the
        ''' compilation ran into a <c>NullReferenceException</c> inside the IL builder (probe g6/g7 for
        ''' <c>Core\CodeGen\ILBuilder.cs</c>, probe g15 for the <c>Finally</c> twin), which is why this shape -
        ''' unlike a diagnostic that emits fine - makes a case fail when the gate does not fire.
        ''' </para>
        ''' </summary>
        Private Const UnemittableTopLevelStatement As String =
            "Try" & vbCrLf &
            "Catch ex As System.Exception" & vbCrLf &
            "    Await System.Threading.Tasks.Task.Delay(1)" & vbCrLf &
            "End Try" & vbCrLf

        <Fact>
        Public Sub NestedClassInstanceFieldInitializerAwait_IsReportedAndGated()
            Dim compilation = CreateSubmissionCompilation(
                "Imports System.Threading.Tasks" & vbCrLf &
                "Class C" & vbCrLf &
                "    Dim s As Integer = Await Task.FromResult(9)" & vbCrLf &
                "End Class")

            Dim result = EmitToMemory(compilation)

            Assert.False(result.Success)
            AssertOnlyErrorIs(result.Diagnostics, "BC36937")
        End Sub

        <Fact>
        Public Sub NestedClassSharedFieldInitializerAwait_IsReportedAndGated()
            Dim compilation = CreateSubmissionCompilation(
                "Imports System.Threading.Tasks" & vbCrLf &
                "Class C" & vbCrLf &
                "    Shared s As Integer = Await Task.FromResult(9)" & vbCrLf &
                "End Class")

            Dim result = EmitToMemory(compilation)

            Assert.False(result.Success)
            AssertOnlyErrorIs(result.Diagnostics, "BC36937")
        End Sub

        <Fact>
        Public Sub NestedClassInstancePropertyInitializerAwait_IsReportedAndGated()
            Dim compilation = CreateSubmissionCompilation(
                "Imports System.Threading.Tasks" & vbCrLf &
                "Class C" & vbCrLf &
                "    Property p As Integer = Await Task.FromResult(9)" & vbCrLf &
                "End Class")

            Dim result = EmitToMemory(compilation)

            Assert.False(result.Success)
            AssertOnlyErrorIs(result.Diagnostics, "BC36937")
        End Sub

        <Fact>
        Public Sub NestedClassSharedReadOnlyPropertyInitializerAwait_IsReportedAndGated()
            Dim compilation = CreateSubmissionCompilation(
                "Imports System.Threading.Tasks" & vbCrLf &
                "Class C" & vbCrLf &
                "    Shared ReadOnly Property p As Integer = Await Task.FromResult(9)" & vbCrLf &
                "End Class")

            Dim result = EmitToMemory(compilation)

            Assert.False(result.Success)
            AssertOnlyErrorIs(result.Diagnostics, "BC36937")
        End Sub

        <Fact>
        Public Sub ModuleFieldInitializerAwait_IsReportedAndGated()
            Dim compilation = CreateSubmissionCompilation(
                "Imports System.Threading.Tasks" & vbCrLf &
                "Module M" & vbCrLf &
                "    Dim s As Integer = Await Task.FromResult(9)" & vbCrLf &
                "End Module")

            Dim result = EmitToMemory(compilation)

            Assert.False(result.Success)
            AssertOnlyErrorIs(result.Diagnostics, "BC36937")
        End Sub

        <Fact>
        Public Sub StructureSharedFieldInitializerAwait_IsReportedAndGated()
            Dim compilation = CreateSubmissionCompilation(
                "Imports System.Threading.Tasks" & vbCrLf &
                "Structure S" & vbCrLf &
                "    Shared s As Integer = Await Task.FromResult(9)" & vbCrLf &
                "End Structure")

            Dim result = EmitToMemory(compilation)

            Assert.False(result.Success)
            AssertOnlyErrorIs(result.Diagnostics, "BC36937")
        End Sub

        <Fact>
        Public Sub DeeplyNestedClassFieldInitializerAwait_IsReportedAndGated()
            Dim compilation = CreateSubmissionCompilation(
                "Imports System.Threading.Tasks" & vbCrLf &
                "Class Outer" & vbCrLf &
                "    Class Inner" & vbCrLf &
                "        Dim s As Integer = Await Task.FromResult(9)" & vbCrLf &
                "    End Class" & vbCrLf &
                "End Class")

            Dim result = EmitToMemory(compilation)

            Assert.False(result.Success)
            AssertOnlyErrorIs(result.Diagnostics, "BC36937")
        End Sub

        ''' <summary>
        ''' An error produced *before* the faulty initializer is bound (here: a method body error in a type that
        ''' is declared first) must not switch the gate off. The gate has to be decided by the diagnostics the
        ''' initializer binding itself produced, not by "the compilation bag already had an error".
        ''' </summary>
        <Fact>
        Public Sub UnrelatedErrorInAnotherType_DoesNotDisableGatingOfTheInitializerError()
            Dim compilation = CreateSubmissionCompilation(
                "Imports System.Threading.Tasks" & vbCrLf &
                "Class A" & vbCrLf &
                "    Shared Sub M()" & vbCrLf &
                "        NoSuchMethod()" & vbCrLf &
                "    End Sub" & vbCrLf &
                "End Class" & vbCrLf &
                "Class B" & vbCrLf &
                "    Dim s As Integer = Await Task.FromResult(9)" & vbCrLf &
                "End Class",
                TestOptions.DebugDll.WithConcurrentBuild(False))

            Dim result = EmitToMemory(compilation)

            Assert.False(result.Success)
            Assert.Contains(result.Diagnostics, Function(d) d.Id = "BC30451" AndAlso d.Severity = DiagnosticSeverity.Error)
            Assert.Contains(result.Diagnostics, Function(d) d.Id = "BC36937" AndAlso d.Severity = DiagnosticSeverity.Error)
        End Sub

        ''' <summary>
        ''' Declaration errors already stopped code generation before this change; the initializer diagnostic
        ''' must not replace or hide them.
        ''' </summary>
        <Fact>
        Public Sub DeclarationErrorAlongsideInitializerError_BothDiagnosticsAreReported()
            Dim compilation = CreateSubmissionCompilation(
                "Imports System.Threading.Tasks" & vbCrLf &
                "Class C" & vbCrLf &
                "    Dim s As Integer = Await Task.FromResult(9)" & vbCrLf &
                "    Dim s As Integer" & vbCrLf &
                "End Class")

            Dim result = EmitToMemory(compilation)

            Assert.False(result.Success)
            Assert.Contains(result.Diagnostics, Function(d) d.Id = "BC30260" AndAlso d.Severity = DiagnosticSeverity.Error)
            Assert.Contains(result.Diagnostics, Function(d) d.Id = "BC36937" AndAlso d.Severity = DiagnosticSeverity.Error)
            Assert.True(GetErrors(result.Diagnostics).Length = 2, String.Join(" | ", GetErrors(result.Diagnostics)))
        End Sub

        ''' <summary>
        ''' The pre-existing route - an error that marks the bound node (here the non-awaitable operand marks it)
        ''' - keeps working. The initializer also reports BC36930 on the same expression, so this shape is not
        ''' gated by the new channel alone.
        ''' </summary>
        <Fact>
        Public Sub NonAwaitableOperandInInitializer_IsReportedAndGated()
            Dim compilation = CreateSubmissionCompilation(
                "Class C" & vbCrLf &
                "    Shared s As Integer = Await 5" & vbCrLf &
                "End Class")

            Dim result = EmitToMemory(compilation)

            Assert.False(result.Success)
            Assert.Contains(result.Diagnostics, Function(d) d.Id = "BC36937" AndAlso d.Severity = DiagnosticSeverity.Error)
            Assert.Contains(result.Diagnostics, Function(d) d.Id = "BC36930" AndAlso d.Severity = DiagnosticSeverity.Error)
            Assert.True(GetErrors(result.Diagnostics).Length = 2, String.Join(" | ", GetErrors(result.Diagnostics)))
        End Sub

        ''' <summary>
        ''' The gate is "the initializer binding produced an error", not "the bag is not empty": a warning that
        ''' is produced by the very same initializer binding (an obsolete member call) must not stop the emit.
        ''' </summary>
        <Fact>
        Public Sub WarningFromInitializerBinding_DoesNotStopEmission()
            Dim compilation = CreateClassCompilation(
<compilation>
    <file name="a.vb">
        <![CDATA[
Class C
    <System.Obsolete>
    Shared Function Old() As Integer
        Return 1
    End Function

    Dim s As Integer = Old()
End Class
]]>
    </file>
</compilation>)

            Dim result = EmitToMemory(compilation)

            Assert.True(result.Success)
            Assert.Contains(result.Diagnostics, Function(d) d.Id = "BC40008" AndAlso d.Severity = DiagnosticSeverity.Warning)
            Assert.DoesNotContain(result.Diagnostics, Function(d) d.Severity = DiagnosticSeverity.Error)
        End Sub

        <Fact>
        Public Sub CleanInitializers_StillEmitSuccessfully()
            Dim compilation = CreateClassCompilation(
<compilation>
    <file name="a.vb">
        <![CDATA[
Class C
    Dim i As Integer = 1
    Shared s As Integer = 2
    ReadOnly Property p As Integer = 3
End Class
]]>
    </file>
</compilation>)

            Dim result = EmitToMemory(compilation)

            Assert.True(result.Success)
            Assert.DoesNotContain(result.Diagnostics, Function(d) d.Severity = DiagnosticSeverity.Error)
        End Sub

        ''' <summary>
        ''' The instance bucket of a script class holds its instance field initializers *and* all of its top-level
        ''' statements, so an error an initializer reports and an error a statement reports gate through the same
        ''' decision. Both buckets stay non-empty here: the clean static bucket must neither swallow the error of
        ''' the instance bucket nor produce one of its own.
        ''' </summary>
        <Fact>
        Public Sub InstanceBucketErrorWithCleanStaticBucket_IsReportedAndGated()
            Dim compilation = CreateSubmissionCompilation(
                "Imports System.Threading.Tasks" & vbCrLf &
                "Class C" & vbCrLf &
                "    Dim s As Integer = Await Task.FromResult(9)" & vbCrLf &
                "    Shared t As Integer = 42" & vbCrLf &
                "End Class")

            Dim result = EmitToMemory(compilation)

            Assert.False(result.Success)
            AssertOnlyErrorIs(result.Diagnostics, "BC36937")
        End Sub

        ''' <summary>
        ''' The mirror of <see cref="InstanceBucketErrorWithCleanStaticBucket_IsReportedAndGated"/>: the static
        ''' bucket carries the error, the (non-empty) instance bucket is clean - it must not report anything
        ''' extra, and the static bucket's error must still gate.
        ''' </summary>
        <Fact>
        Public Sub StaticBucketErrorWithCleanInstanceBucket_IsReportedAndGated()
            Dim compilation = CreateSubmissionCompilation(
                "Imports System.Threading.Tasks" & vbCrLf &
                "Class C" & vbCrLf &
                "    Shared s As Integer = Await Task.FromResult(9)" & vbCrLf &
                "    Dim t As Integer = 42" & vbCrLf &
                "End Class")

            Dim result = EmitToMemory(compilation)

            Assert.False(result.Success)
            AssertOnlyErrorIs(result.Diagnostics, "BC36937")
        End Sub

        ''' <summary>
        ''' A top-level statement whose binding reports an error (BC30582 here: a <c>SyncLock</c> operand that is
        ''' not a reference type) must gate the emission of the script class just like an initializer error does.
        ''' The clean statement and the clean nested class show that nothing else is reported and nothing else is
        ''' affected.
        ''' <para>
        ''' This case is a regression lock, not a discriminating one: this shape emits fine when the gate does not
        ''' fire, and the failed emit it asserts comes from the bag alone. See the class remarks.
        ''' </para>
        ''' </summary>
        <Fact>
        Public Sub TopLevelStatementBindingError_IsReportedAndGated()
            Dim compilation = CreateSubmissionCompilation(
                "SyncLock 5" & vbCrLf &
                "    System.Console.WriteLine(""lock"")" & vbCrLf &
                "End SyncLock" & vbCrLf &
                "System.Console.WriteLine(""after"")" & vbCrLf &
                "Class C" & vbCrLf &
                "    Dim u As Integer = 1" & vbCrLf &
                "End Class")

            Dim result = EmitToMemory(compilation)

            Assert.False(result.Success)
            AssertOnlyErrorIs(result.Diagnostics, "BC30582")
        End Sub

        ''' <summary>
        ''' Same statement-kind shape, and the same bucket also holds a shape code generation cannot survive
        ''' (<see cref="UnemittableTopLevelStatement"/>). Before the gate covered the instance bucket this
        ''' compilation threw out of <c>Emit</c>; the failed emit now comes from the reported binding errors and
        ''' code generation of the whole bucket does not run. This is the case that discriminates the gate for
        ''' top level statements: reverting the gate makes <c>Emit</c> throw instead of returning.
        ''' </summary>
        <Fact>
        Public Sub TopLevelStatementBindingError_StopsCodeGenerationOfTheWholeBucket()
            Dim compilation = CreateSubmissionCompilation(
                "SyncLock 5" & vbCrLf &
                "    System.Console.WriteLine(""lock"")" & vbCrLf &
                "End SyncLock" & vbCrLf &
                UnemittableTopLevelStatement &
                "System.Console.WriteLine(""after"")" & vbCrLf &
                "Class C" & vbCrLf &
                "    Dim u As Integer = 1" & vbCrLf &
                "End Class")

            Dim result = EmitToMemory(compilation)

            Assert.False(result.Success)
            AssertErrorsAre(result.Diagnostics, "BC30582", "BC36943")
        End Sub

        ''' <summary>
        ''' The same channel also carries errors reported from a nested block *inside* a top-level statement: the
        ''' error of the inner <c>SyncLock</c> does not mark any bound node, yet it has to gate. The appended
        ''' <see cref="UnemittableTopLevelStatement"/> is what makes this case fail when the gate does not fire.
        ''' </summary>
        <Fact>
        Public Sub NestedBlockOfTopLevelStatementBindingError_IsReportedAndGated()
            Dim compilation = CreateSubmissionCompilation(
                "If True Then" & vbCrLf &
                "    SyncLock 5" & vbCrLf &
                "        System.Console.WriteLine(""lock"")" & vbCrLf &
                "    End SyncLock" & vbCrLf &
                "End If" & vbCrLf &
                UnemittableTopLevelStatement &
                "System.Console.WriteLine(""after"")")

            Dim result = EmitToMemory(compilation)

            Assert.False(result.Success)
            AssertErrorsAre(result.Diagnostics, "BC30582", "BC36943")
        End Sub

        ''' <summary>
        ''' The second statement kind that reports into the same bucket bag without marking its bound node: a bare
        ''' expression statement that is not the final statement of an interactive submission (BC31003). The
        ''' appended <see cref="UnemittableTopLevelStatement"/> is what makes this case fail when the gate does
        ''' not fire.
        ''' </summary>
        <Fact>
        Public Sub NonFinalBareExpressionAtTopLevel_IsReportedAndGated()
            Dim compilation = CreateSubmissionCompilation(
                "1 + 2" & vbCrLf &
                UnemittableTopLevelStatement &
                "System.Console.WriteLine(""after"")")

            Dim result = EmitToMemory(compilation)

            Assert.False(result.Success)
            AssertErrorsAre(result.Diagnostics, "BC31003", "BC36943")
        End Sub

        ''' <summary>
        ''' Clean top-level statements (including a bare <c>Await</c> statement and an <c>If</c> block) keep
        ''' emitting: the gate must not fire for a submission that only goes through this same path.
        ''' </summary>
        <Fact>
        Public Sub CleanTopLevelStatements_StillEmitSuccessfully()
            Dim compilation = CreateSubmissionCompilation(
                "Imports System.Threading.Tasks" & vbCrLf &
                "Dim t As Integer = 3" & vbCrLf &
                "System.Console.WriteLine(t)" & vbCrLf &
                "Await Task.Delay(1)" & vbCrLf &
                "If t > 2 Then" & vbCrLf &
                "    System.Console.WriteLine(t + 1)" & vbCrLf &
                "End If" & vbCrLf &
                "Class C" & vbCrLf &
                "    Dim u As Integer = 7" & vbCrLf &
                "End Class")

            Dim result = EmitToMemory(compilation)

            Assert.True(result.Success)
            Assert.DoesNotContain(result.Diagnostics, Function(d) d.Severity = DiagnosticSeverity.Error)
        End Sub
    End Class
End Namespace
