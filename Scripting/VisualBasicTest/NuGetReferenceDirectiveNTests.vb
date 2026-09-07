' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' 本 fork 有意偏离上游：以下测试锁定共享 Core「单 #R → N 引用」收口（design-detailed.md §A2，
' U9 spike 两场景的 in-memory 改造）。fixture 程序集全部经 VisualBasicCompilation.Create + Emit
' 到 MemoryStream，再 CreateFromStream 转 MetadataReference；不做任何磁盘写 / 进程 / 网络。

Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.IO
Imports System.Linq
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting
Imports Xunit

Public Class NuGetReferenceDirectiveNTests

    Private Shared ReadOnly s_corLibReference As MetadataReference =
        MetadataReference.CreateFromFile(GetType(Object).Assembly.Location)

    Private Shared ReadOnly s_vbRuntimeReference As MetadataReference =
        MetadataReference.CreateFromFile(GetType(Microsoft.VisualBasic.Strings).Assembly.Location)

    ''' <summary>
    ''' Compiles the given VB source into an in-memory assembly and returns a stream-backed
    ''' metadata reference. No file is written.
    ''' </summary>
    Private Shared Function CreateFixture(assemblyName As String, source As String,
                                          Optional additionalReferences As IEnumerable(Of MetadataReference) = Nothing) As PortableExecutableReference
        Dim syntaxTree = SyntaxFactory.ParseSyntaxTree(source)
        Dim references = New List(Of MetadataReference) From {s_corLibReference, s_vbRuntimeReference}
        If additionalReferences IsNot Nothing Then
            references.AddRange(additionalReferences)
        End If

        Dim compilation = VisualBasicCompilation.Create(
            assemblyName,
            {syntaxTree},
            references,
            New VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, rootNamespace:=""))

        Using peStream = New MemoryStream()
            Dim emitResult = compilation.Emit(peStream)
            Assert.True(emitResult.Success, String.Join(Environment.NewLine, emitResult.Diagnostics))
            peStream.Position = 0
            Return MetadataReference.CreateFromStream(peStream)
        End Using
    End Function

    ' PkgClosure is the dependency ("closure") assembly: it is only ever referenced by script source
    ' in a later submission, never by the first submission's source.
    Private Shared Function CreateClosureFixture() As PortableExecutableReference
        Return CreateFixture("PkgClosure", "
Namespace PkgClosure
    Public Class ClosureType
        Public Function Answer() As Integer
            Return 42
        End Function
    End Class
End Namespace")
    End Function

    ' PkgMain is the primary asset. Its Hello() member references PkgClosure.ClosureType so the
    ' fixture mirrors a NuGet package whose dependency closure must be supplied by the resolver.
    Private Shared Function CreateMainFixture(closureReference As MetadataReference) As PortableExecutableReference
        Return CreateFixture("PkgMain", "
Namespace PkgMain
    Public Class MainType
        Public Function Hello() As String
            Return ""answer="" & New PkgClosure.ClosureType().Answer().ToString()
        End Function
    End Class
End Namespace", {closureReference})
    End Function

    ' A valid, but otherwise unused, in-memory assembly backing an ordinary #R (non-nuget) directive.
    Private Shared Function CreateExtraFixture() As PortableExecutableReference
        Return CreateFixture("PkgExtra", "
Namespace PkgExtra
    Public Class ExtraType
        Public Function Ping() As Integer
            Return 1
        End Function
    End Class
End Namespace")
    End Function

    ' A MemoryStream filled with non-PE garbage. Reading it as metadata raises a BadImageFormat
    ' exception that the reference manager turns into BC31519 anchored at the owning #R directive.
    Private Shared Function CreateGarbagePeReference(seed As Integer) As PortableExecutableReference
        Dim garbageBytes(4095) As Byte
        For i As Integer = 0 To garbageBytes.Length - 1
            garbageBytes(i) = CByte((i * 31 + 7 + seed) And &HFF)
        Next
        Return MetadataReference.CreateFromStream(New MemoryStream(garbageBytes))
    End Function

    Private Shared Sub AssertNoErrors(diagnostics As IEnumerable(Of Diagnostic), label As String)
        Dim errors = diagnostics.Where(Function(d) d.Severity = DiagnosticSeverity.Error).ToArray()
        If errors.Length > 0 Then
            Assert.True(False, label & " has errors:" & vbCrLf &
                String.Join(vbCrLf, errors.Select(Function(d) d.ToString())))
        End If
    End Sub

    ''' <summary>
    ''' Test 1 (U9-i): a single "#R "nuget:X"" expands to [primary asset, closure]; the closure
    ''' assembly is only referenced by source in the second submission, and the N references flow to
    ''' that submission through ExplicitReferences (CommonReferenceManager.Resolution.cs).
    ''' </summary>
    <Fact>
    Public Sub NuGetDirective_ExpandedReferences_AreInheritedAcrossSubmissions()
        Dim closurePe = CreateClosureFixture()
        Dim mainPe = CreateMainFixture(closurePe)

        Dim resolver As MetadataReferenceResolver = New NuGetMultiAssetResolver(
            New Dictionary(Of String, ImmutableArray(Of PortableExecutableReference)) From
            {
                {"X", ImmutableArray.Create(mainPe, closurePe)}
            },
            New Dictionary(Of String, PortableExecutableReference)())

        Dim options = ScriptOptions.Default.WithMetadataResolver(resolver)

        Dim code1 = "#R ""nuget:X""" & vbCrLf &
                    "Dim m As New PkgMain.MainType()" & vbCrLf &
                    "Dim mainLen As Integer = m.Hello().Length"

        Dim code2 = "Dim c As New PkgClosure.ClosureType()" & vbCrLf &
                    "Dim ans As Integer = c.Answer()"

        Dim submission1 = VisualBasicScript.Create(code1, options)
        AssertNoErrors(submission1.GetCompilation().GetDiagnostics(), "Submission 1")

        ' The closure type is visible in submission 2 even though no #R appears here: the expanded
        ' references of submission 1 are inherited as explicit references by the continuation.
        Dim submission2 = submission1.ContinueWith(code2)
        AssertNoErrors(submission2.GetCompilation().GetDiagnostics(), "Submission 2")
    End Sub

    ''' <summary>
    ''' Test 2 (U9-ii): two "#R "nuget:…"" directives on different lines each expand to a valid
    ''' primary asset plus a garbage reference, and a plain #R is interleaved. Each garbage metadata
    ''' read failure (BC31519) must anchor to its own #R line, not to Location.None and not to the
    ''' other directive's line.
    ''' </summary>
    <Fact>
    Public Sub NuGetDirective_BadExpandedReferences_AnchorToTheirOwnDirectiveLine()
        Dim closurePe = CreateClosureFixture()
        Dim mainPe = CreateMainFixture(closurePe)
        Dim extraPe = CreateExtraFixture()
        Dim garbage1 = CreateGarbagePeReference(seed:=1)
        Dim garbage2 = CreateGarbagePeReference(seed:=2)

        Dim resolver As MetadataReferenceResolver = New NuGetMultiAssetResolver(
            New Dictionary(Of String, ImmutableArray(Of PortableExecutableReference)) From
            {
                {"MainPkg", ImmutableArray.Create(mainPe, garbage1)},
                {"ClosurePkg", ImmutableArray.Create(closurePe, garbage2)}
            },
            New Dictionary(Of String, PortableExecutableReference) From
            {
                {"extra.dll", extraPe}
            })

        Dim options = ScriptOptions.Default.WithMetadataResolver(resolver)

        Dim code = "#R ""nuget:MainPkg""" & vbCrLf &
                   "#R ""nuget:ClosurePkg""" & vbCrLf &
                   "#R ""extra.dll""" & vbCrLf &
                   "Dim m As New PkgMain.MainType()" & vbCrLf &
                   "Dim mainLen As Integer = m.Hello().Length" & vbCrLf &
                   "Dim c As New PkgClosure.ClosureType()" & vbCrLf &
                   "Dim ans As Integer = c.Answer()"

        Dim diagnostics = VisualBasicScript.Create(code, options).GetCompilation().GetDiagnostics()

        ' Both garbage assets must produce their own metadata-read failure; every other error would
        ' indicate the valid main/closure/plain assets failed to bind.
        Dim badImageErrors = diagnostics.Where(Function(d) d.Id = "BC31519").ToArray()
        Dim otherErrors = diagnostics.Where(Function(d) d.Severity = DiagnosticSeverity.Error AndAlso d.Id <> "BC31519").ToArray()

        Assert.Equal(2, badImageErrors.Length)
        Assert.Empty(otherErrors)

        Dim lines = badImageErrors.
            Select(Function(d) d.Location.GetLineSpan().StartLinePosition.Line).
            OrderBy(Function(line) line).
            ToArray()

        For Each diagnostic In badImageErrors
            Assert.NotEqual(Location.None, diagnostic.Location)
        Next

        Assert.Equal(0, lines(0))
        Assert.Equal(1, lines(1))
    End Sub

    ''' <summary>
    ''' Resolver that expands a "#R "nuget:&lt;name&gt;"" reference into multiple PE references
    ''' (primary asset followed by its closure) and resolves a small set of ordinary reference
    ''' strings to in-memory PE references. Everything else is delegated to
    ''' <see cref="ScriptMetadataResolver.Default"/>.
    ''' </summary>
    Private NotInheritable Class NuGetMultiAssetResolver
        Inherits MetadataReferenceResolver

        Private ReadOnly _packageAssets As IReadOnlyDictionary(Of String, ImmutableArray(Of PortableExecutableReference))
        Private ReadOnly _fileAssets As IReadOnlyDictionary(Of String, PortableExecutableReference)
        Private ReadOnly _runtime As MetadataReferenceResolver = ScriptMetadataResolver.Default

        Public Sub New(
            packageAssets As IReadOnlyDictionary(Of String, ImmutableArray(Of PortableExecutableReference)),
            fileAssets As IReadOnlyDictionary(Of String, PortableExecutableReference))
            _packageAssets = packageAssets
            _fileAssets = fileAssets
        End Sub

        Public Overrides ReadOnly Property ResolveMissingAssemblies As Boolean
            Get
                Return _runtime.ResolveMissingAssemblies
            End Get
        End Property

        Public Overrides Function ResolveMissingAssembly(definition As MetadataReference, referenceIdentity As AssemblyIdentity) As PortableExecutableReference
            Return _runtime.ResolveMissingAssembly(definition, referenceIdentity)
        End Function

        Public Overrides Function ResolveReference(reference As String, baseFilePath As String, properties As MetadataReferenceProperties) As ImmutableArray(Of PortableExecutableReference)
            If reference.StartsWith("nuget:", StringComparison.Ordinal) Then
                Dim assets As ImmutableArray(Of PortableExecutableReference) = Nothing
                If _packageAssets.TryGetValue(reference.Substring("nuget:".Length), assets) Then
                    Return assets
                End If
                Return ImmutableArray(Of PortableExecutableReference).Empty
            End If

            Dim fileReference As PortableExecutableReference = Nothing
            If _fileAssets.TryGetValue(reference, fileReference) Then
                Return ImmutableArray.Create(fileReference)
            End If

            Return _runtime.ResolveReference(reference, baseFilePath, properties)
        End Function

        Public Overrides Function Equals(obj As Object) As Boolean
            Return ReferenceEquals(Me, obj)
        End Function

        Public Overrides Function GetHashCode() As Integer
            Return 0
        End Function
    End Class

End Class
