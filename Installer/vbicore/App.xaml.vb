Imports System.Text
Imports Microsoft.UI.Xaml

Namespace Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting
    Public Class App
        Inherits Microsoft.UI.Xaml.Application

        WithEvents AppDebugSettings As DebugSettings
        Sub New()
            InitializeComponent()
            AppDebugSettings = DebugSettings
        End Sub

        Private _mWindow As Window
        Public Shared Property VbiArgs As String()

        Protected Overrides Async Sub OnLaunched(args As LaunchActivatedEventArgs)
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
            Environment.ExitCode = Await Vbi.OnStartupAsync(VbiArgs)
            [Exit]()
        End Sub

        Private Sub Application_UnhandledException(
            sender As Object,
            e As UnhandledExceptionEventArgs) Handles Me.UnhandledException
            Dim ex = e.Exception.ToString()
            Debug.WriteLine(ex)
            Stop
            e.Handled = True
        End Sub

        Private Sub AppDebugSettings_BindingFailed(sender As Object, e As BindingFailedEventArgs) Handles AppDebugSettings.BindingFailed
            Debug.WriteLine(e.Message)
        End Sub
    End Class

End Namespace

