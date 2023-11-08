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
            MsgBox("This program can only be run as MSIX packed app. If you're running it from source, call vbi.exe directly.", vbExclamation, "Unintended usage")
            Environment.ExitCode = ex.HResult
            Return
        End Try
        If winRTArgs.Kind = Activation.ActivationKind.File Then
            If Not AskCanRunCode() Then
                Return
            End If
            If IsNetFrameworkScriptArgs(args) Then
                LaunchNetFrameworkApp(args)
            Else
                LaunchNetCoreApp(args)
            End If
        Else
            MsgBox("This program can only be started by file association.", vbExclamation, "Unintended usage")
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

    Private Sub LaunchNetFrameworkApp(args As String())
        LaunchApp("vbifw\vbi.exe", args)
    End Sub

    Private Sub LaunchNetCoreApp(args As String())
        LaunchApp("vbicore\vbi.exe", args)
    End Sub

    Private Sub LaunchApp(absolutePath As String, args As String())
        Try
            Dim packageRoot = Package.Current.InstalledLocation.Path
            Dim exePath = Path.Combine(packageRoot, absolutePath)
            Process.Start(exePath, args)
        Catch ex As Exception
            MsgBox("Failed to run the vbx script, because script runner couldn't be located", vbExclamation, "Launch failed")
        End Try
    End Sub

End Module
