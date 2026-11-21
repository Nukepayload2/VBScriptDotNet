' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Generic
Imports System.Text
Imports System.Threading

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' Everything a host can configure for one script execution or one session. Reference and import lists
    ''' start empty on purpose: the vbi default set (<c>vbi.coreclr.rsp</c>) is merged in by the library, and
    ''' entries the host added first keep their position.
    ''' </summary>
    Public Class VBScriptOptions

        Public Sub New()
            Me.[Imports] = New List(Of String)()
            Me.References = New List(Of String)()
            Me.ScriptArguments = New List(Of String)()
            Me.EmitDebugInformation = True
            Me.OptimizationLevel = VBScriptOptimizationLevel.Debug
        End Sub

        ''' <summary>
        ''' Additional namespaces. The default set is appended after the host's entries; duplicates are dropped
        ''' case-insensitively. Every entry must name a namespace: blank entries are not dropped by the default-set
        ''' merge and are rejected by <see cref="VBScriptOptionsValidator"/>.
        ''' </summary>
        ''' <remarks>
        ''' The rejection is a DI-path rule: the options validator runs only for services registered through
        ''' <c>AddVBScripting</c>. The container-less static facade has no validator and drops blank entries while
        ''' projecting the options onto compiler options, so the same blank entry is a configuration error in the
        ''' DI runner but is silently ignored by the static entry points.
        ''' </remarks>
        Public Property [Imports] As IList(Of String)

        ''' <summary>
        ''' Additional references (assembly simple names or paths). The default set is appended after the host's
        ''' entries; duplicates are dropped case-insensitively. Every entry must name an assembly or an assembly
        ''' path: blank entries are not dropped by the default-set merge and are rejected by
        ''' <see cref="VBScriptOptionsValidator"/>.
        ''' </summary>
        ''' <remarks>
        ''' The rejection is a DI-path rule: the options validator runs only for services registered through
        ''' <c>AddVBScripting</c>. The container-less static facade has no validator and drops blank entries while
        ''' projecting the options onto compiler options, so the same blank entry is a configuration error in the
        ''' DI runner but is silently ignored by the static entry points.
        ''' </remarks>
        Public Property References As IList(Of String)

        ''' <summary>
        ''' Base directory for <c>#Load</c> and relative references. <see langword="Nothing"/> means the process
        ''' current directory at call time, which is what vbi uses.
        ''' </summary>
        Public Property WorkingDirectory As String

        ''' <summary>Arguments exposed to the script as <see cref="VBScriptGlobals.Args"/>.</summary>
        Public Property ScriptArguments As IReadOnlyList(Of String)

        ''' <summary>Encoding used to decode file scripts; <see langword="Nothing"/> lets the compiler detect it.</summary>
        Public Property FileEncoding As Encoding

        Public Property EmitDebugInformation As Boolean

        Public Property OptimizationLevel As VBScriptOptimizationLevel

        ''' <summary>
        ''' Globals instance handed to the script. Prefer this over <see cref="GlobalsType"/>: a session must reuse
        ''' one instance across submissions.
        ''' </summary>
        ''' <remarks>
        ''' The instance's type must be public: scripts compile into their own dynamic submission assembly, so a
        ''' non-public globals type fails with <c>TypeAccessException</c> when the generated submission accesses it
        ''' (the failure is reported as script data, like any other run-time exception).
        ''' </remarks>
        Public Property Globals As Object

        ''' <summary>
        ''' Globals type for the one-shot entry when no instance is supplied; must derive from
        ''' <see cref="VBScriptGlobals"/>. Ignored when <see cref="Globals"/> is set.
        ''' </summary>
        ''' <remarks>
        ''' The type must be public and have either a public constructor taking <see cref="IScriptOutput"/> or a public
        ''' parameterless constructor. A non-public type fails with <c>TypeAccessException</c> when the generated
        ''' submission accesses it, because submissions are compiled into a separate dynamic assembly.
        ''' </remarks>
        Public Property GlobalsType As Type

        ''' <summary>
        ''' Append <c>System.Drawing</c> and <c>System.Windows.Forms</c> to the references. Only meaningful on
        ''' net472; net10 ignores it and the DI runner logs one warning.
        ''' </summary>
        Public Property UseDesktopReferences As Boolean

        ''' <summary>
        ''' The host environment the NuGet restore layer mirrors: target framework, runtime identifier, platform
        ''' version, the SDK version observed by the first real restore, and a fingerprint of the reachable
        ''' <c>NuGet.config</c> files. Read-only; never <see langword="Nothing"/> (the snapshot is created on first
        ''' access when the host did not provide one).
        ''' </summary>
        Public ReadOnly Property RestoreContext As NuGetRestoreContext
            Get
                Dim current = Volatile.Read(_restoreContext)
                If current IsNot Nothing Then
                    Return current
                End If

                Dim created = NuGetRestoreContext.CreateDefault(WorkingDirectory)
                Dim winner = Interlocked.CompareExchange(_restoreContext, created, Nothing)
                Return If(winner, created)
            End Get
        End Property

        Private _restoreContext As NuGetRestoreContext

        ''' <summary>
        ''' Shares the restore snapshot with a copy of these options (the effective-options clone), so the SDK
        ''' version observed by the restore service stays visible to the result the host reads.
        ''' </summary>
        Friend Sub SetRestoreContext(context As NuGetRestoreContext)
            _restoreContext = context
        End Sub

    End Class

End Namespace
