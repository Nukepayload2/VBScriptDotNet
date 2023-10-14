Imports Microsoft.CodeAnalysis.Scripting
Imports System.Threading
Public Module ScriptTaskExtensions
    <System.Runtime.CompilerServices.Extension>
    Public Async Function ContinueWith(task As Task(Of ScriptState), code As String, Optional options As ScriptOptions = Nothing, Optional cancellationToken As CancellationToken = Nothing) As Task(Of ScriptState(Of Object))
        Return Await (Await task.ConfigureAwait(False)).ContinueWithAsync(code, options, cancellationToken).ConfigureAwait(False)
    End Function
    <System.Runtime.CompilerServices.Extension>
    Public Async Function ContinueWith(task As Task(Of ScriptState(Of Object)), code As String, Optional options As ScriptOptions = Nothing, Optional cancellationToken As CancellationToken = Nothing) As Task(Of ScriptState(Of Object))
        Return Await (Await task.ConfigureAwait(False)).ContinueWithAsync(code, options, cancellationToken).ConfigureAwait(False)
    End Function
    <System.Runtime.CompilerServices.Extension>
    Public Async Function ContinueWith(Of T)(task As Task(Of ScriptState), code As String, Optional options As ScriptOptions = Nothing, Optional cancellationToken As CancellationToken = Nothing) As Task(Of ScriptState(Of T))
        Return Await (Await task.ConfigureAwait(False)).ContinueWithAsync(Of T)(code, options, cancellationToken).ConfigureAwait(False)
    End Function
    <System.Runtime.CompilerServices.Extension>
    Public Async Function ContinueWith(Of T)(task As Task(Of ScriptState(Of Object)), code As String, Optional options As ScriptOptions = Nothing, Optional cancellationToken As CancellationToken = Nothing) As Task(Of ScriptState(Of T))
        Return Await (Await task.ConfigureAwait(False)).ContinueWithAsync(Of T)(code, options, cancellationToken).ConfigureAwait(False)
    End Function
    <System.Runtime.CompilerServices.Extension>
    Public Async Function ContinueWith(Of S)(task As Task(Of ScriptState(Of S)), code As String, Optional options As ScriptOptions = Nothing, Optional cancellationToken As CancellationToken = Nothing) As Task(Of ScriptState(Of Object))
        Return Await (Await task.ConfigureAwait(False)).ContinueWithAsync(code, options, cancellationToken).ConfigureAwait(False)
    End Function
End Module
