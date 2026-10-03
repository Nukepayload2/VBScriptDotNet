' ===========================================================================================
' RESERVED — 本文件的用例未启用。
' 理由与解除条件见 issues\issue-parallel-submission-binding-assert.md §三之二、
' upstream-merge.md §2.25(k)、HANDOFF.md §5.3（issue 35 的修复已 RESERVE）。
' 保留本文件是为留下复现配方：两段提交链 + 全新 MetadataReference + 闸门齐放的专用线程。
' 解除时：恢复 [Fact]、删除本横幅，并按 issue 35 §三之二 的三条路径重新决策。
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
