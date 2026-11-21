' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable
Imports System.Threading
Imports System.Threading.Tasks

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' A stateful submission chain over the script core. Obtain one from
    ''' <see cref="IVBScriptSessionFactory.CreateSession"/> and dispose it with <c>Await Using</c>; sessions are not
    ''' registered in the DI container.
    ''' <para>
    ''' One globals instance is reused across submissions. A compile error does not end the session and does not
    ''' refresh its options; a run-time exception is reported in the result and the session stays usable. Only
    ''' cancellation faults the session: <see cref="Reset"/> or <see cref="DisposeAsync"/> is required before
    ''' submitting again, and other submissions throw <see cref="VBScriptHostException"/>.
    ''' </para>
    ''' <para>
    ''' A session is not thread-safe: submitting while another submission is in flight throws
    ''' <see cref="VBScriptBusyException"/>.
    ''' </para>
    ''' </summary>
    Public Interface IVBScriptSession
        Inherits IAsyncDisposable

#If NET Then
        ''' <summary>
        ''' Submits one line of code. Compile errors and script exceptions come back in
        ''' <see cref="VBScriptSubmissionResult"/>; cancellation is reported by
        ''' <see cref="VBScriptSubmissionResult.IsFaulted"/> and leaves the session faulted.
        ''' </summary>
        <System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Compiles and loads assemblies dynamically; the script and its references cannot be statically analyzed.")>
        <System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Compiles and loads assemblies dynamically, which is not available in AOT-only applications.")>
        Function SubmitAsync(code As String, Optional cancellationToken As CancellationToken = Nothing) As Task(Of VBScriptSubmissionResult)

        ''' <summary>
        ''' Classifies interactive input as <see cref="VBScriptSubmissionState.Complete"/>,
        ''' <see cref="VBScriptSubmissionState.Incomplete"/> or <see cref="VBScriptSubmissionState.Invalid"/> so the
        ''' host knows whether to keep reading continuation lines. Only the text is compiled; no session state changes.
        ''' </summary>
        <System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Compiles and loads assemblies dynamically; the script and its references cannot be statically analyzed.")>
        <System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Compiles and loads assemblies dynamically, which is not available in AOT-only applications.")>
        Function IsCompleteSubmission(code As String) As VBScriptSubmissionState

        ''' <summary>
        ''' A read-only snapshot of the variables the submissions declared, de-duplicated by name (the last
        ''' declaration wins, compared case-insensitively) and never including globals. Reflecting the values uses
        ''' the script's dynamic compilation path.
        ''' </summary>
        ''' <remarks>
        ''' The one session member that cannot carry the trim/AOT annotations here: they target methods, and a VB
        ''' interface property has no accessor declaration to attach them to (writing <c>Get…End Get</c> inside an
        ''' interface is BC30603), while putting them on the property itself is BC30662. They live on the
        ''' implementing getter (<c>VBScriptSession.Variables</c>) instead.
        ''' </remarks>
        ReadOnly Property Variables As ImmutableArray(Of VBScriptVariable)

        ''' <summary>
        ''' The de-duplicated variable with this name (case-insensitive), or <see langword="Nothing"/>. Reading its
        ''' <see cref="VBScriptVariable.Value"/> uses the script's dynamic compilation path.
        ''' </summary>
        <System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Reads script variables through reflection over dynamically compiled submissions.")>
        <System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Reads script variables from dynamically compiled submissions, which is not available in AOT-only applications.")>
        Function GetVariable(name As String) As VBScriptVariable

        ''' <summary>
        ''' Clears the submission chain, restores the initial options snapshot and creates a fresh assembly loader.
        ''' The globals instance is kept, and a session faulted by cancellation becomes usable again.
        ''' </summary>
        <System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Resets the dynamically compiled submission chain.")>
        <System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Resets the dynamically compiled submission chain, which is not available in AOT-only applications.")>
        Sub Reset()

        ''' <summary>
        ''' Cancels any in-flight submission, releases the loader and ends the session. Idempotent; after it returns
        ''' every submission throws <see cref="VBScriptHostException"/>.
        ''' </summary>
        <System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Releases the dynamically compiled submission chain.")>
        <System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Releases the dynamically compiled submission chain, which is not available in AOT-only applications.")>
        Shadows Function DisposeAsync() As ValueTask
#Else
        ''' <summary>
        ''' Submits one line of code. Compile errors and script exceptions come back in
        ''' <see cref="VBScriptSubmissionResult"/>; cancellation is reported by
        ''' <see cref="VBScriptSubmissionResult.IsFaulted"/> and leaves the session faulted.
        ''' </summary>
        Function SubmitAsync(code As String, Optional cancellationToken As CancellationToken = Nothing) As Task(Of VBScriptSubmissionResult)

        ''' <summary>
        ''' Classifies interactive input as <see cref="VBScriptSubmissionState.Complete"/>,
        ''' <see cref="VBScriptSubmissionState.Incomplete"/> or <see cref="VBScriptSubmissionState.Invalid"/> so the
        ''' host knows whether to keep reading continuation lines. Only the text is compiled; no session state changes.
        ''' </summary>
        Function IsCompleteSubmission(code As String) As VBScriptSubmissionState

        ''' <summary>
        ''' A read-only snapshot of the variables the submissions declared, de-duplicated by name (the last
        ''' declaration wins, compared case-insensitively) and never including globals. Reflecting the values uses
        ''' the script's dynamic compilation path.
        ''' </summary>
        ReadOnly Property Variables As ImmutableArray(Of VBScriptVariable)

        ''' <summary>
        ''' The de-duplicated variable with this name (case-insensitive), or <see langword="Nothing"/>. Reading its
        ''' <see cref="VBScriptVariable.Value"/> uses the script's dynamic compilation path.
        ''' </summary>
        Function GetVariable(name As String) As VBScriptVariable

        ''' <summary>
        ''' Clears the submission chain, restores the initial options snapshot and creates a fresh assembly loader.
        ''' The globals instance is kept, and a session faulted by cancellation becomes usable again.
        ''' </summary>
        Sub Reset()

        ''' <summary>
        ''' Cancels any in-flight submission, releases the loader and ends the session. Idempotent; after it returns
        ''' every submission throws <see cref="VBScriptHostException"/>.
        ''' </summary>
        Shadows Function DisposeAsync() As ValueTask
#End If

    End Interface

End Namespace
