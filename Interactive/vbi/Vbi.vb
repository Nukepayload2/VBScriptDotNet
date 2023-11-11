' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.IO
Imports System.Reflection
Imports Microsoft.CodeAnalysis.Scripting.Hosting

Namespace Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

    Friend NotInheritable Class Vbi
        Friend Const InteractiveResponseFileName As String = "vbi.rsp"

        Public Shared Function Main(args As String()) As Integer
#If WINDOWS7_0_OR_GREATER Then
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(False)
            System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2)
            System.Windows.Forms.Application.EnableVisualStyles()
#End If

#If NETFRAMEWORK Then
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(False)
            System.Windows.Forms.Application.EnableVisualStyles()
            Console.Title = "VB Interactive (net48)"
#Else
            Console.Title = "VB Interactive (net6.0)"
#End If

            Try
                ' Note that AppContext.BaseDirectory isn't necessarily the directory containing vbi.exe.
                ' For example, when executed via corerun it's the directory containing corerun.
                Dim vbiDirectory = Path.GetDirectoryName(GetType(Vbi).GetTypeInfo().Assembly.ManifestModule.FullyQualifiedName)
                Dim retVal = VisualBasicScript.RunInteractive(args, vbiDirectory, InteractiveResponseFileName)

                If retVal <> 0 Then
                    Console.Error.WriteLine("Unexpected exit code " & retVal)
                End If
                Return VisualBasicScript.RunInteractive(args, vbiDirectory, InteractiveResponseFileName)
            Catch ex As Exception
                Console.Error.WriteLine(ex.ToString())
                Return 1
            End Try
        End Function

    End Class

End Namespace

