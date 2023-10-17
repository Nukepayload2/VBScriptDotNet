' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.
Option Compare Text

Imports System.IO
Imports System.Reflection
Imports Microsoft.CodeAnalysis.Scripting.Hosting

#If WINDOWS10_0_17763_0_OR_GREATER Then
Imports Windows.ApplicationModel
#End If

Namespace Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

    Friend NotInheritable Class Vbi
        Friend Const InteractiveResponseFileName As String = "vbi.rsp"

        Public Shared Function Main(args As String()) As Integer
            Console.Title = "VB Interactive"
#If WINDOWS7_0_OR_GREATER Then

#If WINDOWS10_0_17763_0_OR_GREATER Then
            Dim winRTArgs = AppInstance.GetActivatedEventArgs
            If winRTArgs.Kind = Activation.ActivationKind.File Then
                Console.WriteLine("==== Security Warning ====")
                Console.WriteLine("Running scripts can potentially harm your computer.")
                Console.WriteLine("Do not run it if you obtained it from an untrusted source.")
                Console.WriteLine()
                Do
                    Console.WriteLine("Press Enter to run the script.")
                    Console.WriteLine("Press ESC to abort.")
                    Dim response = Console.ReadKey
                    Select Case response.Key
                        Case ConsoleKey.Enter
                            Console.WriteLine("Running...")
                            Exit Do
                        Case ConsoleKey.Escape
                            Console.WriteLine("Aborted.")
                            Const ERROR_CANCELLED = &H4C7
                            Return ERROR_CANCELLED
                    End Select
                Loop
            End If
#End If

            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(False)
            System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2)
            System.Windows.Forms.Application.EnableVisualStyles()
#End If

            Try
                ' Note that AppContext.BaseDirectory isn't necessarily the directory containing vbi.exe.
                ' For example, when executed via corerun it's the directory containing corerun.
                Dim vbiDirectory = Path.GetDirectoryName(GetType(Vbi).GetTypeInfo().Assembly.ManifestModule.FullyQualifiedName)

                Dim buildPaths = New BuildPaths(
                    clientDir:=vbiDirectory,
                    workingDir:=Directory.GetCurrentDirectory(),
                    sdkDir:=Path.GetDirectoryName(GetType(Object).Assembly.Location),
                    tempDir:=Path.GetTempPath())

                Dim compiler = New VisualBasicInteractiveCompiler(
                    responseFile:=Path.Combine(vbiDirectory, InteractiveResponseFileName),
                    buildPaths:=buildPaths,
                    args:=args,
                    analyzerLoader:=New NotImplementedAnalyzerLoader())

                Dim runner = New CommandLineRunner(
                    ConsoleIO.Default,
                    compiler,
                    VisualBasicScriptCompiler.Instance,
                    VisualBasicObjectFormatter.Instance)

                Dim retVal = runner.RunInteractive()
                If retVal <> 0 Then
                    PromptScriptError()
                End If
                Return retVal
            Catch ex As Exception
                Console.WriteLine(ex.ToString())
                PromptScriptError()
                Return 1
            End Try
        End Function

        Private Shared Sub PromptScriptError()
            MsgBox("The script has error. See the output window for more information.", vbExclamation, "Script Error")
        End Sub
    End Class

End Namespace

