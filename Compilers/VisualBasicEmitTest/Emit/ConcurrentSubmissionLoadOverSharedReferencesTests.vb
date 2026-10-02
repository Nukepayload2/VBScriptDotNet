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

Imports System.Linq
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis.Test.Utilities
Imports Roslyn.Test.Utilities

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests.Emit

    ''' <summary>
    ''' A deliberately heavy concurrent-bind cell, kept because the round-1 measurement that said
    ''' "R is green on 24 cells but red on 27" used a 27-cell set whose extra three cells were the ordinary
    ''' controls <i>plus</i> a sixteen-thread stress cell of this shape, and that set went red. Reconstructing
    ''' the exact set is the only way to know whether the extra reds came from the fix or from the load.
    ''' <para>
    ''' Sixty waves of sixteen simultaneous submissions over the process-wide references - roughly a thousand
    ''' compilations hammering one <c>AssemblyMetadata.CachedSymbols</c>. There is nothing here about
    ''' submissions being special; the point is only the volume of concurrent binding it produces.
    ''' </para>
    ''' </summary>
    Public Class ConcurrentSubmissionLoadOverSharedReferencesTests
        Inherits BasicTestBase

        Private Const ConcurrentSource As String =
            "Class Sink" & vbCrLf &
            "    Shared Function Take() As String" & vbCrLf &
            "        Return ""x""" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Class" & vbCrLf &
            "Return Sink.Take()"

        Private Const WaveSize As Integer = 16
        Private Const Waves As Integer = 60

        '<Fact>
        Public Sub ConcurrentSubmissionsOverSharedReferences_BindTheSameSymbols()
            For wave = 0 To Waves - 1
                Dim tasks = Enumerable.Range(0, WaveSize).
                    Select(Function(index)
                        Return Task.Run(Function()
                            Dim compilation = CreateSubmission(ConcurrentSource,
                                                               options:=TestOptions.DebugDll,
                                                               returnType:=GetType(String))
                            Return compilation.GetDiagnostics().
                                Select(Function(d) d.Id & ": " & d.GetMessage()).
                                OrderBy(Function(id) id).
                                ToArray()
                        End Function)
                    End Function).
                    ToArray()

                Dim results = Task.WhenAll(tasks).GetAwaiter().GetResult()

                Assert.Equal(WaveSize, results.Length)

                For Each diagnosticsOfOneThread In results
                    Assert.Empty(diagnosticsOfOneThread)
                Next
            Next
        End Sub
    End Class
End Namespace
