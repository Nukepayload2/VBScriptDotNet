' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.IO
Imports System.Reflection
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.VisualBasic
Imports My.Resources

Namespace Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

    Friend NotInheritable Class VisualBasicInteractiveCompiler
        Inherits VisualBasicCompiler

        Friend Sub New(responseFile As String, buildPaths As BuildPaths, args As String(), analyzerLoader As IAnalyzerAssemblyLoader)
            MyBase.New(VisualBasicCommandLineParser.Script, responseFile, args, buildPaths, Nothing, analyzerLoader)
        End Sub

        Public Overrides Function GetCommandLineMetadataReferenceResolver(loggerOpt As TouchedFileLogger) As MetadataReferenceResolver
            Return CommandLineRunner.GetMetadataReferenceResolver(Arguments, loggerOpt)
        End Function

        Public Overrides ReadOnly Property Type As Type
            Get
                Return GetType(VisualBasicInteractiveCompiler)
            End Get
        End Property

        Public Overrides Sub PrintLogo(consoleOutput As TextWriter)
            consoleOutput.WriteLine(VBScriptingResources.LogoLine1, GetSelfVersion())
            consoleOutput.WriteLine(VBScriptingResources.LogoLine2, GetRoslynVersion())
            consoleOutput.WriteLine()
        End Sub

        Private Function GetSelfVersion() As String
            Return FormatVersionWithoutRevision(GetType(VisualBasicInteractiveCompiler).Assembly.GetName.Version)
        End Function

        Private Function FormatVersionWithoutRevision(version As Version) As String
            Return New Version(version.Major, version.Minor, version.Build).ToString
        End Function

        Private Function GetRoslynVersion() As String
            Return FormatVersionWithoutRevision(GetType(VisualBasicCompiler).Assembly.GetName.Version)
        End Function

        Public Overrides Sub PrintHelp(consoleOutput As TextWriter)
            consoleOutput.Write(VBScriptingResources.InteractiveHelp)
        End Sub
    End Class

End Namespace
