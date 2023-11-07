Public Class DpiAwareness
    Private Const DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
    Private Declare Function SetProcessDpiAwarenessContext Lib "user32.dll" (dpiContext As IntPtr) As Boolean
    Public Shared Sub UsePerMonitorV2()
        ' Obtain the DPI awareness context for per-monitor v2
        Dim dpiContext As New IntPtr(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)

        ' Set the DPI awareness context for the current process
        SetProcessDpiAwarenessContext(dpiContext)
    End Sub
End Class
