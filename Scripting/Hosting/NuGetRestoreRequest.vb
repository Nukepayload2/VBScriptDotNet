' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' One restore-execution request: the package identity (name + version) plus the byte-stable pieces the
    ''' executor needs to run NuGet against a temporary project. A custom <see cref="INuGetRestoreRunner"/> only
    ''' has to read <see cref="PackageName"/>/<see cref="PackageVersion"/> and may ignore the rest; the default
    ''' implementation forwards every property to the internal restore runner.
    ''' </summary>
    Public NotInheritable Class NuGetRestoreRequest

        Private ReadOnly _packageName As String
        Private ReadOnly _packageVersion As String
        Private ReadOnly _cacheDirectory As String
        Private ReadOnly _projectXml As String
        Private ReadOnly _globalJson As String
        Private ReadOnly _projectRelativePath As String
        Private ReadOnly _mirrorFiles As ImmutableArray(Of NuGetRestoreConfigFile)
        Private ReadOnly _sourceFingerprint As String

        Public Sub New(packageName As String, packageVersion As String)
            Me.New(packageName, packageVersion, cacheDirectory:=Nothing, projectXml:=Nothing, globalJson:=Nothing, projectRelativePath:=Nothing)
        End Sub

        Public Sub New(packageName As String, packageVersion As String, cacheDirectory As String, projectXml As String, globalJson As String, Optional projectRelativePath As String = Nothing, Optional mirrorFiles As ImmutableArray(Of NuGetRestoreConfigFile) = Nothing, Optional sourceFingerprint As String = Nothing)
            _packageName = If(packageName, String.Empty)
            _packageVersion = If(packageVersion, String.Empty)
            _cacheDirectory = If(cacheDirectory, String.Empty)
            _projectXml = If(projectXml, String.Empty)
            _globalJson = If(globalJson, String.Empty)
            _projectRelativePath = If(projectRelativePath, String.Empty)
            _mirrorFiles = If(mirrorFiles.IsDefault, ImmutableArray(Of NuGetRestoreConfigFile).Empty, mirrorFiles)
            _sourceFingerprint = If(sourceFingerprint, String.Empty)
        End Sub

        ''' <summary>Package id of the reference that triggered the restore.</summary>
        Public ReadOnly Property PackageName As String
            Get
                Return _packageName
            End Get
        End Property

        ''' <summary>Version of the reference that triggered the restore.</summary>
        Public ReadOnly Property PackageVersion As String
            Get
                Return _packageVersion
            End Get
        End Property

        ''' <summary>Write-once cache directory the temporary project belongs to; empty for custom runners that ignore it.</summary>
        Public ReadOnly Property CacheDirectory As String
            Get
                Return _cacheDirectory
            End Get
        End Property

        ''' <summary>Generated temporary project content; empty for custom runners that ignore it.</summary>
        Public ReadOnly Property ProjectXml As String
            Get
                Return _projectXml
            End Get
        End Property

        ''' <summary>Generated <c>global.json</c> content pinning the SDK; empty when no SDK was selected.</summary>
        Public ReadOnly Property GlobalJson As String
            Get
                Return _globalJson
            End Get
        End Property

        ''' <summary>Project path relative to <see cref="CacheDirectory"/>; empty means the runner's default.</summary>
        Public ReadOnly Property ProjectRelativePath As String
            Get
                Return _projectRelativePath
            End Get
        End Property

        ''' <summary>
        ''' Directory-level <c>NuGet.config</c> files to mirror into <see cref="CacheDirectory"/> before restoring.
        ''' Empty when the script's directory chain carries none.
        ''' </summary>
        Public ReadOnly Property MirrorFiles As ImmutableArray(Of NuGetRestoreConfigFile)
            Get
                Return _mirrorFiles
            End Get
        End Property

        ''' <summary>
        ''' Source-config fingerprint that keyed this restore (the host restore context's value). Carried so a custom
        ''' runner can observe the same input the cache key used; empty when the coordinator derived it itself.
        ''' </summary>
        Public ReadOnly Property SourceFingerprint As String
            Get
                Return _sourceFingerprint
            End Get
        End Property

    End Class

End Namespace
