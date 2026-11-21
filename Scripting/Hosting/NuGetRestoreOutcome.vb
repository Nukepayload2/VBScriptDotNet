' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' Raw outcome of one <see cref="INuGetRestoreRunner"/> call: exit code, error text, whether NuGet wrote its
    ''' cache, the generated assets text on success, and the mirrored-config failure the coordinator reports
    ''' separately from a restore failure. No diagnostic interpretation is performed here.
    ''' </summary>
    Public NotInheritable Class NuGetRestoreOutcome

        Private ReadOnly _exitCode As Integer
        Private ReadOnly _standardError As String
        Private ReadOnly _nuGetCacheWritten As Boolean
        Private ReadOnly _assetsJsonText As String
        Private ReadOnly _configFailurePath As String
        Private ReadOnly _configFailureReason As String
        Private ReadOnly _diagnostics As ImmutableArray(Of VBScriptDiagnostic)

        Public Sub New(exitCode As Integer,
                       standardError As String,
                       nuGetCacheWritten As Boolean,
                       assetsJsonText As String,
                       Optional diagnostics As ImmutableArray(Of VBScriptDiagnostic) = Nothing,
                       Optional configFailurePath As String = Nothing,
                       Optional configFailureReason As String = Nothing)
            _exitCode = exitCode
            _standardError = If(standardError, String.Empty)
            _nuGetCacheWritten = nuGetCacheWritten
            _assetsJsonText = assetsJsonText
            _diagnostics = If(diagnostics.IsDefault, ImmutableArray(Of VBScriptDiagnostic).Empty, diagnostics)
            _configFailurePath = configFailurePath
            _configFailureReason = If(configFailureReason, String.Empty)
        End Sub

        ''' <summary>Process exit code reported by the restore executor.</summary>
        Public ReadOnly Property ExitCode As Integer
            Get
                Return _exitCode
            End Get
        End Property

        ''' <summary>Standard error text produced by the executor; empty when there was none.</summary>
        Public ReadOnly Property StandardError As String
            Get
                Return _standardError
            End Get
        End Property

        ''' <summary>True when NuGet wrote its <c>obj/project.nuget.cache</c>, i.e. the restore completed.</summary>
        Public ReadOnly Property NuGetCacheWritten As Boolean
            Get
                Return _nuGetCacheWritten
            End Get
        End Property

        ''' <summary>Generated <c>obj/project.assets.json</c> text on success; <see langword="Nothing"/> otherwise.</summary>
        Public ReadOnly Property AssetsJsonText As String
            Get
                Return _assetsJsonText
            End Get
        End Property

        ''' <summary>Diagnostics the executor attached to the outcome (normally empty; the coordinator builds them).</summary>
        Public ReadOnly Property Diagnostics As ImmutableArray(Of VBScriptDiagnostic)
            Get
                Return _diagnostics
            End Get
        End Property

        ''' <summary>Mirrored <c>NuGet.config</c> file that could not be written, or <see langword="Nothing"/>.</summary>
        Public ReadOnly Property ConfigFailurePath As String
            Get
                Return _configFailurePath
            End Get
        End Property

        ''' <summary>Reason the mirrored config could not be written; empty when none failed.</summary>
        Public ReadOnly Property ConfigFailureReason As String
            Get
                Return _configFailureReason
            End Get
        End Property

        ''' <summary>True when the executor completed a restore (exit code 0 and the NuGet cache was written).</summary>
        Public ReadOnly Property Succeeded As Boolean
            Get
                Return _exitCode = 0 AndAlso _nuGetCacheWritten AndAlso _configFailurePath Is Nothing
            End Get
        End Property

    End Class

End Namespace
