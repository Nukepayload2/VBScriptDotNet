' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.
Option Compare Text

Imports System.IO
Imports System.Reflection
Imports System.Runtime.CompilerServices
Imports System.Threading.Tasks


#If USE_WINUI Then
Imports Microsoft.UI.Dispatching
Imports Microsoft.UI.Xaml
#End If

Namespace Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

    Friend NotInheritable Class Vbi
        Friend Const InteractiveResponseFileName As String = "vbi.rsp"

        Public Shared Function Main(args As String()) As Integer
            Console.Title = "VB Interactive"

#If WINDOWS7_0_OR_GREATER Then
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(False)
            System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2)
            System.Windows.Forms.Application.EnableVisualStyles()
            Console.Title += " (net10.0)"
#End If

#If NETFRAMEWORK Then
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(False)
            System.Windows.Forms.Application.EnableVisualStyles()
            Console.Title += " (net48)"
#End If

#If USE_WINUI Then
            WinRT.ComWrappersSupport.InitializeComWrappers()
            App.VbiArgs = args
            Application.Start(AddressOf OnAppInit)
            Return Environment.ExitCode
#Else
            Return OnStartup(args)
#End If
        End Function

        Public Shared Function OnStartup(args() As String) As Integer
            Return OnStartupAsync(args).GetAwaiter().GetResult()
        End Function

        Public Shared Async Function OnStartupAsync(args() As String) As Task(Of Integer)
            Try
                ' Note that AppContext.BaseDirectory isn't necessarily the directory containing vbi.exe.
                ' For example, when executed via corerun it's the directory containing corerun.
                Dim vbiDirectory = Path.GetDirectoryName(GetType(Vbi).GetTypeInfo().Assembly.ManifestModule.FullyQualifiedName)

                If VbiCompileMode.IsCompileInvocation(args) Then
                    Return VbiCompileMode.Run(args, vbiDirectory)
                End If

                Dim retVal = Await VisualBasicScript.RunInteractiveAsync(args, vbiDirectory, InteractiveResponseFileName)

                If retVal <> 0 Then
                    PromptScriptError()
                End If
                Return retVal
            Catch ex As Exception
                Console.Error.WriteLine(ex.ToString())
                PromptScriptError()
                Return 1
            End Try
        End Function

#If USE_WINUI Then
        Private Shared Sub OnAppInit(p As ApplicationInitializationCallbackParams)
            Dim synchronizationContext As New DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread())
            Threading.SynchronizationContext.SetSynchronizationContext(synchronizationContext)
            Dim app As New App
        End Sub
#End If

        Private Shared Sub PromptScriptError()
#If WINDOWS7_0_OR_GREATER Or NETFRAMEWORK Then
            ' 输出被调用方捕获(管道/文件/无控制台)时不弹窗，横幅直接写 stderr，避免阻塞自动化调用方
            If Console.IsErrorRedirected OrElse Console.IsOutputRedirected Then
                Console.Error.WriteLine("The script has error. See the output for more information.")
                Return
            End If
            MsgBox("The script has error. See the output window for more information.", vbExclamation, "Script Error")
#Else
            Console.Error.WriteLine("The script has error. See the output for more information.")
#End If
        End Sub
    End Class

End Namespace
