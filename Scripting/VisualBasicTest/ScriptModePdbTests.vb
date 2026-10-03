' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' U7 (design-detailed.md section U7; test-plan section 3 U7): debug information, and the file/line/column the
' runtime reports for a frame of a script.
'
' C# baseline: {{Roslyn}}\src\Scripting\CSharpTest\ScriptTests.cs, the Pdb_* family. Counted on the baseline
' itself (`grep -n "Pdb_" CSharpTest/ScriptTests.cs`) the family is TWELVE methods, not the "2x2x2 matrix + one
' no encoding cell" the plan describes. The twelve are, in file order:
'   Pdb_CreateFromString_CodeFromFile_WithEmitDebugInformation_WithoutFileEncoding_CompilationErrorException
'   Pdb_CreateFromString_CodeFromFile_WithEmitDebugInformation_WithFileEncoding_ResultInPdbEmitted
'   Pdb_CreateFromString_CodeFromFile_WithoutEmitDebugInformation_WithoutFileEncoding_ResultInPdbNotEmitted
'   Pdb_CreateFromString_CodeFromFile_WithoutEmitDebugInformation_WithFileEncoding_ResultInPdbNotEmitted
'   Pdb_CreateFromStream_CodeFromFile_WithEmitDebugInformation_ResultInPdbEmitted
'   Pdb_CreateFromStream_CodeFromFile_WithoutEmitDebugInformation_ResultInPdbNotEmitted
'   Pdb_CreateFromString_InlineCode_WithEmitDebugInformation_WithoutFileEncoding_ResultInPdbEmitted
'   Pdb_CreateFromString_InlineCode_WithEmitDebugInformation_WithFileEncoding_ResultInPdbEmitted
'   Pdb_CreateFromString_InlineCode_WithoutEmitDebugInformation_WithoutFileEncoding_ResultInPdbNotEmitted
'   Pdb_CreateFromString_InlineCode_WithoutEmitDebugInformation_WithFileEncoding_ResultInPdbNotEmitted
'   Pdb_CreateFromStream_InlineCode_WithEmitDebugInformation_ResultInPdbEmitted
'   Pdb_CreateFromStream_InlineCode_WithoutEmitDebugInformation_ResultInPdbNotEmitted
' The axes are (string | stream) x (code from a file | inline code) x (emit debug information | not); the
' missing-encoding cell is the first of the four "code from a file" string cells, so the counts per group are
' 4 + 2 + 4 + 2 = 12, not 8 + 1. Cells 1 to 12 below are one for one with those twelve methods, in the same
' order. Cell 13 has no C# counterpart: it is the #Load row of test-plan section 3 U7, which is what this unit
' exists for in this fork (see that cell's own comment).
'
' What the C# cells actually assert
' ---------------------------------
' The C# baseline asserts a real stack frame: its VerifyStackTraceAsync (ScriptTests.cs, `VerifyStackTraceAsync`)
' runs the script, catches the exception and asserts firstFrame.GetFileName() / GetFileLineNumber() /
' GetFileColumnNumber(). It is not "does not throw", and it is not a file on disk either: the executor emits
' the PE and the PDB into MemoryStreams (Scripting\Core\ScriptBuilder.cs, `Build`, the two `using` streams at
' :130-133) and hands both to the in-memory assembly context
' (Scripting\Core\Hosting\AssemblyLoader\CoreAssemblyLoaderImpl.cs, `LoadFromStream`), which is why nothing here
' writes a PDB file. This file makes the same assertion, so the file/line/column the runtime reports is the
' subject of every emitting cell rather than a proxy for it.
'
' The cells whose subject is the emit itself additionally emit into memory directly, through the production
' emit options (Scripting\Core\ScriptBuilder.cs, `GetEmitOptions` at :168-169 - the very call `Build` makes at
' :133). The PE is then read back with PEReader and its CodeView entry checked, which is the in-memory half of
' what the C# baseline's TestEmit_PortablePdb delegates to PdbValidation.ValidateDebugDirectory
' ({{Roslyn}}\src\Test\PdbUtilities\Reader\PdbValidation.cs, `ValidateDebugDirectory`, :563).
'
' Pdb_PortablePdb_DebugDirectoryMatchesThePdb is the other half and is the C# baseline's own cell for it
' (ScriptTests.cs, `TestEmit`, :74): it ports ValidateDebugDirectory's assertions one by one, so the entry in the
' PE and the PDB stream are compared with each other instead of each being checked on its own. It is the cell
' that corresponds to `TestEmit_PortablePdb` (:75) - the `TestEmit_WindowsPdb` variant (:78) has no counterpart
' here and cannot have one on this host; that cell's summary states why.
'
' Where the two options land - and one divergence
' ----------------------------------------------
' design-detailed.md section U7 asks for "EmitDebugInformation / FileEncoding landing on
' VisualBasicCompilationOptions" to be asserted. Read against the source neither lands there, and the cells
' assert what does happen:
'   * EmitDebugInformation never reaches the compilation: CreateSubmission builds VisualBasicCompilationOptions
'     (Scripting\VisualBasic\VisualBasicScriptCompiler.vb, `CreateSubmission`, :212-229) without reading it. It
'     is read by Script.Execute (Scripting\Core\Script.cs, `GetExecutor` at :361-365) and becomes EmitOptions
'     through ScriptBuilder.GetEmitOptions. Asserting it on the compilation would assert something absent.
'   * FilePath reaches the syntax tree (CreateSubmission, :183) and FileEncoding reaches it when the script was
'     created from a Stream (VisualBasicScript.vb, `Create(Of T)`, :39) - but NOT when it was created from a
'     string (:29 passes no encoding to SourceText.From).
' The second half of that is a DIVERGENCE from the C# blueprint, and every string cell below is written
' against the fork's actual behaviour rather than against the C# expectation:
'   * C# CSharpScript.cs:37 passes options?.FileEncoding for the string form as well, so
'     Pdb_CreateFromString_CodeFromFile_WithEmitDebugInformation_WithFileEncoding_ResultInPdbEmitted (:859)
'     emits a PDB and gets a frame naming debug.csx.
'   * In VB the same shape reports BC37236, because the tree has a path and no encoding and that is exactly the
'     debug document gate (Compilers\Core\Portable\Compilation\Compilation.cs, `CreateDebugDocuments`, :2512;
'     the condition at :2518, the report at :2520).
' The asymmetry is inherited, not introduced by this fork: upstream VB has no stream overloads at all and its
' string overload is the same `SourceText.From(If(code, String.Empty))` (compared against
' {{Roslyn}}\src\Scripting\VisualBasic\VisualBasicScript.vb, `Create(Of T)`). Nothing in the product sets
' WithFileEncoding either - it is a public API axis for external hosts - so no product path is affected.
' Cells 2 and 8 pin the asymmetry so it cannot change silently; see cell 2 for the tripwire note.
'
' VB versus C#
' ------------
' Apart from that one asymmetry the two languages agree cell by cell: the same frame values (line 1, column 1
' for a one line statement, the script path when the script has one, the empty string when it does not, no
' file information at all when no PDB was emitted), and the same per-tree debug document gate. The only other
' difference is the identifier of the missing-encoding error, which is per language: C# reports CS8055
' (Compilers\CSharp\Portable\Errors\ErrorCode.cs, ERR_EncodinglessSyntaxTree), VB reports BC37236
' (Compilers\VisualBasic\Portable\Errors\Errors.vb, `ERR_EncodinglessSyntaxTree`, :1688).

Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Reflection.Metadata
Imports System.Reflection.PortableExecutable
Imports System.Text
Imports System.Threading
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Emit
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting
Imports Xunit

''' <summary>
''' The Pdb_* family of the C# baseline, one cell per method, plus the #Load cell of test-plan section 3 U7.
''' Every cell is in memory: the script, the emit and the frame it produces. No file, directory, process,
''' registry entry or network call is involved.
''' </summary>
Public Class ScriptModePdbTests

    ''' <summary>
    ''' The single statement every cell runs. Its sequence point covers the whole statement, so the frame the
    ''' runtime reports for it is the cell's subject: line 1, column 1. The C# baseline's script is
    ''' <c>throw new System.Exception();</c> and asserts the same three values (ScriptTests.cs,
    ''' `VerifyStackTraceAsync`).
    ''' </summary>
    Private Const ThrowingCode As String = "Throw New System.Exception()"

    ''' <summary>
    ''' The file path the "code from a file" cells give the script. It is a name the script carries, never a
    ''' file this process opens: nothing in this file touches the file system.
    ''' </summary>
    Private Const ScriptPath As String = "debug.vbx"

    ''' <summary>
    ''' BC37236, Compilers\VisualBasic\Portable\Errors\Errors.vb, `ERR_EncodinglessSyntaxTree` (:1688). The C#
    ''' baseline pins the same behaviour with CS8055 (`Diagnostic(ErrorCode.ERR_EncodinglessSyntaxTree, code)`).
    ''' </summary>
    Private Const EncodinglessSyntaxTreeId As String = "BC37236"

#Region "options"

    ''' <summary>
    ''' The two switches the C# baseline varies, plus the file path. <paramref name="filePath"/> is passed
    ''' straight to WithFilePath so that a cell can hand the script no path at all (the "inline code" cells) and
    ''' cell 3 can mirror the baseline's <c>WithFilePath(null)</c> exactly.
    ''' </summary>
    Private Shared Function OptionsFor(emitDebugInformation As Boolean, filePath As String, fileEncoding As Encoding) As ScriptOptions
        Return ScriptModeConformance.DefaultOptions.
            WithEmitDebugInformation(emitDebugInformation).
            WithFilePath(filePath).
            WithFileEncoding(fileEncoding)
    End Function

#End Region

#Region "shape of the compilation"

    ''' <summary>
    ''' The shape half of the section U7 requirement: the file path and, on the stream form, the encoding both
    ''' reach the compilation's one and only tree. Asserting the path here is what lets the cells below say why a
    ''' frame carries the path it carries, and it makes a cell fail when the option stops travelling instead of
    ''' silently turning into "no file information" (which would look like a pass in the not-emitting cells).
    ''' <c>FilePath</c> is compared after a null coalesce because a script created without a path may carry
    ''' either Nothing or the empty string; both mean "no path" to the debug document pass.
    ''' </summary>
    Private Shared Function AssertTreeShape(script As Script(Of Object), expectedFilePath As String, encodingIsPresent As Boolean) As SyntaxTree
        Dim tree = Assert.Single(script.GetCompilation().SyntaxTrees)

        Assert.Equal(expectedFilePath, If(tree.FilePath, ""))
        If encodingIsPresent Then
            Assert.NotNull(tree.GetText().Encoding)
        Else
            Assert.Null(tree.GetText().Encoding)
        End If

        Return tree
    End Function

#End Region

#Region "in memory emit"

    ''' <summary>
    ''' Emits the script's compilation into memory with the emit options production uses. When
    ''' <paramref name="emitDebugInformation"/> is true a PDB stream is passed, which is exactly the decision
    ''' ScriptBuilder.Build makes (Scripting\Core\ScriptBuilder.cs, `Build`, :131) - and therefore also the
    ''' decision that makes the compiler produce debug documents at all
    ''' (Compilers\Core\Portable\Compilation\Compilation.cs, `Emit`, :2992).
    ''' </summary>
    Private Shared Function EmitInMemory(script As Script(Of Object),
                                         emitDebugInformation As Boolean,
                                         ByRef peStream As MemoryStream,
                                         ByRef pdbStream As MemoryStream) As EmitResult
        peStream = New MemoryStream()
        pdbStream = If(emitDebugInformation, New MemoryStream(), Nothing)

        Return ScriptBuilder.Emit(peStream, pdbStream, script.GetCompilation(),
                                  ScriptBuilder.GetEmitOptions(emitDebugInformation), CancellationToken.None)
    End Function

    ''' <summary>
    ''' The in-memory counterpart of the C# baseline's <c>PdbValidation.ValidateDebugDirectory</c> call in
    ''' TestEmit_PortablePdb: the PE must carry a portable CodeView entry naming the PDB the compilation is
    ''' expected to produce. Reading it back is what turns "the emit succeeded" into "a PDB was produced".
    ''' </summary>
    Private Shared Sub AssertPeNamesThePortablePdb(compilation As Compilation, peStream As MemoryStream)
        peStream.Position = 0

        Using reader = New PEReader(peStream, PEStreamOptions.LeaveOpen)
            Dim codeView = reader.ReadDebugDirectory().Single(Function(entry) entry.Type = DebugDirectoryEntryType.CodeView)

            Assert.True(codeView.IsPortableCodeView, "the debug directory entry has to be a portable PDB entry")
            Assert.Equal(compilation.AssemblyName & ".pdb", reader.ReadCodeViewDebugDirectoryData(codeView).Path)
        End Using
    End Sub

    ''' <summary>
    ''' Asserts that the missing-encoding error is reported once, as an error, on the given file and line, and
    ''' that the host receives that same diagnostic when it runs the script. The host half is what makes the
    ''' cell about the observable behaviour rather than about the emit call alone: the executor emits, adds the
    ''' emit diagnostics and rethrows them (ScriptBuilder.cs, `CreateExecutor`, :88-91).
    ''' </summary>
    Private Shared Sub AssertEncodinglessSyntaxTreeReported(script As Script(Of Object),
                                                            result As EmitResult,
                                                            expectedFilePath As String,
                                                            expectedLine As Integer)
        Assert.False(result.Success, "a tree that cannot be made debuggable cannot be emitted")

        Dim reported = result.Diagnostics.Where(Function(d) d.Id = EncodinglessSyntaxTreeId).ToArray()
        Assert.Equal(1, reported.Length)
        Assert.Equal(DiagnosticSeverity.Error, reported(0).Severity)
        Assert.Equal(expectedFilePath, reported(0).Location.GetLineSpan().Path)
        Assert.Equal(expectedLine, reported(0).Location.GetLineSpan().StartLinePosition.Line + 1)

        Dim failure = Assert.Throws(Of CompilationErrorException)(
            Sub() script.RunAsync().GetAwaiter().GetResult())
        Assert.Contains(failure.Diagnostics, Function(d) d.Id = EncodinglessSyntaxTreeId)
    End Sub

    ''' <summary>The names of the documents of an in-memory portable PDB, used by the #Load cell.</summary>
    Private Shared Function PdbDocumentNames(pdbStream As MemoryStream) As String()
        pdbStream.Position = 0

        Using provider = MetadataReaderProvider.FromPortablePdbStream(pdbStream, MetadataStreamOptions.LeaveOpen)
            Dim reader = provider.GetMetadataReader()
            Return reader.Documents.Select(Function(handle) reader.GetString(reader.GetDocument(handle).Name)).ToArray()
        End Using
    End Function

#End Region

#Region "the debug directory of the emitted PE"

    ''' <summary>
    ''' The in-memory port of the C# baseline's <c>TestEmit_PortablePdb</c> (ScriptTests.cs, `TestEmit`, :74) and of
    ''' what it delegates to: <c>PdbValidation.ValidateDebugDirectory</c>
    ''' ({{Roslyn}}\src\Test\PdbUtilities\Reader\PdbValidation.cs, `ValidateDebugDirectory`, :563-639). The emitting
    ''' cells above check that the PE carries a portable CodeView entry naming the PDB; this cell is the half this
    ''' file was missing - the entry and the PDB stream have to describe the same artifact.
    ''' <para>
    ''' The baseline asserts the same things through the same production emit options
    ''' (ScriptBuilder.GetEmitOptions(emitDebugInformation:=True), :168-169, the call ScriptBuilder.Emit makes at
    ''' :172-186), so the two languages are compared on one code path rather than on two hand built option sets.
    ''' </para>
    ''' <para>
    ''' Why it can fail: every assertion names a different way for the PE and the PDB to disagree. The CodeView
    ''' entry has to be the portable flavour (major 0x0100, minor 0x504D) - a native PDB entry carries the other
    ''' pair; the identity has to hold - <c>pdbReader.DebugMetadataHeader.Id</c> is the GUID and the stamp the PE
    ''' advertises, so an emit that hands out a PDB other than the one the entry names fails; <c>Age</c> has to be
    ''' 1; the path has to be padded to the 260 byte target (PeWriter.PadPdbPath, :367-371) because this compilation
    ''' is not deterministic (<c>CompilationOptions.Deterministic</c> defaults to false and no ScriptOptions surface
    ''' sets it), which is why the baseline's other branch - the exact length assertion - is not the one used here;
    ''' and the directory has to hold that single entry, so a PDB checksum entry, a reproducible entry or an
    ''' embedded PDB would each make the count wrong.
    ''' </para>
    ''' <para>
    ''' The C# baseline's <c>TestEmit_WindowsPdb</c> ([ConditionalFact(WindowsOnly)], :78) has no counterpart here
    ''' and cannot have one: the emit options are built once, from
    ''' <c>PdbHelpers.GetPlatformSpecificDebugInformationFormat</c> (Scripting\Core\ScriptBuilder.cs, :51-53), and
    ''' that helper returns PortablePdb whenever CoreCLR or Mono is loaded
    ''' (Scripting\Core\Utilities\PdbHelpers.cs, `GetPlatformSpecificDebugInformationFormat`, :14-24). No other
    ''' options object reaches the emit, so on this host the format is fixed at PortablePdb and the Windows axis is
    ''' unreachable rather than untested. The portable assertions below are what pin that fixed choice.
    ''' </para>
    ''' </summary>
    <Fact>
    Public Sub Pdb_PortablePdb_DebugDirectoryMatchesThePdb()
        Dim script = VisualBasicScript.Create("1 + 2", OptionsFor(True, Nothing, Nothing))
        Dim compilation = script.GetCompilation()
        Assert.False(compilation.Options.Deterministic, "the padded path length below is the non deterministic branch")

        Dim pe As MemoryStream = Nothing
        Dim pdb As MemoryStream = Nothing
        Dim result = EmitInMemory(script, emitDebugInformation:=True, peStream:=pe, pdbStream:=pdb)

        Assert.True(result.Success, "the emit reported: " & String.Join("; ", result.Diagnostics.Select(Function(d) d.ToString())))

        pe.Position = 0
        Using reader = New PEReader(pe, PEStreamOptions.LeaveOpen)
            Dim entries = reader.ReadDebugDirectory()
            Assert.Equal(1, entries.Length)

            Dim codeViewEntry = entries(0)
            Assert.Equal(DebugDirectoryEntryType.CodeView, codeViewEntry.Type)
            Assert.Equal(CInt(&H100), CInt(codeViewEntry.MajorVersion))
            Assert.Equal(CInt(&H504D), CInt(codeViewEntry.MinorVersion))

            Dim codeViewData = reader.ReadCodeViewDebugDirectoryData(codeViewEntry)
            Assert.Equal(1, codeViewData.Age)
            Assert.Equal(compilation.AssemblyName & ".pdb", codeViewData.Path)

            ' CodeView data layout: "RSDS" (4) + GUID (16) + Age (4) + NUL terminated path.
            Dim paddedPathLength = CInt(codeViewEntry.DataSize) - 24
            Assert.True(paddedPathLength >= 260, "the path field has to be padded to MAX_PATH, was " & paddedPathLength)

            pdb.Position = 0
            Using provider = MetadataReaderProvider.FromPortablePdbStream(pdb, MetadataStreamOptions.LeaveOpen)
                Dim pdbReader = provider.GetMetadataReader()

                Assert.Equal(New BlobContentId(codeViewData.Guid, codeViewEntry.Stamp),
                             New BlobContentId(pdbReader.DebugMetadataHeader.Id))
            End Using
        End Using
    End Sub

#End Region

#Region "the frame"

    ''' <summary>
    ''' Runs a script, and returns the first frame of the exception it threw with file information requested.
    ''' This is the C# baseline's assertion verbatim: <c>new StackTrace(ex, needFileInfo: true)</c> followed by
    ''' <c>GetFrames()[0]</c> (ScriptTests.cs, `VerifyStackTraceAsync`). The symbols behind that frame are the
    ''' in-memory PDB stream the executor loaded, so reaching a file and a line here is the whole point of the
    ''' cell - an empty frame and a frame with a wrong file both fail loudly with the raw stack trace attached.
    ''' </summary>
    Private Shared Function FirstFrameOf(script As Script(Of Object)) As StackFrame
        Try
            script.RunAsync().GetAwaiter().GetResult()
        Catch ex As Exception
            Dim frames = New StackTrace(ex, True).GetFrames()

            Assert.True(frames IsNot Nothing AndAlso frames.Length > 0,
                        "the script threw but the runtime reported no frame: " & Environment.NewLine & ex.ToString())
            Return frames(0)
        End Try

        Assert.True(False, "the script was expected to throw")
        Return Nothing
    End Function

    ''' <summary>An emitting cell: the frame has to name the file the script was given.</summary>
    Private Shared Sub AssertFrameNamesTheScriptFile(frame As StackFrame)
        Assert.Equal(ScriptPath, frame.GetFileName())
        Assert.Equal(1, frame.GetFileLineNumber())
        Assert.Equal(1, frame.GetFileColumnNumber())
    End Sub

    ''' <summary>An emitting cell for a script that was given no file path.</summary>
    Private Shared Sub AssertFrameNamesTheEmptyPath(frame As StackFrame)
        Assert.Equal("", frame.GetFileName())
        Assert.Equal(1, frame.GetFileLineNumber())
        Assert.Equal(1, frame.GetFileColumnNumber())
    End Sub

    ''' <summary>
    ''' A not-emitting cell: with no PDB there is nothing to map an instruction back to, so the frame has to
    ''' report the absence rather than a stale or invented path. The C# baseline asserts exactly these three
    ''' values for its four *NotEmitted cells (its VerifyStackTraceAsync defaults: filename null, line 0,
    ''' column 0).
    ''' </summary>
    Private Shared Sub AssertFrameHasNoFileInformation(frame As StackFrame)
        Assert.Null(frame.GetFileName())
        Assert.Equal(0, frame.GetFileLineNumber())
        Assert.Equal(0, frame.GetFileColumnNumber())
    End Sub

#End Region

#Region "code from a file - the string form"

    ''' <summary>
    ''' Baseline: Pdb_CreateFromString_CodeFromFile_WithEmitDebugInformation_WithoutFileEncoding_
    ''' CompilationErrorException (ScriptTests.cs, :842).
    ''' <para>
    ''' Why it can fail: the diagnostic has to be BC37236 and not just any error, it has to be anchored on the
    ''' script's own path and first line - which is where the gate reports it (Compilation.cs,
    ''' `CreateDebugDocuments`, :2520, on the tree root's location) - and the host has to receive that same
    ''' diagnostic. Cell 7 below holds the same absent encoding with no path and has to stay green, so dropping
    ''' the <c>Not String.IsNullOrEmpty(tree.FilePath)</c> half of the condition at :2518 turns exactly one of
    ''' the two cells red.
    ''' </para>
    ''' </summary>
    <Fact>
    Public Sub Pdb_String_CodeFromFile_WithDebugInformation_WithoutEncoding_ReportsBC37236()
        Dim options = OptionsFor(True, ScriptPath, Nothing)
        Dim script = VisualBasicScript.Create(ThrowingCode, options)
        AssertTreeShape(script, ScriptPath, encodingIsPresent:=False)

        Dim pe As MemoryStream = Nothing
        Dim pdb As MemoryStream = Nothing
        Dim result = EmitInMemory(script, emitDebugInformation:=True, peStream:=pe, pdbStream:=pdb)

        AssertEncodinglessSyntaxTreeReported(script, result, ScriptPath, 1)
    End Sub

    ''' <summary>
    ''' Baseline: Pdb_CreateFromString_CodeFromFile_WithEmitDebugInformation_WithFileEncoding_
    ''' ResultInPdbEmitted (ScriptTests.cs, :859) - where the encoding makes the emit succeed and the frame name
    ''' is debug.csx.
    ''' <para>
    ''' VB now matches C#: the string form of Create hands ScriptOptions.FileEncoding to SourceText.From
    ''' (VisualBasicScript.vb, `Create(Of T)`, :29), so the option reaches the tree and the debug document gate
    ''' (Compilation.cs, `CreateDebugDocuments`, :2518) does not fire. This is the fixed path that used to report
    ''' BC37236; the assertion mirrors the stream-positive cell (encoding present on the tree + a PDB that names
    ''' the script file). Cell 1 (same shape, encoding Nothing) is the counter-lock that keeps BC37236.
    ''' </para>
    ''' </summary>
    <Fact>
    Public Sub Pdb_String_CodeFromFile_WithDebugInformation_WithEncoding_ResultInPdbEmitted()
        Dim options = OptionsFor(True, ScriptPath, Encoding.UTF8)
        Dim script = VisualBasicScript.Create(ThrowingCode, options)

        Assert.Same(Encoding.UTF8, options.FileEncoding)
        AssertTreeShape(script, ScriptPath, encodingIsPresent:=True)

        Dim pe As MemoryStream = Nothing
        Dim pdb As MemoryStream = Nothing
        Dim result = EmitInMemory(script, emitDebugInformation:=True, peStream:=pe, pdbStream:=pdb)

        Assert.True(result.Success, "the emit reported: " & String.Join("; ", result.Diagnostics.Select(Function(d) d.ToString())))
        Assert.True(pdb.Length > 0, "the PDB stream has to carry the portable PDB")
        AssertPeNamesThePortablePdb(script.GetCompilation(), pe)

        AssertFrameNamesTheScriptFile(FirstFrameOf(script))
    End Sub

    ''' <summary>
    ''' C3 - the two factories must be equivalent under the same options, not just "both non-null". The same
    ''' <c>ScriptOptions</c> (path + UTF8 + debug) is fed to the string and stream forms of Create; the resulting
    ''' single tree's text must report the <em>same</em> Encoding and the <em>same</em> FilePath on both sides.
    ''' Discriminating: if the string overload stopped forwarding FileEncoding the two <c>Encoding</c> values would
    ''' differ (one UTF8, one Nothing) and the equality breaks; if the path stopped travelling the FilePath
    ''' equality breaks.
    ''' </summary>
    <Fact>
    Public Sub FileEncoding_StringAndStreamFactories_ProduceEquivalentSourceText()
        Dim options = OptionsFor(True, ScriptPath, Encoding.UTF8)

        Dim stringScript = VisualBasicScript.Create(ThrowingCode, options)
        Dim streamScript = VisualBasicScript.Create(New MemoryStream(Encoding.UTF8.GetBytes(ThrowingCode)), options)

        Dim fromString = Assert.Single(stringScript.GetCompilation().SyntaxTrees).GetText()
        Dim fromStream = Assert.Single(streamScript.GetCompilation().SyntaxTrees).GetText()

        Assert.Equal(fromStream.Encoding, fromString.Encoding)
        Assert.Same(Encoding.UTF8, fromString.Encoding)
        Assert.Equal(fromStream.ToString(), fromString.ToString())
    End Sub

    ''' <summary>
    ''' C5 - the other string entry point must honour FileEncoding the same way (same implementation, no second
    ''' unfixed <c>SourceText.From</c>). A continuation built through <c>Script.ContinueWith(String, options)</c>
    ''' with UTF8 + path carries the encoding on its tree. Discriminating: were ContinueWith(String) the one place
    ''' still dropping the option, the tree's encoding would be Nothing and this cell turns red.
    ''' </summary>
    <Fact>
    Public Sub ContinueWith_StringForm_HonoursFileEncoding()
        Dim baseScript = VisualBasicScript.Create("Dim q = 1", ScriptModeConformance.DefaultOptions)
        Dim continued = baseScript.ContinueWith("?", OptionsFor(True, ScriptPath, Encoding.UTF8))

        Dim tree = Assert.Single(continued.GetCompilation().SyntaxTrees)
        Assert.Equal(ScriptPath, tree.FilePath)
        Assert.NotNull(tree.GetText().Encoding)
    End Sub

    ''' <summary>
    ''' Baseline: Pdb_CreateFromString_CodeFromFile_WithoutEmitDebugInformation_WithoutFileEncoding_
    ''' ResultInPdbNotEmitted (ScriptTests.cs, :866). The path is cleared as well as the encoding, which is what
    ''' the baseline does and what makes this cell different from cell 1: the same absent encoding is harmless
    ''' here precisely because no debug information is requested.
    ''' <para>
    ''' Why it can fail: the frame has to be bare. If the executor emitted the PDB regardless of
    ''' Options.EmitDebugInformation (Script.cs, `GetExecutor`, :365 passes it to CreateExecutor), this cell
    ''' would report <c>debug.vbx</c> or an invented name and fail.
    ''' </para>
    ''' </summary>
    <Fact>
    Public Sub Pdb_String_CodeFromFile_WithoutDebugInformation_WithoutEncoding_FrameHasNoFileInformation()
        Dim script = VisualBasicScript.Create(ThrowingCode, OptionsFor(False, Nothing, Nothing))
        AssertTreeShape(script, "", encodingIsPresent:=False)

        AssertFrameHasNoFileInformation(FirstFrameOf(script))
    End Sub

    ''' <summary>
    ''' Baseline: Pdb_CreateFromString_CodeFromFile_WithoutEmitDebugInformation_WithFileEncoding_
    ''' ResultInPdbNotEmitted (ScriptTests.cs, :873). This is the pair of cell 2: the path and the encoding
    ''' option are both set and the frame still has to be bare, so emitting debug information - not the encoding
    ''' - is what decides. (The encoding option is set and, since the factory now honours it, does reach the
    ''' string source - but with emit debug off the frame stays bare regardless.)
    ''' </para>
    ''' </summary>
    <Fact>
    Public Sub Pdb_String_CodeFromFile_WithoutDebugInformation_WithEncoding_FrameHasNoFileInformation()
        Dim options = OptionsFor(False, ScriptPath, Encoding.UTF8)
        Dim script = VisualBasicScript.Create(ThrowingCode, options)

        Assert.Same(Encoding.UTF8, options.FileEncoding)
        AssertTreeShape(script, ScriptPath, encodingIsPresent:=True)

        AssertFrameHasNoFileInformation(FirstFrameOf(script))
    End Sub

#End Region

#Region "code from a file - the stream form"

    ''' <summary>
    ''' Baseline: Pdb_CreateFromStream_CodeFromFile_WithEmitDebugInformation_ResultInPdbEmitted
    ''' (ScriptTests.cs, :881).
    ''' <para>
    ''' The stream form has no encoding argument in the baseline because the decoded text carries its own: the
    ''' factory hands ScriptOptions.FileEncoding to SourceText.From, and SourceText fills in a UTF-8 without BOM
    ''' when nothing is passed (VisualBasicScript.vb, `Create(Of T)`, :39;
    ''' Compilers\Core\Portable\Text\SourceText.cs, `From(Stream, Encoding, ...)`, :201). That single difference
    ''' from the string form is what makes this cell the fork's positive path for a named file, and the
    ''' assertion is on the encoding being present at all, since none was asked for.
    ''' </para>
    ''' <para>
    ''' Why it can fail: three independent layers have to hold - the tree must carry the path and an encoding,
    ''' the emit must produce a PE whose CodeView entry names the PDB, and the loaded PDB must map the frame
    ''' back to <c>debug.vbx</c> line 1 column 1. Cell 6 is its pair: same everything with the emit switch off,
    ''' and the frame has to be bare.
    ''' </para>
    ''' </summary>
    <Fact>
    Public Sub Pdb_Stream_CodeFromFile_WithDebugInformation_FrameNamesTheScriptFile()
        Dim script = VisualBasicScript.Create(New MemoryStream(Encoding.UTF8.GetBytes(ThrowingCode)),
                                              OptionsFor(True, ScriptPath, Nothing))
        AssertTreeShape(script, ScriptPath, encodingIsPresent:=True)

        Dim pe As MemoryStream = Nothing
        Dim pdb As MemoryStream = Nothing
        Dim result = EmitInMemory(script, emitDebugInformation:=True, peStream:=pe, pdbStream:=pdb)

        Assert.True(result.Success, "the emit reported: " & String.Join("; ", result.Diagnostics.Select(Function(d) d.ToString())))
        Assert.True(pdb.Length > 0, "the PDB stream has to carry the portable PDB")
        AssertPeNamesThePortablePdb(script.GetCompilation(), pe)

        AssertFrameNamesTheScriptFile(FirstFrameOf(script))
    End Sub

    ''' <summary>
    ''' Baseline: Pdb_CreateFromStream_CodeFromFile_WithoutEmitDebugInformation_ResultInPdbNotEmitted
    ''' (ScriptTests.cs, :888). The path is still there, so the cell isolates the emit switch on the stream axis
    ''' the same way cell 4 does it on the string axis.
    ''' </summary>
    <Fact>
    Public Sub Pdb_Stream_CodeFromFile_WithoutDebugInformation_FrameHasNoFileInformation()
        Dim script = VisualBasicScript.Create(New MemoryStream(Encoding.UTF8.GetBytes(ThrowingCode)),
                                              OptionsFor(False, ScriptPath, Nothing))

        AssertFrameHasNoFileInformation(FirstFrameOf(script))
    End Sub

#End Region

#Region "inline code - the string form"

    ''' <summary>
    ''' Baseline: Pdb_CreateFromString_InlineCode_WithEmitDebugInformation_WithoutFileEncoding_
    ''' ResultInPdbEmitted (ScriptTests.cs, :896). The baseline asserts <c>filename: ""</c>.
    ''' <para>
    ''' This is the cell that separates the two halves of the gate at Compilation.cs:2518: no path, no encoding,
    ''' and the emit still has to succeed because the <c>Not String.IsNullOrEmpty(tree.FilePath)</c> half is
    ''' false. Take that half out and this cell reports BC37236 like cell 1.
    ''' </para>
    ''' </summary>
    <Fact>
    Public Sub Pdb_String_InlineCode_WithDebugInformation_WithoutEncoding_FrameNamesTheEmptyPath()
        Dim script = VisualBasicScript.Create(ThrowingCode, OptionsFor(True, "", Nothing))
        AssertTreeShape(script, "", encodingIsPresent:=False)

        Dim pe As MemoryStream = Nothing
        Dim pdb As MemoryStream = Nothing
        Dim result = EmitInMemory(script, emitDebugInformation:=True, peStream:=pe, pdbStream:=pdb)

        Assert.True(result.Success, "the emit reported: " & String.Join("; ", result.Diagnostics.Select(Function(d) d.ToString())))
        Assert.True(pdb.Length > 0, "the PDB stream has to carry the portable PDB")
        AssertPeNamesThePortablePdb(script.GetCompilation(), pe)

        AssertFrameNamesTheEmptyPath(FirstFrameOf(script))
    End Sub

    ''' <summary>
    ''' Baseline: Pdb_CreateFromString_InlineCode_WithEmitDebugInformation_WithFileEncoding_ResultInPdbEmitted
    ''' (ScriptTests.cs, :904). The baseline asserts <c>filename: ""</c> here too, and so does this cell.
    ''' <para>
    ''' The encoding option is set and (the factory now honouring it) reaches the source; what this cell adds over
    ''' cell 7 is the pairing of "an encoding was requested" with "the absent path is still what the frame
    ''' reports", instead of the message being the frame's name. The debug document gate still does not fire
    ''' because there is no path.
    ''' </para>
    ''' </summary>
    <Fact>
    Public Sub Pdb_String_InlineCode_WithDebugInformation_WithEncoding_FrameNamesTheEmptyPath()
        Dim options = OptionsFor(True, "", Encoding.UTF8)
        Dim script = VisualBasicScript.Create(ThrowingCode, options)

        Assert.Same(Encoding.UTF8, options.FileEncoding)
        AssertTreeShape(script, "", encodingIsPresent:=True)

        Dim pe As MemoryStream = Nothing
        Dim pdb As MemoryStream = Nothing
        Dim result = EmitInMemory(script, emitDebugInformation:=True, peStream:=pe, pdbStream:=pdb)

        Assert.True(result.Success, "the emit reported: " & String.Join("; ", result.Diagnostics.Select(Function(d) d.ToString())))
        Assert.True(pdb.Length > 0, "the PDB stream has to carry the portable PDB")
        AssertPeNamesThePortablePdb(script.GetCompilation(), pe)

        AssertFrameNamesTheEmptyPath(FirstFrameOf(script))
    End Sub

    ''' <summary>
    ''' Baseline: Pdb_CreateFromString_InlineCode_WithoutEmitDebugInformation_WithoutFileEncoding_
    ''' ResultInPdbNotEmitted (ScriptTests.cs, :911). Both switches are off, so the frame has to be bare.
    ''' </summary>
    <Fact>
    Public Sub Pdb_String_InlineCode_WithoutDebugInformation_WithoutEncoding_FrameHasNoFileInformation()
        Dim script = VisualBasicScript.Create(ThrowingCode, OptionsFor(False, "", Nothing))
        AssertTreeShape(script, "", encodingIsPresent:=False)

        AssertFrameHasNoFileInformation(FirstFrameOf(script))
    End Sub

    ''' <summary>
    ''' Baseline: Pdb_CreateFromString_InlineCode_WithoutEmitDebugInformation_WithFileEncoding_
    ''' ResultInPdbNotEmitted (ScriptTests.cs, :918). The encoding is present and the frame is still bare: on
    ''' every axis the emit switch is the only thing that decides.
    ''' </summary>
    <Fact>
    Public Sub Pdb_String_InlineCode_WithoutDebugInformation_WithEncoding_FrameHasNoFileInformation()
        Dim script = VisualBasicScript.Create(ThrowingCode, OptionsFor(False, "", Encoding.UTF8))
        AssertTreeShape(script, "", encodingIsPresent:=True)

        AssertFrameHasNoFileInformation(FirstFrameOf(script))
    End Sub

#End Region

#Region "inline code - the stream form"

    ''' <summary>
    ''' Baseline: Pdb_CreateFromStream_InlineCode_WithEmitDebugInformation_ResultInPdbEmitted
    ''' (ScriptTests.cs, :926). No path and no encoding argument, so the only thing this cell can attribute the
    ''' frame to is the decoded stream's own encoding (SourceText.cs, `From(Stream, Encoding, ...)`, :201).
    ''' </summary>
    <Fact>
    Public Sub Pdb_Stream_InlineCode_WithDebugInformation_FrameNamesTheEmptyPath()
        Dim script = VisualBasicScript.Create(New MemoryStream(Encoding.UTF8.GetBytes(ThrowingCode)),
                                              OptionsFor(True, "", Nothing))

        Dim pe As MemoryStream = Nothing
        Dim pdb As MemoryStream = Nothing
        Dim result = EmitInMemory(script, emitDebugInformation:=True, peStream:=pe, pdbStream:=pdb)

        Assert.True(result.Success, "the emit reported: " & String.Join("; ", result.Diagnostics.Select(Function(d) d.ToString())))
        Assert.True(pdb.Length > 0, "the PDB stream has to carry the portable PDB")
        AssertPeNamesThePortablePdb(script.GetCompilation(), pe)

        AssertFrameNamesTheEmptyPath(FirstFrameOf(script))
    End Sub

    ''' <summary>
    ''' Baseline: Pdb_CreateFromStream_InlineCode_WithoutEmitDebugInformation_ResultInPdbNotEmitted
    ''' (ScriptTests.cs, :933). The last of the twelve, and the last pair: cell 11 emits and this one does not,
    ''' with nothing else different.
    ''' </summary>
    <Fact>
    Public Sub Pdb_Stream_InlineCode_WithoutDebugInformation_FrameHasNoFileInformation()
        Dim script = VisualBasicScript.Create(New MemoryStream(Encoding.UTF8.GetBytes(ThrowingCode)),
                                              OptionsFor(False, "", Nothing))

        AssertFrameHasNoFileInformation(FirstFrameOf(script))
    End Sub

#End Region

#Region "#Load - the resolved file keeps its own document and its own lines"

    ''' <summary>
    ''' test-plan section 3 U7, the #Load row, and the reason this unit exists in this fork: the row mapping of
    ''' loaded trees is the area this fork has had a bug in (issues\issue-vbx-load-span-shift.md - the old
    ''' text-inlining host turned a main.vbx line 3 diagnostic into line 7 and reported loaded.vbx's own
    ''' diagnostics against main.vbx). The debug information layer is one way to lock that regression down, not
    ''' the only one - ScriptTests.vb `TestLoadDirectiveDoesNotShiftDiagnosticSpan` (:526) and
    ''' `TestLoadedFileDiagnosticsUseRealFileAndLine` (:551) lock the diagnostic layer, and this cell is not a
    ''' second copy of them: those two assert the diagnostic anchor, this one asserts what the PDB says about
    ''' the same tree.
    ''' <para>
    ''' The loaded tree is a syntax tree of its own with its own FilePath
    ''' (Scripting\VisualBasic\VisualBasicScriptCompiler.vb, `CollectLoadTrees` at :88; the loaded tree is parsed
    ''' at :122 with <c>resolvedPath</c>), and loaded trees are added before the main file so their top level code
    ''' runs first (`CreateSubmission`, :213-230). Both facts are observable in the frame: the throw sits on line
    ''' 2 of the loaded file, so the frame has to name that file and that line. Under the old inlining this frame
    ''' named main.vbx with a shifted line - which is what this cell is built to catch.
    ''' <para>
    ''' (The line anchors above track the current working tree; `CollectLoadTrees` and the tree assembly
    ''' moved when #Load gained once semantics.)
    ''' </para>
    ''' <para>
    ''' Why it can fail: the loaded file's name, its line number and the document table are three separate
    ''' checks, and the line number is the one the old bug moved. A host that inlined the loaded text, or that
    ''' parsed it against the main file's path, fails on all three; a host that executed the trees in the other
    ''' order throws from main.vbx instead and fails on the file.
    ''' </para>
    ''' <para>
    ''' One detail the cell would be meaningless without: the debug document gate at
    ''' Compilation.cs:2518 applies per tree, so every tree in the compilation needs an encoding. The shared
    ''' ScriptModeConformance.MemorySourceReferenceResolver returns unencoded text - right for the span cells,
    ''' which run without debug information - and the string form of Create drops the option as described in the
    ''' file header; both would turn this cell into BC37236 on one of the two trees. So the main script is
    ''' created from a stream (the form that carries an encoding) and the resolver below hands the loaded file
    ''' its own encoding, which is what a file backed resolver does when it reads a real script file.
    ''' </para>
    ''' </summary>
    <Fact>
    Public Sub Pdb_LoadedFileKeepsItsOwnFileAndLineInTheFrame()
        Const mainPath As String = "C:\scripts\main.vbx"
        Const loadedPath As String = "C:\scripts\loaded.vbx"

        Dim files As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From
        {
            {loadedPath, "Dim loadedValue As Integer = 1" & vbCrLf &
                          ThrowingCode},
            {mainPath, "#Load ""loaded.vbx""" & vbCrLf &
                       "Dim mainValue As Integer = 2"}
        }

        Dim options = OptionsFor(True, mainPath, Encoding.UTF8).
            WithSourceResolver(New EncodedMemorySourceReferenceResolver(files))
        Dim script = VisualBasicScript.Create(New MemoryStream(Encoding.UTF8.GetBytes(files(mainPath))), options)

        ' Loaded trees come first; the main file is last.
        Dim trees = script.GetCompilation().SyntaxTrees.ToArray()
        Assert.Equal(2, trees.Length)
        Assert.Equal(loadedPath, If(trees(0).FilePath, ""))
        Assert.Equal(mainPath, If(trees(1).FilePath, ""))

        Dim pe As MemoryStream = Nothing
        Dim pdb As MemoryStream = Nothing
        Dim result = EmitInMemory(script, emitDebugInformation:=True, peStream:=pe, pdbStream:=pdb)

        Assert.True(result.Success, "the emit reported: " & String.Join("; ", result.Diagnostics.Select(Function(d) d.ToString())))
        Assert.Equal(New String() {loadedPath, mainPath},
                     PdbDocumentNames(pdb).OrderBy(Function(name) name, StringComparer.Ordinal).ToArray())

        Dim frame = FirstFrameOf(script)
        Assert.Equal(loadedPath, frame.GetFileName())
        Assert.Equal(2, frame.GetFileLineNumber())
        Assert.Equal(1, frame.GetFileColumnNumber())
    End Sub

    ''' <summary>
    ''' Resolves <c>#Load</c> targets from memory and gives each loaded file's text its own encoding. The shared
    ''' <see cref="ScriptModeConformance.MemorySourceReferenceResolver"/> cannot be reused here: it returns
    ''' unencoded text, which the debug document pass rejects per tree (Compilation.cs, `CreateDebugDocuments`,
    ''' :2518) - see the cell above. Nothing is written to disk; the "paths" are dictionary keys.
    ''' </summary>
    Private NotInheritable Class EncodedMemorySourceReferenceResolver
        Inherits SourceReferenceResolver

        Private ReadOnly _files As IReadOnlyDictionary(Of String, String)

        Public Sub New(files As IReadOnlyDictionary(Of String, String))
            _files = files
        End Sub

        Public Overrides Function NormalizePath(path As String, baseFilePath As String) As String
            Return If(ResolveReference(path, baseFilePath), path)
        End Function

        Public Overrides Function ResolveReference(path As String, baseFilePath As String) As String
            If _files.ContainsKey(path) Then
                Return path
            End If

            If baseFilePath IsNot Nothing Then
                Dim combined = IO.Path.Combine(If(IO.Path.GetDirectoryName(baseFilePath), ""), path)
                If _files.ContainsKey(combined) Then
                    Return combined
                End If
            End If

            Return Nothing
        End Function

        Public Overrides Function OpenRead(resolvedPath As String) As Stream
            Return New MemoryStream(Encoding.UTF8.GetBytes(_files(resolvedPath)))
        End Function

        Public Overrides Function ReadText(resolvedPath As String) As SourceText
            Return SourceText.From(_files(resolvedPath), Encoding.UTF8)
        End Function

        Public Overrides Function Equals(other As Object) As Boolean
            Return ReferenceEquals(Me, other)
        End Function

        Public Overrides Function GetHashCode() As Integer
            Return _files.Count
        End Function
    End Class

#End Region

End Class
