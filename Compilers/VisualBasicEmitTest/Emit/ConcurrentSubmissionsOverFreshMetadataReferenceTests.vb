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
Imports System.Threading
Imports Microsoft.CodeAnalysis.Test.Utilities
Imports Roslyn.Test.Utilities

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests.Emit

    ''' <summary>
    ''' Two compilations in one process must end up with <b>one</b> symbol per assembly.
    ''' <para>
    ''' A <see cref="MetadataReference"/> owns an <c>AssemblyMetadata</c>, and the symbols built over it are
    ''' cached process-wide in <c>AssemblyMetadata.CachedSymbols</c> precisely so that every compilation
    ''' referencing the same metadata shares them: a submission and its previous submission, two tests running
    ''' in parallel, an IDE with background analysis and a foreground build all depend on that. The reference
    ''' manager looked the cache up while holding <c>SymbolCacheAndReferenceManagerStateGuard</c> but created
    ''' and published a missing symbol with the guard released in between, so two threads could each decide
    ''' that no symbol existed, each create its own <c>PEAssemblySymbol</c>, and each publish it.
    ''' </para>
    ''' <para>
    ''' Two symbols for one assembly are not by themselves a failure - they only do damage when the same type
    ''' is reached through both of them <i>inside one compilation</i>. The comparison at
    ''' <c>Binder_Conversions.vb:442</c> is then given two different <c>System.String</c>s: equal by
    ''' <c>SpecialType</c>, so <c>Conversions</c> calls the conversion an identity one, and not equal by
    ''' <c>IsSameTypeIgnoringAll</c>, which is what the assert requires. A submission chain is the shape that
    ''' does that: the later link binds the earlier link's declarations through one reference manager and its
    ''' own body through another, so if those two managers did not agree on the corlib symbol, the later link
    ''' is holding two corlibs at once.
    ''' </para>
    ''' <para>
    ''' So this cell does three things the ordinary "run many tests in parallel" cells cannot do.
    ''' <list type="bullet">
    ''' <item>It builds its own reference objects, whose symbol caches start out <i>empty</i>, so the window
    ''' between the cache lookup and the publication is guaranteed to be open when the wave starts instead of
    ''' having been closed by whichever compilation happened to run first.</item>
    ''' <item>It runs each thread on its own thread released by a gate, not on a thread pool that is still
    ''' growing threads one at a time. The window is microseconds wide; serialising the work hides it. This
    ''' was measured: the <c>Task.Run</c> form of this cell stays green on the unfixed code.</item>
    ''' <item>Each thread builds a two-link submission chain, so the two symbol sets a race can produce are
    ''' actually compared with each other. Independent submissions never meet, and a cell built from them
    ''' passes on the unfixed code even though the duplicates are created every single wave.</item>
    ''' </list>
    ''' </para>
    ''' </summary>
    Public Class ConcurrentSubmissionsOverFreshMetadataReferenceTests
        Inherits BasicTestBase

        ''' <summary>
        ''' The first link: it declares the trace sink and the shared event. A type the script declares is used
        ''' as the sink so that reading it never initializes the submission class.
        ''' </summary>
        Private Const OwnerSource As String =
            "Class Sink" & vbCrLf &
            "    Shared Trace As String = """"" & vbCrLf &
            "    Shared Sub Log(s As String)" & vbCrLf &
            "        Trace = Trace & s" & vbCrLf &
            "    End Sub" & vbCrLf &
            "    Shared Function Take() As String" & vbCrLf &
            "        Return Trace" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Class" & vbCrLf &
            "Shared Event Ev As System.EventHandler" & vbCrLf &
            "Return 0"

        ''' <summary>
        ''' The second link. The <c>Handles</c> clause resolves <c>Ev</c> - and with it
        ''' <c>System.EventHandler</c> - through the first link's compilation, while the body of the submission
        ''' and the submission's own return type bind through this link's. That is the meeting point.
        ''' </summary>
        Private Const HandlerSource As String =
            "Shared Sub H(s As Object, e As System.EventArgs) Handles Me.Ev" & vbCrLf &
            "    Sink.Log(""H"")" & vbCrLf &
            "End Sub" & vbCrLf &
            "Return Sink.Take()"

        Private Const WaveSize As Integer = 16

        ''' <summary>
        ''' One wave would do; the cell repeats so that a green result is a statement about many attempts
        ''' rather than about one lucky interleaving. The count is fixed rather than retried, so a failure is a
        ''' failure and not a flake.
        ''' </summary>
        Private Const Waves As Integer = 8

        ''' <summary>
        ''' A reference over the same metadata image as <paramref name="processWide"/>, but over an
        ''' <c>AssemblyMetadata</c> of its own, so its process-wide symbol cache starts empty. Sharing the
        ''' image matters: the two references describe the same assembly identity, so the only thing that can
        ''' make two threads disagree about "is there a symbol for this assembly?" is the cache.
        ''' </summary>
        Private Shared Function ReferenceWithEmptySymbolCache(processWide As PortableExecutableReference) As PortableExecutableReference
            Dim processWideMetadata = DirectCast(processWide.GetMetadataNoCopy(), AssemblyMetadata)
            Dim freshMetadata = processWideMetadata.CopyWithoutSharingCachedSymbols()

            ' The premise of the cell, asserted rather than assumed: if this ever stops being zero the cell
            ' has quietly stopped testing the race and has become a load test.
            Assert.Equal(0, freshMetadata.CachedSymbols.WeakCount)

            Return freshMetadata.GetReference(filePath:=processWide.FilePath, display:=processWide.Display)
        End Function

        '<Fact>
        Public Sub ConcurrentSubmissionsOverOneMetadataReference_BindTheSameSymbols()
            For wave = 0 To Waves - 1
                ' A brand new pair of references per wave, so every wave starts from an empty symbol cache.
                Dim references = New MetadataReference() {
                    ReferenceWithEmptySymbolCache(DirectCast(MscorlibRef_v4_0_30316_17626, PortableExecutableReference)),
                    ReferenceWithEmptySymbolCache(DirectCast(MsvbRef_v4_0_30319_17929, PortableExecutableReference))
                }

                Dim startGate = New ManualResetEventSlim(False)
                Dim results(WaveSize - 1) As String
                Dim threads(WaveSize - 1) As Thread

                For index = 0 To WaveSize - 1
                    Dim captured = index

                    threads(index) = New Thread(Sub()
                                                 startGate.Wait()
                                                 results(captured) = CompileChainAndCollectDiagnostics(references)
                                             End Sub)
                    threads(index).IsBackground = True
                Next

                For Each t In threads
                    t.Start()
                Next

                startGate.Set()

                For Each t In threads
                    Assert.True(t.Join(TimeSpan.FromMinutes(2)), "a wave thread did not finish")
                Next

                For Each diagnosticsOfOneThread In results
                    Assert.NotNull(diagnosticsOfOneThread)
                    Assert.Equal("", diagnosticsOfOneThread)
                Next
            Next
        End Sub

        ''' <summary>
        ''' Builds a two-link submission chain over the given references and returns the whole diagnostic set
        ''' of the second link, rendered as one "id: message" string per diagnostic. Both links are checked:
        ''' the owner link is where the first symbol for the metadata gets created, and the handler link is
        ''' where a second one would be compared against it.
        ''' </summary>
        Private Shared Function CompileChainAndCollectDiagnostics(references As MetadataReference()) As String
            Dim owner = VisualBasicCompilation.CreateScriptCompilation(
                GetUniqueName(),
                syntaxTree:=Parse(OwnerSource, options:=TestOptions.Script),
                references:=references,
                options:=TestOptions.DebugDll,
                returnType:=GetType(String))

            Dim handler = VisualBasicCompilation.CreateScriptCompilation(
                GetUniqueName(),
                syntaxTree:=Parse(HandlerSource, options:=TestOptions.Script),
                references:=references,
                options:=TestOptions.DebugDll,
                previousScriptCompilation:=owner,
                returnType:=GetType(String))

            Return RenderDiagnostics(handler)
        End Function

        Private Shared Function RenderDiagnostics(compilation As Compilation) As String
            Return String.Join(" | ",
                compilation.GetDiagnostics().
                    Select(Function(d) d.Id & ": " & d.GetMessage()).
                    OrderBy(Function(id) id).
                    ToArray())
        End Function
    End Class
End Namespace
