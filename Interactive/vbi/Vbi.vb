' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.
Option Compare Text

Imports System.IO
Imports System.Reflection

#If USE_WINUI Then
Imports Microsoft.UI.Dispatching
Imports Microsoft.UI.Xaml
#End If

Namespace Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

    Friend NotInheritable Class Vbi
        Friend Const InteractiveResponseFileName As String = "vbi.rsp"

#If USE_WINUI Then

        Private Declare Sub XamlCheckProcessRequirements Lib "Microsoft.ui.xaml.dll" ()

#End If

        Public Shared Function Main(args As String()) As Integer
            Console.Title = "VB Interactive"

#If WINDOWS7_0_OR_GREATER Then
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(False)
            System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2)
            System.Windows.Forms.Application.EnableVisualStyles()
            Console.Title += " (net6.0)"
#End If

#If NETFRAMEWORK Then
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(False)
            System.Windows.Forms.Application.EnableVisualStyles()
            Console.Title += " (net48)"
#End If

#If USE_WINUI Then
            XamlCheckProcessRequirements
            WinRT.ComWrappersSupport.InitializeComWrappers()
            App.VbiArgs = args
            Application.Start(AddressOf OnAppInit)
            Return Environment.ExitCode
#Else
            Return OnStartup(args)
#End If
        End Function

        Public Shared Function OnStartup(args() As String) As Integer
            Try
                ' Note that AppContext.BaseDirectory isn't necessarily the directory containing vbi.exe.
                ' For example, when executed via corerun it's the directory containing corerun.
                Dim vbiDirectory = Path.GetDirectoryName(GetType(Vbi).GetTypeInfo().Assembly.ManifestModule.FullyQualifiedName)
                Dim retVal = VisualBasicScript.RunInteractive(args, vbiDirectory, InteractiveResponseFileName)

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
            MsgBox("The script has error. See the output window for more information.", vbExclamation, "Script Error")
        End Sub
    End Class

End Namespace
