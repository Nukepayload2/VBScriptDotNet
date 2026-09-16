' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' U4: reference aliases (<host> / <implicit>). The normative source is `spec\spec-reference-directive.md`,
' section "Reference aliases": the paragraph that defines the merge predicate, the two aliases the scripting host
' applies, the "No escape hatch" rule and the "Ordinary compilation uses the same rule" clause. Every anchor
' below was verified against this revision and carries both a line and the symbol name: the line pins the
' revision, the name is what survives a reformatting of the production file. A range written as `file:N-M` is
' the full span of the named member; a bare `:N` is a point inside it.
'   Scripting\Core\Script.cs:238-239, field `HostAssemblyReferenceProperties` (the <host> alias and the
'     recursive flag); Script.cs:245-304, method `GetReferencesForCompilation`, which applies it to the
'     globalsType assembly (`:254` the `Previous == null` branch, `:259` the globals-type branch, `:267` the
'     aliased reference it adds).
'   Scripting\Core\Hosting\Resolvers\RuntimeMetadataReferenceResolver.cs:28-29, field
'     `s_resolvedMissingAssemblyReferenceProperties` (the <implicit> alias); `:100` `ResolveMissingAssembly`,
'     `:115` the trusted-platform-assembly substitution that applies the properties, and `:140-143`
'     `CreateResolvedMissingReference`, which attaches them to a substituted assembly.
'   Compilers\Core\Portable\ReferenceManager\CommonReferenceManager.State.cs:713-719, method
'     `GetReferencedAssemblyAliases` (the observable view), and `:721-725` `DeclarationsAccessibleWithoutAlias`
'     (the predicate: an assembly merges only when its alias set is empty or contains `global`).
'   Compilers\VisualBasic\Portable\Symbols\MergedNamespaceSymbol.vb:102-120, method
'     `ConstituentGlobalNamespaces` - the VB alias filter (`:114` calls `DeclarationsAccessibleWithoutAlias`)
'     that keeps an aliased assembly's global namespace out of the merge.
'   Compilers\VisualBasic\Portable\CommandLine\VisualBasicCommandLineParser.vb:1612-1664, method
'     `LoadCoreLibraryReference`; `:1633` attaches the non-global "RoslynCoreLibrary" alias to the real core
'     library under /nostdlib, which is the ordinary-compilation half of the same rule.
'
' Mechanism. These cells exercise the compiler side of "why is this name not visible": the merged namespace
' drops the aliased assembly's global namespace (`MergedNamespaceSymbol.vb`). They are not the cells of
' `ImportsAccumulationFailureTests.vb`, which exercise the host side of the same symptom (the host replays an
' `Imports` clause on every submission). The visible symptom is the same, the mechanism is not.
'
' Discriminative argument. The alias set is asserted where it is produced (cell 6 pins the host assembly's set to
' exactly <host>, cell 4 pins a substituted assembly's to <implicit>), and the source level consequence of the
' alias being absent is asserted in the same file: cell 3 pairs every rejected form with the very same form
' against the very same assembly added without an alias, which binds; cell 4 and cell 5 each pair their rejected
' name with the same name against an unaliased reference, which binds. So dropping `WithAliases` /
' `WithRecursiveAliases` from `Script.cs`'s `HostAssemblyReferenceProperties` would flip the pinned alias set and
' the outcome of cells 1, 3 and 5 together, without any cell re-implementing the predicate:
' `DeclarationsAccessibleWithoutAlias` is called, not copied. Cell 8 is the one cell that reads the warning half
' of the diagnostic set, and its alias regression detector is the arity rather than the identity: if the alias
' were not applied, the name would resolve and the `Imports` would produce nothing at all, so "exactly one
' warning" is what turns red. Its remaining assertions - the same ID, the same message template, one
' interpolated name apart, reported at the imported name - do not detect a broken alias; they are the anchors
' of the sentence cell 8 quotes, which states exactly those four identities of the two warnings.
'
' Cell 8 also anchors the delivery half of the sentence that follows in the same section: "the warning is
' produced, not necessarily delivered: a script entry point surfaces a submission's warnings only when that
' submission has no compilation errors". It is the only cell here whose outcome depends on a non-error
' diagnostic reaching the script entry point's own surface: `VisualBasicScript.Compile` hands back the
' submission's warnings as the whole set (`Script.cs:340`), while for a submission with errors the set it hands
' back holds errors only (`ScriptBuilder.cs:103-117`), so the "only when" holds and a host that dropped warnings
' would leave cells 1 to 7 green.
'
' Everything is in memory: fixtures are emitted to a MemoryStream, references are stream or metadata backed, and
' no file, process, registry or network access happens (test-plan section 2). The only file reads are already
' loaded assemblies that the process is running from, which is the existing `AssemblyMetadata.CreateFromImage`
' fixture pattern of this test project.

Imports System
Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.IO
Imports System.Linq
Imports System.Text
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting
Imports HostAliasFixture
Imports Xunit

''' <summary>
''' U4: the <c>&lt;host&gt;</c> and <c>&lt;implicit&gt;</c> reference aliases. One cell per judgement the
''' "Reference aliases" section of <c>spec\spec-reference-directive.md</c> makes, plus the ordinary
''' compilation cell of its "Ordinary compilation uses the same rule" clause.
''' </summary>
Public Class ScriptModeReferenceAliasTests

#Region "plumbing"

    Private Const HostAlias As String = "<host>"
    Private Const ImplicitAlias As String = "<implicit>"

    ' The alias `LoadCoreLibraryReference` attaches to the real core library under /nostdlib.
    Private Const CoreLibraryAlias As String = "RoslynCoreLibrary"

    Private Shared Function Lines(ParamArray text As String()) As String
        Return String.Join(vbCrLf, text)
    End Function

    Private Shared Function ErrorDiagnostics(diagnostics As ImmutableArray(Of Diagnostic)) As Diagnostic()
        Return diagnostics.Where(Function(d) d.Severity = DiagnosticSeverity.Error).ToArray()
    End Function

    Private Shared Function ErrorIds(diagnostics As ImmutableArray(Of Diagnostic)) As String()
        Return ErrorDiagnostics(diagnostics).Select(Function(d) d.Id).OrderBy(Function(id) id, StringComparer.Ordinal).ToArray()
    End Function

    ''' <summary>The name/alias view of the reference set: the enumeration the merge predicate walks.</summary>
    Private Shared Function ReferenceAliases(compilation As Compilation) As List(Of (Name As String, Aliases As ImmutableArray(Of String)))
        Dim result = New List(Of (Name As String, Aliases As ImmutableArray(Of String)))()
        For Each entry In compilation.GetBoundReferenceManager().GetReferencedAssemblyAliases()
            result.Add((entry.AssemblySymbol.Identity.Name, entry.Aliases))
        Next

        Return result
    End Function

    Private Shared Function AliasesOf(compilation As Compilation, assemblyName As String) As ImmutableArray(Of String)
        For Each entry In ReferenceAliases(compilation)
            If entry.Name = assemblyName Then
                Return entry.Aliases
            End If
        Next

        Return ImmutableArray(Of String).Empty
    End Function

    ''' <summary>
    ''' The merge predicate itself, called rather than re-implemented. Nothing means the assembly is not in the
    ''' reference set at all.
    ''' </summary>
    Private Shared Function DeclarationsAccessibleWithoutAlias(compilation As Compilation, assemblyName As String) As Boolean?
        Dim manager = DirectCast(compilation.GetBoundReferenceManager(),
                                 CommonReferenceManager(Of VisualBasicCompilation, VisualBasic.Symbols.AssemblySymbol))
        Dim entries = manager.GetReferencedAssemblyAliases().ToArray()
        For index = 0 To entries.Length - 1
            If entries(index).AssemblySymbol.Identity.Name = assemblyName Then
                Return manager.DeclarationsAccessibleWithoutAlias(index)
            End If
        Next

        Return Nothing
    End Function

    Private Shared Function Report(label As String, source As String, diagnostics As ImmutableArray(Of Diagnostic),
                                   Optional aliases As IEnumerable(Of (Name As String, Aliases As ImmutableArray(Of String))) = Nothing) As String
        Dim builder = New StringBuilder()
        builder.AppendLine(label).AppendLine("--- source ---")
        For Each line In source.Replace(vbCrLf, vbLf).Split(ControlChars.Lf)
            builder.AppendLine("| " & line)
        Next

        builder.AppendLine("--- diagnostics ---")
        If diagnostics.IsDefaultOrEmpty Then
            builder.AppendLine("(none)")
        Else
            For Each diagnostic In diagnostics
                builder.AppendLine($"{diagnostic.Id} {diagnostic.Severity}: {diagnostic.GetMessage()}")
            Next
        End If

        If aliases IsNot Nothing Then
            builder.AppendLine("--- reference aliases ---")
            For Each entry In aliases
                builder.AppendLine(entry.Name & " => [" & String.Join(", ", entry.Aliases) & "]")
            Next
        End If

        Return builder.ToString()
    End Function

#End Region

#Region "fixtures"

    Private Shared ReadOnly s_corLibReference As MetadataReference =
        MetadataReference.CreateFromFile(GetType(Object).Assembly.Location)

    Private Shared ReadOnly s_vbRuntimeReference As MetadataReference =
        MetadataReference.CreateFromFile(GetType(Microsoft.VisualBasic.Strings).Assembly.Location)

    ''' <summary>
    ''' Compiles VB source into an in-memory assembly and returns a stream backed reference. No file is written;
    ''' the shape follows the existing in-memory fixture helpers of this test project.
    ''' </summary>
    Private Shared Function CreateFixture(assemblyName As String, source As String,
                                          Optional additionalReferences As IEnumerable(Of MetadataReference) = Nothing) As PortableExecutableReference
        Dim references = New List(Of MetadataReference) From {s_corLibReference, s_vbRuntimeReference}
        If additionalReferences IsNot Nothing Then
            references.AddRange(additionalReferences)
        End If

        Dim compilation = VisualBasicCompilation.Create(
            assemblyName,
            {SyntaxFactory.ParseSyntaxTree(source)},
            references,
            New VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, rootNamespace:=""))

        Using peStream = New MemoryStream()
            Dim emitResult = compilation.Emit(peStream)
            Assert.True(emitResult.Success, String.Join(Environment.NewLine, emitResult.Diagnostics))
            peStream.Position = 0
            Return MetadataReference.CreateFromStream(peStream)
        End Using
    End Function

#End Region

#Region "cell 1 - <host> keeps the host object namespace out of unqualified lookup"

    ''' <summary>
    ''' Cell 1 ("keeps the host object's namespaces and global types out of unqualified lookup in a script"). The
    ''' host object's assembly is the one carrying the alias (`Script.cs:238-239`,
    ''' `HostAssemblyReferenceProperties`), so its namespace is the name that has to stop existing - and the
    ''' diagnostic says exactly that, rather than reporting an ambiguity.
    ''' </summary>
    <Fact>
    Public Sub HostAlias_HidesTheHostObjectNamespaceFromUnqualifiedLookup()
        Dim source = "Dim o As " & HiddenHostNamespace & "." & HiddenHostTypeName & " = Nothing"
        Dim script = VisualBasicScript.Create(source, ScriptModeConformance.DefaultOptions, GetType(HiddenHostObject))
        Dim diagnostics = script.Compile()

        Assert.Equal({"BC30002"}, ErrorIds(diagnostics))

        Dim span = ErrorDiagnostics(diagnostics)(0).Location.SourceSpan
        Assert.Equal(HiddenHostNamespace & "." & HiddenHostTypeName, script.Code.Substring(span.Start, span.Length))

        Dim hostAssemblyName = GetType(HiddenHostObject).Assembly.GetName().Name
        Assert.Equal({HostAlias}, AliasesOf(script.GetCompilation(), hostAssemblyName).ToArray())
        Assert.False(DeclarationsAccessibleWithoutAlias(script.GetCompilation(), hostAssemblyName),
                     Report("the host assembly must not merge into the global namespace", source, diagnostics,
                            ReferenceAliases(script.GetCompilation())))
    End Sub

    Private Const HiddenHostNamespace As String = "HostAliasFixture"
    Private Const HiddenHostTypeName As String = "HiddenHostObject"

#End Region

#Region "cell 2 - host object members stay reachable"

    ''' <summary>
    ''' Cell 2, the positive partner of cells 1 and 3. The hiding is about the bare name only: the host object
    ''' instance is passed to the submission, so its members bind through the host object member reference rather
    ''' than through the merged namespace. A hidden assembly therefore still yields a usable globals object.
    ''' </summary>
    <Fact>
    Public Sub HostAlias_KeepsHostObjectMembersReachable()
        Dim script = VisualBasicScript.Create("Number + Doubled()", ScriptModeConformance.DefaultOptions, GetType(HiddenHostObject))
        Dim diagnostics = script.Compile()
        Assert.True(ErrorDiagnostics(diagnostics).Length = 0,
                    Report("the host object members must bind", script.Code, diagnostics, ReferenceAliases(script.GetCompilation())))

        Assert.Equal(63, script.RunAsync(New HiddenHostObject()).GetAwaiter().GetResult().ReturnValue)
    End Sub

#End Region

#Region "cell 3 - the hiding is absolute"

    ''' <summary>
    ''' Cell 3, the hardest rule of the section: "A reference that carries a non-global alias is therefore
    ''' unreachable from Visual Basic source: the hiding is absolute." VB has no <c>extern alias</c> and the
    ''' section states the compiler provides no other syntax that can name an aliased reference, so every source
    ''' form that could name a type is tried and every one of them is rejected. <c>NameOf</c> reports the bare
    ''' identifier (BC30451) while a type position reports the whole dotted name (BC30002) - both are "the name
    ''' does not exist".
    ''' </summary>
    <Fact>
    Public Sub HostAlias_HidingIsAbsolute_EverySourceFormIsRejected()
        Dim hidden = HiddenHostNamespace & "." & HiddenHostTypeName
        Dim cases As (Source As String, ExpectedId As String)() = {
            ("Dim o As " & hidden & " = Nothing", "BC30002"),
            ("Dim o As Global." & hidden & " = Nothing", "BC30002"),
            ("Dim o = New " & hidden & "()", "BC30002"),
            ("Dim t = GetType(" & hidden & ")", "BC30002"),
            ("Dim n = NameOf(" & hidden & ")", "BC30451"),
            ("Dim o = CType(Nothing, " & hidden & ")", "BC30002"),
            (Lines("Imports " & HiddenHostNamespace, "Dim o As " & hidden & " = Nothing"), "BC30002")
        }

        For Each item In cases
            Dim script = VisualBasicScript.Create(item.Source, ScriptModeConformance.DefaultOptions, GetType(HiddenHostObject))
            Dim diagnostics = script.Compile()
            Assert.Equal({item.ExpectedId}, ErrorIds(diagnostics))
        Next

        ' The same rejection is not the absence of a reference: add the very same assembly without an alias and
        ' the very same source binds. The alias, not the reference, is what makes the name unreachable.
        Dim unaliasedHostAssembly = AssemblyMetadata.CreateFromImage(
            File.ReadAllBytes(GetType(HiddenHostObject).Assembly.Location)).GetReference()
        Dim control = VisualBasicScript.Create("Dim o As " & hidden & " = Nothing" & vbCrLf & "1",
                                              ScriptModeConformance.DefaultOptions.AddReferences(unaliasedHostAssembly))
        Assert.Equal({}, ErrorIds(control.Compile()))
    End Sub

#End Region

#Region "cell 4 - <implicit> satisfies identity without merging"

    ''' <summary>
    ''' Cell 4: <c>&lt;implicit&gt;</c> "lets the assembly satisfy an assembly identity without merging its types
    ''' into the global namespace". The alias literal is fixed by
    ''' `RuntimeMetadataReferenceResolver.cs:28-29` (`s_resolvedMissingAssemblyReferenceProperties`) and the
    ''' substitution path is `:100` `ResolveMissingAssembly` to `:140-143` `CreateResolvedMissingReference`; this
    ''' cell drives the same shape with an in-memory fixture so that both halves of the sentence become
    ''' observable. The second half of the cell checks the literal against the production resolver's own output.
    ''' </summary>
    <Fact>
    Public Sub ImplicitAlias_SatisfiesAssemblyIdentityWithoutMergingIntoTheGlobalNamespace()
        Dim unaliased = CreateFixture(SubstitutedAssemblyName, Lines(
            "Namespace " & SubstitutedNamespace,
            "    Public Class " & SubstitutedTypeName,
            "    End Class",
            "End Namespace"))

        ' The owner is compiled against the unaliased reference; the alias is a property of the reference, not of
        ' the assembly, so the same metadata is hidden in one compilation and merged in the other.
        Dim owner = CreateFixture(OwnerAssemblyName, Lines(
            "Namespace " & OwnerNamespace,
            "    Public Class " & OwnerTypeName,
            "        Public Function Make() As " & SubstitutedNamespace & "." & SubstitutedTypeName,
            "            Return New " & SubstitutedNamespace & "." & SubstitutedTypeName & "()",
            "        End Function",
            "    End Class",
            "End Namespace"), {unaliased})

        Dim substituted = unaliased.WithAliases(ImmutableArray.Create(ImplicitAlias))

        Dim options = ScriptOptions.Default.WithMetadataResolver(
            New ImplicitSubstitutionResolver(SubstitutedAssemblyName, substituted)).AddReferences(owner)

        ' Identity satisfied: the owning assembly's signature names a type of the substituted assembly, so the
        ' call binds and compiles with no diagnostic at all - which is only possible if the substituted reference
        ' is in the reference set and its symbol was read.
        Dim satisfied = VisualBasicScript.Create("New " & OwnerNamespace & "." & OwnerTypeName & "().Make()", options)
        Assert.Equal({}, ErrorIds(satisfied.Compile()))

        Dim compilation = satisfied.GetCompilation()
        Dim substitutedSymbol = TryCast(compilation.GetAssemblyOrModuleSymbol(substituted), IAssemblySymbol)
        Assert.NotNull(substitutedSymbol)
        Assert.Equal(SubstitutedAssemblyName, substitutedSymbol.Identity.Name)

        ' ... and it does not merge into the global namespace: absent from the merge, present in the reference set.
        Assert.Contains(ImplicitAlias, AliasesOf(compilation, SubstitutedAssemblyName))
        Assert.False(DeclarationsAccessibleWithoutAlias(compilation, SubstitutedAssemblyName),
                     Report("the substituted assembly must not merge", satisfied.Code, satisfied.Compile(),
                            ReferenceAliases(compilation)))

        Dim naming = VisualBasicScript.Create("Dim hidden As " & SubstitutedNamespace & "." & SubstitutedTypeName & " = Nothing", options)
        Assert.Equal({"BC30002"}, ErrorIds(naming.Compile()))

        AssertProductionImplicitAliasIsApplied()
        AssertUnaliasedSubstitutionBinds()
    End Sub

    ''' <summary>
    ''' The positive partner: with the alias gone the very same type name binds. The two variants are the two
    ''' sides of the predicate - empty aliases and the global alias both merge
    ''' (`CommonReferenceManager.State.cs:721-725`, `DeclarationsAccessibleWithoutAlias`).
    ''' </summary>
    Private Shared Sub AssertUnaliasedSubstitutionBinds()
        Dim source = "Dim o As " & SubstitutedNamespace & "." & SubstitutedTypeName & " = Nothing"
        For Each aliases In {ImmutableArray(Of String).Empty, ImmutableArray.Create(MetadataReferenceProperties.GlobalAlias)}
            Dim reference = CreateFixture(SubstitutedAssemblyName, Lines(
                "Namespace " & SubstitutedNamespace,
                "    Public Class " & SubstitutedTypeName,
                "    End Class",
                "End Namespace")).WithAliases(aliases)

            Dim script = VisualBasicScript.Create(source, ScriptOptions.Default.AddReferences(reference))
            Assert.Equal({}, ErrorIds(script.Compile()))
        Next
    End Sub

    ''' <summary>
    ''' The literal is not this file's invention: the production resolver applies it to the assemblies it
    ''' substitutes for missing dependencies, and every such assembly is out of the merge.
    ''' </summary>
    Private Shared Sub AssertProductionImplicitAliasIsApplied()
        Dim script = VisualBasicScript.Create("1", ScriptModeConformance.DefaultOptions)
        script.Compile()

        Dim substituted = ReferenceAliases(script.GetCompilation()).
            Where(Function(entry) entry.Aliases.Contains(ImplicitAlias) AndAlso
                                  Not entry.Aliases.Contains(MetadataReferenceProperties.GlobalAlias)).
            ToArray()

        Assert.True(substituted.Length > 0,
                    Report("the production resolver has to substitute at least one dependency", script.Code, script.Compile(),
                           ReferenceAliases(script.GetCompilation())))

        For Each entry In substituted
            Assert.False(DeclarationsAccessibleWithoutAlias(script.GetCompilation(), entry.Name), entry.Name)
        Next
    End Sub

    Private Const SubstitutedAssemblyName As String = "AliasProbeSubstituted"
    Private Const SubstitutedNamespace As String = "AliasProbeSubstituted"
    Private Const SubstitutedTypeName As String = "SubstitutedType"
    Private Const OwnerAssemblyName As String = "AliasProbeOwner"
    Private Const OwnerNamespace As String = "AliasProbeOwner"
    Private Const OwnerTypeName As String = "OwnerType"

    ''' <summary>
    ''' Returns the fixture as the substitution for one missing identity, spelled with the alias
    ''' <c>RuntimeMetadataReferenceResolver</c> uses. Every other assembly is delegated to the runtime resolver,
    ''' which is what production does for every other dependency.
    ''' </summary>
    Private NotInheritable Class ImplicitSubstitutionResolver
        Inherits MetadataReferenceResolver

        Private ReadOnly _substitutedName As String
        Private ReadOnly _substituted As PortableExecutableReference
        Private ReadOnly _runtime As MetadataReferenceResolver = ScriptMetadataResolver.Default

        Public Sub New(substitutedName As String, substituted As PortableExecutableReference)
            _substitutedName = substitutedName
            _substituted = substituted
        End Sub

        Public Overrides ReadOnly Property ResolveMissingAssemblies As Boolean
            Get
                Return True
            End Get
        End Property

        Public Overrides Function ResolveMissingAssembly(definition As MetadataReference, referenceIdentity As AssemblyIdentity) As PortableExecutableReference
            If referenceIdentity.Name = _substitutedName Then
                Return _substituted
            End If

            Return _runtime.ResolveMissingAssembly(definition, referenceIdentity)
        End Function

        Public Overrides Function ResolveReference(reference As String, baseFilePath As String, properties As MetadataReferenceProperties) As ImmutableArray(Of PortableExecutableReference)
            Return _runtime.ResolveReference(reference, baseFilePath, properties)
        End Function

        Public Overrides Function Equals(obj As Object) As Boolean
            Return ReferenceEquals(Me, obj)
        End Function

        Public Overrides Function GetHashCode() As Integer
            Return 0
        End Function
    End Class

#End Region

#Region "cell 5 - the host alias propagates recursively"

    ''' <summary>
    ''' Cell 5: <c>&lt;host&gt;</c> is applied "with recursive aliases" (`Script.cs:238-239`, the
    ''' <c>WithRecursiveAliases(true)</c> call) and the resolver contract asks for that explicitly ("requests
    ''' assembly references with recursive aliases enabled, so an alias applied to a directive reference
    ''' propagates to the assemblies it references"). The assertion is that an assembly which is <em>not</em> the
    ''' host assembly nevertheless carries <c>&lt;host&gt;</c> and stays out of the merge - the propagation, not
    ''' just the direct alias - plus the source level consequence, paired with the unaliased control.
    ''' </summary>
    <Fact>
    Public Sub HostAlias_PropagatesRecursivelyToTheHostAssemblyDependencies()
        Dim hostAssemblyName = GetType(HiddenHostObject).Assembly.GetName().Name
        Dim dependencyName = GetType(ScriptOptions).Assembly.GetName().Name
        Assert.NotEqual(hostAssemblyName, dependencyName)
        Assert.Contains(dependencyName, GetType(HiddenHostObject).Assembly.GetReferencedAssemblies().Select(Function(a) a.Name))

        Dim probe = "Dim o As " & dependencyName & ".ScriptOptions = Nothing"
        Dim script = VisualBasicScript.Create(probe & vbCrLf & "1", ScriptModeConformance.DefaultOptions, GetType(HiddenHostObject))
        Dim diagnostics = script.Compile()

        Dim aliases = AliasesOf(script.GetCompilation(), dependencyName)
        Assert.Contains(HostAlias, aliases)
        Assert.DoesNotContain(MetadataReferenceProperties.GlobalAlias, aliases)
        Assert.False(DeclarationsAccessibleWithoutAlias(script.GetCompilation(), dependencyName),
                     Report("a recursively aliased dependency must not merge", probe, diagnostics,
                            ReferenceAliases(script.GetCompilation())))
        Assert.Equal({"BC30002"}, ErrorIds(diagnostics))

        ' The positive partner: reference the same assembly explicitly and its namespace is a name again.
        Dim explicitReference = AssemblyMetadata.CreateFromImage(
            File.ReadAllBytes(GetType(ScriptOptions).Assembly.Location)).GetReference()
        Dim paired = VisualBasicScript.Create(probe & vbCrLf & "1",
                                              ScriptModeConformance.DefaultOptions.AddReferences(explicitReference),
                                              GetType(HiddenHostObject))
        Assert.Equal({}, ErrorIds(paired.Compile()))
    End Sub

#End Region

#Region "cell 6 - CommandLineScriptGlobals as globalsType"

    ''' <summary>
    ''' Cell 6: the same rule with the globals type the command line host passes
    ''' (`Scripting\Core\Hosting\CommandLine\CommandLineScriptGlobals.cs:22`, the type the script compiler hands
    ''' to `Script.GlobalsType`). Its assembly carries exactly <c>&lt;host&gt;</c>, its members are reachable
    ''' through the globals instance, and its namespace is not a name the script can write.
    ''' </summary>
    <Fact>
    Public Sub CommandLineScriptGlobals_AliasSetAndMemberVisibility()
        Dim globals = New CommandLineScriptGlobals(New TestConsoleIO("").Out, VisualBasicObjectFormatter.Instance)
        globals.Args.Add("first")

        Dim script = VisualBasicScript.Create(Lines("Args.Add(""second"")", "Args.Count"),
                                              ScriptModeConformance.DefaultOptions,
                                              GetType(CommandLineScriptGlobals))
        Dim diagnostics = script.Compile()
        Dim compilation = script.GetCompilation()
        Assert.True(ErrorDiagnostics(diagnostics).Length = 0,
                    Report("the globals members must bind", script.Code, diagnostics, ReferenceAliases(compilation)))

        Assert.Equal(2, script.RunAsync(globals).GetAwaiter().GetResult().ReturnValue)

        Dim assemblyName = GetType(CommandLineScriptGlobals).Assembly.GetName().Name
        Assert.Equal({HostAlias}, AliasesOf(compilation, assemblyName).ToArray())
        Assert.False(DeclarationsAccessibleWithoutAlias(compilation, assemblyName),
                     Report("the globals type assembly must not merge", script.Code, diagnostics, ReferenceAliases(compilation)))

        Dim probe = VisualBasicScript.Create("Dim o As " & assemblyName & ".ScriptOptions = Nothing",
                                             ScriptModeConformance.DefaultOptions,
                                             GetType(CommandLineScriptGlobals))
        Assert.Equal({"BC30002"}, ErrorIds(probe.Compile()))
    End Sub

#End Region

#Region "cell 7 - ordinary compilation applies the same alias filter"

    ''' <summary>
    ''' Cell 7, the second container: "Ordinary compilation uses the same rule. The alias filter is not specific
    ''' to scripts." The shape is reproduced twice - once over an in-memory fixture so that both the hidden and
    ''' the visible outcome are observable, and once over the real core library pair the parser builds
    ''' (`VisualBasicCommandLineParser.vb:1612-1664`, `LoadCoreLibraryReference`, whose `:1633` is where that pair
    ''' gets its alias: <c>System.Private.CoreLib.dll</c> aliased, its facade global). The second half therefore
    ''' reproduces the reference-set shape of that pair; the alias literal it applies is this file's
    ''' `CoreLibraryAlias`, not a value read back out of the parser, so the cell pins the filter and not the
    ''' parser's choice of name.
    ''' </summary>
    <Fact>
    Public Sub OrdinaryCompilation_AppliesTheSameNonGlobalAliasFilter()
        Dim fixture = CreateFixture(OrdinaryAssemblyName, Lines(
            "Namespace Global." & OrdinaryNamespace,
            "    Public Class " & OrdinaryTypeName,
            "    End Class",
            "End Namespace"))

        Assert.Equal({"BC30002"}, ErrorIds(OrdinaryDiagnostics(fixture.WithAliases(ImmutableArray.Create(CoreLibraryAlias)))))
        Assert.Equal({}, ErrorIds(OrdinaryDiagnostics(fixture)))
        Assert.Equal({}, ErrorIds(OrdinaryDiagnostics(fixture.WithAliases(ImmutableArray.Create(MetadataReferenceProperties.GlobalAlias)))))

        AssertRealCoreLibraryAliasFilter()
    End Sub

    ''' <summary>
    ''' The production shape: the real core library carrying the same literal the parser applies to it under
    ''' `/nostdlib` (`CoreLibraryAlias`), with its facade left global. An ordinary compilation that only uses
    ''' core types still succeeds - the facade's type forwards reach the aliased library, so the library's surface
    ''' does not merge but its identity is satisfied - which is the clause's claim that the "type not defined"
    ''' error set is unchanged.
    ''' </summary>
    Private Shared Sub AssertRealCoreLibraryAliasFilter()
        Dim coreLibraryPath = GetType(Object).Assembly.Location
        Dim facadePath = Path.Combine(Path.GetDirectoryName(coreLibraryPath), "System.Runtime.dll")
        Assert.True(File.Exists(facadePath), "the runtime facade is expected next to the core library")

        Dim compilation = VisualBasicCompilation.Create(
            "AliasProbeOrdinaryRealCoreLibrary",
            {SyntaxFactory.ParseSyntaxTree(Lines(
                "Public Class Probe",
                "    Public Function Use() As Object",
                "        Dim text As String = ""alias""",
                "        Return text",
                "    End Function",
                "End Class"))},
            {MetadataReference.CreateFromFile(facadePath),
             MetadataReference.CreateFromFile(coreLibraryPath).WithAliases(ImmutableArray.Create(CoreLibraryAlias))},
            New VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, rootNamespace:=""))

        Assert.Equal({}, ErrorIds(compilation.GetDiagnostics()))

        Dim coreLibraryName = GetType(Object).Assembly.GetName().Name
        Assert.False(DeclarationsAccessibleWithoutAlias(compilation, coreLibraryName),
                     Report("the aliased core library must not merge", "", compilation.GetDiagnostics(),
                            ReferenceAliases(compilation)))
        Assert.True(DeclarationsAccessibleWithoutAlias(compilation, Path.GetFileNameWithoutExtension(facadePath)))
    End Sub

    Private Const OrdinaryAssemblyName As String = "AliasProbeOrdinary"
    Private Const OrdinaryNamespace As String = "AliasProbeOrdinary"
    Private Const OrdinaryTypeName As String = "OrdinaryType"

    Private Shared Function OrdinaryDiagnostics(reference As MetadataReference) As ImmutableArray(Of Diagnostic)
        Dim source = Lines(
            "Public Class Probe",
            "    Public Sub Use()",
            "        Dim o As " & OrdinaryNamespace & "." & OrdinaryTypeName & " = Nothing",
            "    End Sub",
            "End Class")
        Dim compilation = VisualBasicCompilation.Create(
            "AliasProbeConsumer",
            {SyntaxFactory.ParseSyntaxTree(source)},
            {s_corLibReference, s_vbRuntimeReference, reference},
            New VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, rootNamespace:=""))
        Return compilation.GetDiagnostics()
    End Function

#End Region

#Region "cell 8 - an Imports of a hidden namespace reports the missing-namespace warning"

    ''' <summary>
    ''' Cell 8 anchors the "No escape hatch" paragraph's runtime claim: an <c>Imports</c> of a namespace a
    ''' non-global alias hides "reports the same warning as an <c>Imports</c> of a namespace that does not exist -
    ''' the same ID (<c>BC40056</c>), the same message template, reported at the imported name - so the two differ
    ''' only in the name the message quotes." The seven cells above read error diagnostics only and BC40056 is a
    ''' warning, so none of them can observe this claim: cell 3's <c>Imports</c> case shows that the clause does
    ''' not produce an error, not that it produces a warning.
    ''' </summary>
    <Fact>
    Public Sub ImportsOfHiddenAndMissingNamespaces_ReportTheSameWarning()
        Dim hiddenSource = "Imports " & HiddenHostNamespace
        Dim missingSource = "Imports " & MissingNamespace

        Dim hidden = VisualBasicScript.Create(hiddenSource, ScriptModeConformance.DefaultOptions, GetType(HiddenHostObject))
        Dim hiddenDiagnostics = hidden.Compile()
        Dim missing = VisualBasicScript.Create(missingSource, ScriptModeConformance.DefaultOptions)
        Dim missingDiagnostics = missing.Compile()

        Dim hiddenWarning = OnlyWarning(hiddenDiagnostics, hiddenSource)
        Dim missingWarning = OnlyWarning(missingDiagnostics, missingSource)

        ' The same ID ...
        Assert.Equal("BC40056", hiddenWarning.Id)
        Assert.Equal(hiddenWarning.Id, missingWarning.Id)

        ' ... over the same message template. The template is compared rather than the formatted message: it is
        ' the unsubstituted one, so it still holds the placeholder and cannot hold either name.
        Dim hiddenTemplate = hiddenWarning.Descriptor.MessageFormat.ToString()
        Assert.Contains("{0}", hiddenTemplate)
        Assert.DoesNotContain(HiddenHostNamespace, hiddenTemplate)
        Assert.Equal(hiddenTemplate, missingWarning.Descriptor.MessageFormat.ToString())

        ' ... so the two are told apart by the quoted name alone, each quoting its own.
        Assert.Contains(HiddenHostNamespace, hiddenWarning.GetMessage())
        Assert.Contains(MissingNamespace, missingWarning.GetMessage())
        Assert.NotEqual(hiddenWarning.GetMessage(), missingWarning.GetMessage())

        ' Both are reported at the imported name, not at the whole clause.
        Assert.Equal(HiddenHostNamespace, ReportedText(hiddenSource, hiddenWarning))
        Assert.Equal(MissingNamespace, ReportedText(missingSource, missingWarning))
    End Sub

    ''' <summary>
    ''' A namespace no assembly in the reference set declares, so that its <c>Imports</c> is unresolvable for a
    ''' reason other than a reference alias.
    ''' </summary>
    Private Const MissingNamespace As String = "NoSuchNamespaceAnywhereProbe"

    ''' <summary>
    ''' The counterpart of <c>ErrorDiagnostics</c> for the warning half of the diagnostic set. <c>ErrorIds</c> is
    ''' deliberately left as it is: it is what the seven cells above assert against. The arity is asserted over
    ''' the warnings and not over the whole set: `Script.cs:340` returns a submission's warnings as the whole set
    ''' for a submission without errors, so a total-count assertion here could not fail while this one passes.
    ''' </summary>
    Private Shared Function OnlyWarning(diagnostics As ImmutableArray(Of Diagnostic), source As String) As Diagnostic
        Dim warnings = diagnostics.Where(Function(d) d.Severity = DiagnosticSeverity.Warning).ToArray()
        Assert.True(warnings.Length = 1, Report("exactly one warning is expected", source, diagnostics))
        Return warnings(0)
    End Function

    ''' <summary>The source text the diagnostic points at, which is the syntax node it was reported on.</summary>
    Private Shared Function ReportedText(source As String, diagnostic As Diagnostic) As String
        Dim span = diagnostic.Location.SourceSpan
        Return source.Substring(span.Start, span.Length)
    End Function

#End Region

End Class

''' <summary>
''' The host object of cells 1, 2, 3 and 5. <c>Namespace Global.&lt;name&gt;</c> keeps the project root namespace
''' out of the metadata name, so the name the script cannot write is exactly <c>HostAliasFixture</c> rather than
''' a root-namespace-prefixed one.
''' </summary>
Namespace Global.HostAliasFixture

    Public Class HiddenHostObject

        Public ReadOnly Property Number As Integer = 21

        Public Function Doubled() As Integer
            Return Number * 2
        End Function

    End Class

End Namespace
