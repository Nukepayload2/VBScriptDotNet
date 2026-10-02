' ===========================================================================================
' RESERVED / 已保留未启用（2026-10-02）—— 不要把它们改回 [Fact]
'
' 这些用例是为 issue 35 的候选修复 R2 写的。**该修复已 RESERVE（暂不交付）**，原因：
'   1) C# 侧是**同一个缺陷**（Compilers\CSharp\Portable\Symbols\ReferenceManager.cs 与 VB 那份
'      逐行同构：查缓存持锁 -> 建符号在锁外 -> 再入锁发布）。单独修 VB ＝ 对上游形成分叉，
'      而 decisions.md D5 要求给出「为什么 VB 必须分叉」；目前只有"本 fork 只发布 VB 脚本"
'      这个**产品范围**理由，不是技术理由。
'   2) R2 本身在普通编译上引入了回归（Symbol 门 28 条失败：采纳缓存符号未校验 IsLinked 兼容，
'      且跨编译共享范围过宽——NoPia / UsedAssembliesTests 要求不同实例）。
'
' 保留本文件是为了不丢掉**复现配方**（"两段提交链 + 全新 MetadataReference + 闸门齐放的专用线程"
' 这个形状本身很难重新想到）。解除 RESERVE 时：把 [Fact] 恢复、删掉本横幅，并按 issue 35
' §三之二 的三条了结路径之一重新决策。
' 依据：InternalDevDocs\issues\issue-parallel-submission-binding-assert.md §三之二、
'        InternalDevDocs\upstream-merge.md §2.25(k)
' ===========================================================================================
' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis.Test.Utilities
Imports Roslyn.Test.Utilities

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests.Emit

    ''' <summary>
    ''' The ordinary-compilation control for the fix in <c>ReferenceManager.CreateAndSetSourceAssemblyFullBind</c>.
    ''' <para>
    ''' That fix makes "is there already a symbol for this metadata?", "create one" and "publish it" a single
    ''' atomic step under <c>SymbolCacheAndReferenceManagerStateGuard</c>, so two compilations in one process
    ''' can no longer end up with two <c>PEAssemblySymbol</c>s for one assembly - two <c>System.String</c>s that
    ''' compare equal by <c>SpecialType</c> (so <c>Conversions</c> calls the conversion an identity one) but not
    ''' by <c>IsSameTypeIgnoringAll</c>, which is what <c>Binder.CreateConversionAndReportDiagnostic</c> asserts
    ''' on (<c>Binder_Conversions.vb:442</c>).
    ''' </para>
    ''' <para>
    ''' A submission chain is the only shape that showed it, because that is where two compilations in one
    ''' process bind the same metadata and meet each other's types. The cells here are the counterpart of that:
    ''' the same declaration shapes in ordinary, non-script compilations, where the fix must move nothing at
    ''' all.
    ''' </para>
    ''' <para>
    ''' How they check that, precisely: every cell compares the compilation's <b>entire</b> diagnostic list -
    ''' id, severity and rendered message, in production order - against a literal expected sequence, with
    ''' <c>Assert.Equal</c> over the collection overload. That is a whole-list comparison, not "no errors":
    ''' a compilation that starts reporting one extra warning, or that keeps reporting an error but moves it to
    ''' a different id or wording, fails here. Three of the four cells expect the empty list; the fourth
    ''' expects one specific warning, so the comparison is shown to discriminate in both directions rather
    ''' than only to accept silence.
    ''' </para>
    ''' </summary>
    Public Class ConcurrentSubmissionsOverOneMetadataReferenceTests
        Inherits BasicTestBase

        ''' <summary>The expected diagnostic list of a compilation that must report nothing at all.</summary>
        Private Shared ReadOnly NoDiagnostics As String() = Array.Empty(Of String)()

        ''' <summary>
        ''' Renders every diagnostic the compilation produces, in production order, as
        ''' "&lt;id&gt;: &lt;severity&gt;: &lt;message&gt;". Everything a caller could observe about a
        ''' diagnostic is in the string, so equality of the two lists is equality of the diagnostic sets.
        ''' The severity is spelled out rather than formatted from the enum, so that the expected literals in
        ''' the cells below read the same way the compiler's own output does and do not depend on how the
        ''' enum happens to render.
        ''' </summary>
        Private Shared Function DiagnosticSet(compilation As Compilation) As String()
            Return compilation.GetDiagnostics().
                Select(Function(d) d.Id & ": " & SeverityName(d.Severity) & ": " & d.GetMessage(CultureInfo.InvariantCulture)).
                ToArray()
        End Function

        Private Shared Function SeverityName(severity As DiagnosticSeverity) As String
            Select Case severity
                Case DiagnosticSeverity.Error
                    Return "error"
                Case DiagnosticSeverity.Warning
                    Return "warning"
                Case DiagnosticSeverity.Info
                    Return "info"
                Case Else
                    Return "hidden"
            End Select
        End Function

        ''' <summary>
        ''' Whole-list comparison. The two lists are joined with a separator that no rendered diagnostic
        ''' contains and compared as one string, so that a failure shows every character of the difference
        ''' instead of xUnit's truncated collection rendering, and so that the comparison cannot be resolved
        ''' by an overload that falls back to reference equality on the arrays.
        ''' </summary>
        Private Shared Sub AssertDiagnosticSet(expected As IEnumerable(Of String), compilation As Compilation)
            Assert.Equal(String.Join(" | ", expected), String.Join(" | ", DiagnosticSet(compilation)))
        End Sub

        ''' <summary>
        ''' The same shape the crashing cell binds - a shared method of a declared class whose result is
        ''' returned, and again through a field, which is where the implicit conversion of
        ''' <c>Binder_Conversions.vb:442</c> is classified - as an ordinary compilation. No submission, no
        ''' return type on the compilation, nothing that goes near the submission path.
        ''' </summary>
        '<Fact>
        Public Sub OrdinaryCompilation_SameShape_KeepsItsExactDiagnostics()
            Dim compilation = CreateCompilationWithMscorlib45AndVBRuntime(
                <compilation name="OrdinaryShape">
                    <file name="a.vb"><![CDATA[
Public Class Sink
    Public Shared Function Take() As String
        Return "x"
    End Function
End Class

Public Module Consumer
    Public Function Use() As String
        Return Sink.Take()
    End Function

    Public Function UseViaField() As String
        Dim field As String = Sink.Take()
        Return field
    End Function
End Module
]]></file>
                </compilation>,
                options:=TestOptions.DebugDll)

            AssertDiagnosticSet(NoDiagnostics, compilation)
        End Sub

        ''' <summary>
        ''' The class/module field-initializer shapes, which is where a shared constructor - and with it the
        ''' member of the submission class the fix touches - is created. A fix that changed which symbol those
        ''' initializers bind to would show here as a changed diagnostic list.
        ''' </summary>
        '<Fact>
        Public Sub OrdinaryModuleAndClass_KeepTheirExactDiagnostics()
            Dim compilation = CreateCompilationWithMscorlib45AndVBRuntime(
                <compilation name="OrdinaryShapes">
                    <file name="a.vb"><![CDATA[
Public Class Boxed
    Public Shared z As Integer = 5
End Class

Public Module Held
    Public y As Integer = 6
End Module

Public Class WithConsts
    Public Const d As Date = #1/1/2020#
    Public Shared x As Integer = 7
End Class
]]></file>
                </compilation>,
                options:=TestOptions.DebugDll)

            AssertDiagnosticSet(NoDiagnostics, compilation)
        End Sub

        ''' <summary>
        ''' The mixed shape, stated exactly as it is: one ordinary compilation that carries both the
        ''' submission's own script class (the <c>Sink</c> the crashing cell returns from) <i>and</i> an
        ''' ordinary consumer of it in the same compilation, so the two meet through the same corlib symbol
        ''' inside a single <c>ReferenceManager</c>. There is no <c>CreateSubmission</c> here and no submission
        ''' return type: it is an ordinary compilation, and it is named and documented as one.
        ''' </summary>
        '<Fact>
        Public Sub OrdinaryCompilation_MixedScriptClassAndConsumer_KeepTheirExactDiagnostics()
            Dim compilation = CreateCompilationWithMscorlib45AndVBRuntime(
                <compilation name="Mixed">
                    <file name="a.vb"><![CDATA[
Public Class Sink
    Public Shared Function Take() As String
        Return "x"
    End Function
End Class

Public Class Caller
    Public Function Go() As String
        Return Sink.Take()
    End Function
End Class
]]></file>
                </compilation>,
                options:=TestOptions.DebugDll)

            AssertDiagnosticSet(NoDiagnostics, compilation)
        End Sub

        ''' <summary>
        ''' The one cell whose expected list is not empty, so that the comparison above is demonstrably able
        ''' to see a diagnostic rather than only to accept its absence.
        ''' <para>
        ''' Both cells below are produced by <c>Binder.CreateConversionAndReportDiagnostic</c> - the very method
        ''' whose <c>Debug.Assert</c> at <c>Binder_Conversions.vb:442</c> is the registered failure. The
        ''' implicit <c>String</c> to <c>Integer</c> narrowing under <c>Option Strict On</c> reports
        ''' BC30512, and the explicit <c>CInt</c> of the same pair reports nothing: the two differ only in
        ''' <c>isExplicit</c>, which is the flag the assert also reads. Pinning the exact id, severity and
        ''' wording of the first, and the absence of any for the second, means a fix that changed which metadata
        ''' symbol this conversion is classified against, or that dropped, reworded or added one of them, is
        ''' caught here even though no test of the submission path would notice.
        ''' </para>
        ''' </summary>
        '<Fact>
        Public Sub OrdinaryConversions_KeepTheirExactDiagnostics()
            Dim compilation = CreateCompilationWithMscorlib45AndVBRuntime(
                <compilation name="Conversions">
                    <file name="a.vb"><![CDATA[
Option Strict On

Public Module Converted
    Public Function ImplicitNarrowing() As Integer
        Dim text As String = "x"
        Return text
    End Function

    Public Function ExplicitNarrowing() As Integer
        Dim text As String = "x"
        Return CInt(text)
    End Function
End Module
]]></file>
                </compilation>,
                options:=TestOptions.DebugDll)

            AssertDiagnosticSet(
                New String() {
                    "BC30512: error: Option Strict On disallows implicit conversions from 'String' to 'Integer'."
                },
                compilation)
        End Sub
    End Class
End Namespace
