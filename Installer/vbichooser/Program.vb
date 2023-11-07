Option Compare Text

Imports System.IO
Imports System.Security.Principal
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Windows.Forms
Imports Windows.ApplicationModel
Imports Windows.ApplicationModel.Activation

Module Program
    Sub Main(args As String())

        Application.SetCompatibleTextRenderingDefault(False)
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)
        Application.EnableVisualStyles()

        Dim winRTArgs As IActivatedEventArgs
        Try
            winRTArgs = AppInstance.GetActivatedEventArgs
        Catch ex As Exception
            MsgBox("This program can only be run as MSIX packed app. If you're running it from source, set the MSIX packaging project as startup project or target Windows 7 and try again.", vbExclamation, "Failed to start")
            Environment.ExitCode = ex.HResult
            Return
        End Try
        If winRTArgs.Kind = Activation.ActivationKind.File Then
            If Not AskCanRunCode() Then
                Return
            End If
            If IsNetFrameworkScriptArgs(args) Then
                LaunchNetFrameworkApp()
            Else
                LaunchNetCoreApp()
            End If
        Else
            ' Start menu activation
            LaunchNetCoreApp()
        End If
    End Sub

    Private Function AskCanRunCode() As Boolean
        Dim isElevated = New WindowsPrincipal(WindowsIdentity.GetCurrent()).
            IsInRole(WindowsBuiltInRole.Administrator)
        Dim yesButton As New TaskDialogButton With {
            .Text = My.Resources.Resources.SecurityDialog_Run,
            .ShowShieldIcon = isElevated
        }
        Dim noButton As New TaskDialogButton With {
            .Text = My.Resources.Resources.SecurityDialog_Block
        }
        Dim dlgPage As New TaskDialogPage With {
            .Caption = My.Resources.Resources.SecurityDialog_Title,
            .Buttons = New TaskDialogButtonCollection From {
                yesButton, noButton
            },
            .DefaultButton = noButton,
            .Text = My.Resources.Resources.SecurityDialog_Content
        }

        Dim buttonChoice = TaskDialog.ShowDialog(dlgPage)
        Return buttonChoice Is yesButton
    End Function

    Private Function IsNetFrameworkScriptArgs(args() As String) As Boolean
        For Each arg In args
            If arg.EndsWith(".vbx", StringComparison.OrdinalIgnoreCase) AndAlso File.Exists(arg) Then
                Return IsNetFrameworkScriptFile(arg)
            End If
        Next
        Return False
    End Function

    Private Function IsNetFrameworkScriptFile(arg As String) As Boolean
        ' Group 1: Attribute name
        ' Group 2: Attribute value
        Dim regAttributeComment As New Regex("'[ \t]*Attribute[ \t]*(\w+)[ \t]*=[ \t]*""([\w\.]+)""",
            RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant)
        Dim scriptContent = File.ReadAllText(arg, Encoding.UTF8)
        Dim matches = regAttributeComment.Matches(scriptContent)

        If matches.Count > 0 Then
            Dim match1 = matches(0)
            Dim attributeName = match1.Groups(1).Value
            Dim attributeValue = match1.Groups(2).Value
            If attributeName = "TargetFramework" Then
                Return IsNetFrameworkTargetFramework(attributeValue)
            End If
        End If

        Return False
    End Function

    Private Function IsNetFrameworkTargetFramework(attributeValue As String) As Boolean
        Return attributeValue.StartsWith("net4", StringComparison.OrdinalIgnoreCase)
    End Function

    Private Sub LaunchNetFrameworkApp()
        MsgBox("TODO: Start .NET Framework version of vbi.exe")
    End Sub

    Private Sub LaunchNetCoreApp()
        MsgBox("TODO: Start .NET version of vbi.exe")
    End Sub
End Module
