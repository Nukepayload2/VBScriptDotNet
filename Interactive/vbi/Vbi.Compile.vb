' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' vbi compile mode (B2). The original design proposed linking the C# Shared compiler
' sources (Compilers\Shared\{BuildClient,Vbc,...}.cs) into this VB project, but VBC
' cannot parse C# (verified: adding a .cs Compile item yields BC30626/BC30205). This
' module re-implements the in-process compile entry (Vbc.Run, Compilers\Shared\Vbc.cs:22-29)
' directly in VB against the fork VisualBasicCompiler internals, which are visible to the
' vbi assembly via InternalsVisibleTo (Microsoft.CodeAnalysis.csproj:56, ...VisualBasic.vbproj:52).

Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Text
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.VisualBasic

Namespace Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

    Friend NotInheritable Class VbiCompileMode

        Private Sub New()
        End Sub

        ' Decision 5 (README): a .vb source or an /out: or /target: switch selects compile mode;
        ' .vbx without those keeps executing through VisualBasicScript.RunInteractiveAsync.
        ' /i and /i+ force interactive mode regardless of source files.
        Friend Shared Function IsCompileInvocation(args() As String) As Boolean
            Dim hasVbSource = False
            Dim hasOutputSwitch = False
            For Each arg In args
                If arg = "--" Then
                    ' Arguments after -- are script arguments, not sources or switches.
                    Exit For
                End If

                If arg.StartsWith("/", StringComparison.OrdinalIgnoreCase) OrElse
                   arg.StartsWith("-", StringComparison.OrdinalIgnoreCase) Then
                    Dim name = arg.TrimStart("/"c, "-"c)
                    Select Case name
                        Case "i", "i+"
                            Return False
                    End Select
                    If name.StartsWith("out:", StringComparison.OrdinalIgnoreCase) OrElse
                       name.StartsWith("target:", StringComparison.OrdinalIgnoreCase) Then
                        hasOutputSwitch = True
                    End If
                Else
                    If String.Equals(Path.GetExtension(arg), ".vb", StringComparison.OrdinalIgnoreCase) Then
                        hasVbSource = True
                    End If
                End If
            Next
            Return hasVbSource OrElse hasOutputSwitch
        End Function

        Friend Shared Function Run(args() As String, vbiDirectory As String) As Integer
            Dim buildPaths = New BuildPaths(
                clientDir:=vbiDirectory,
                workingDir:=Directory.GetCurrentDirectory(),
                sdkDir:=Nothing,
                tempDir:=Path.GetTempPath())

            Dim loader As IAnalyzerAssemblyLoader = New AnalyzerAssemblyLoader()
            ' No response file for compile mode: vbc.rsp does not ship next to vbi, so the compiler
            ' runs with the injected defaults only (vbi.rsp is the interactive/script response file).
            Dim responseFile = Path.Combine(vbiDirectory, VisualBasicCompiler.ResponseFileName)
            Dim compiler As VisualBasicCompiler = New VbiCompiler(responseFile, buildPaths, AppendCompileDefaults(args), loader)

            If compiler.Arguments.Utf8Output Then
                Try
                    Console.OutputEncoding = New UTF8Encoding(encoderShouldEmitUTF8Identifier:=False)
                Catch
                    ' Best effort: fall back to the default console encoding when the platform disallows changing it.
                End Try
            End If

            Return compiler.Run(Console.Out)
        End Function

        ' Defaults injected so a bare `vbi foo.vb /out:foo.dll` produces a working .NET Core assembly
        ' that references System.Runtime (like a `dotnet build` product):
        '  * /nostdlib - prevents the parser from adding the runtime's System.dll facade, whose type
        '    forwards to System.Private.CoreLib collide with the reference-pack System.Runtime and cause
        '    BC30560 ambiguity in the compiler-synthesized code.
        '  * /r:<reference pack assemblies> - the .NET reference pack that declares the framework types
        '    directly (dotnet build resolves these the same way). Its System.Runtime declares System.Object
        '    and is therefore picked as the cor library, so the parser's non-global "RoslynCoreLibrary"
        '    alias of System.Private.CoreLib (VisualBasicCommandLineParser.vb:1600-1602) is skipped and the
        '    emitted assembly references System.Runtime rather than System.Private.CoreLib.
        '  * /define:_MyType="Empty" - the default My template references desktop-only types that are
        '    absent on .NET Core (e.g. System.ComponentModel.Design.HelpKeyword), so My is disabled unless
        '    the user provides an explicit _MyType define.
        Private Shared Function AppendCompileDefaults(args() As String) As String()
            Dim list As New List(Of String)(args)

            If Not list.Any(Function(a) a.StartsWith("/nostdlib", StringComparison.OrdinalIgnoreCase)) Then
                list.Add("/nostdlib")
            End If

            Dim refPackDir = FindRefPackDirectory()
            If refPackDir IsNot Nothing Then
                For Each dll In Directory.GetFiles(refPackDir, "*.dll")
                    If Not list.Any(Function(a) a.StartsWith("/r:", StringComparison.OrdinalIgnoreCase) AndAlso
                                              a.IndexOf(Path.GetFileName(dll), StringComparison.OrdinalIgnoreCase) >= 0) Then
                        list.Add("/r:""" & dll & """")
                    End If
                Next
            Else
                ' Fallback when the .NET reference pack is not installed: reference the runtime core
                ' library globally. The emitted assembly then references System.Private.CoreLib directly,
                ' which is loadable but not consumable from a normal .NET project.
                If Not list.Any(Function(a) a.StartsWith("/r:", StringComparison.OrdinalIgnoreCase) AndAlso
                                          a.IndexOf("System.Private.CoreLib", StringComparison.OrdinalIgnoreCase) >= 0) Then
                    list.Add("/r:System.Private.CoreLib")
                End If
            End If

            If Not list.Any(Function(a) a.StartsWith("/define:", StringComparison.OrdinalIgnoreCase) AndAlso
                                      a.IndexOf("_MyType", StringComparison.OrdinalIgnoreCase) >= 0) Then
                list.Add("/define:_MyType=""Empty""")
            End If

            Return list.ToArray()
        End Function

        Private Shared Function FindRefPackDirectory() As String
            Try
                Dim runtimeDir = Path.GetDirectoryName(GetType(Object).Assembly.Location)
                If runtimeDir Is Nothing Then
                    Return Nothing
                End If
                Dim dotnetRoot = Directory.GetParent(runtimeDir).Parent.Parent.FullName
                Dim refRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref")
                If Directory.Exists(refRoot) Then
                    For Each versionDir In Directory.GetDirectories(refRoot)
                        Dim refDir = Path.Combine(versionDir, "ref", "net10.0")
                        If Directory.Exists(refDir) Then
                            Return refDir
                        End If
                    Next
                End If
            Catch
            End Try
            Return Nothing
        End Function
    End Class

    ' Compile-only VisualBasicCompiler, the VB analogue of Compilers\Shared\Vbc.cs:15-20.
    ' Unlike vbc it resolves references from the runtime platform assemblies (the same resolver
    ' the script path uses, CommandLineRunner.GetMetadataReferenceResolver) because vbi ships no
    ' /sdkpath reference set; both the injected reference-pack paths and user /r: bare names
    ' resolve through that resolver.
    Friend NotInheritable Class VbiCompiler
        Inherits VisualBasicCompiler

        Friend Sub New(responseFile As String, buildPaths As BuildPaths, args As String(), analyzerLoader As IAnalyzerAssemblyLoader)
            MyBase.New(VisualBasicCommandLineParser.[Default], responseFile, args, buildPaths, Nothing, analyzerLoader)
        End Sub

        Friend Overrides Function GetCommandLineMetadataReferenceResolver(loggerOpt As TouchedFileLogger) As MetadataReferenceResolver
            Return CommandLineRunner.GetMetadataReferenceResolver(Arguments, loggerOpt)
        End Function

        ' On .NET Core the core library is System.Private.CoreLib referenced by the parser under a
        ' non-global "RoslynCoreLibrary" alias (VisualBasicCommandLineParser.vb:1600-1602). The regular
        ' compile path would then lose global core types (DebuggerHidden, EditorBrowsable, ...) that the
        ' compiler-synthesized code needs. The script path solves this with IgnoreCorLibraryDuplicatedTypes
        ' (VisualBasicScriptCompiler.vb:215), so apply the same unification here.
        Public Overrides Function CreateCompilation(consoleOutput As TextWriter,
                                                    touchedFilesLogger As TouchedFileLogger,
                                                    errorLogger As ErrorLogger,
                                                    analyzerConfigOptions As ImmutableArray(Of AnalyzerConfigOptionsResult),
                                                    globalAnalyzerConfigOptions As AnalyzerConfigOptionsResult) As Compilation
            Dim compilation = MyBase.CreateCompilation(consoleOutput, touchedFilesLogger, errorLogger, analyzerConfigOptions, globalAnalyzerConfigOptions)
            If compilation IsNot Nothing Then
                Dim vbCompilation = DirectCast(compilation, VisualBasicCompilation)
                Return vbCompilation.WithOptions(vbCompilation.Options.WithIgnoreCorLibraryDuplicatedTypes(True))
            End If
            Return Nothing
        End Function
    End Class

End Namespace
