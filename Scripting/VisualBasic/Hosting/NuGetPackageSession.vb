' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable
Imports System.Reflection
Imports System.Runtime.InteropServices
Imports System.Runtime.Versioning
Imports System.Text

Namespace Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

    ''' <summary>
    ''' Host capability that scopes NuGet restore (TFM / net48 flag). Injectable so tests can drive the
    ''' net48 host branch of the nuget-reference design on a net10 runner; defaults to a running-host
    ''' self-check.
    ''' </summary>
    Friend NotInheritable Class NuGetHostCapability

        Private ReadOnly _targetFramework As String

        Friend Sub New(targetFramework As String)
            _targetFramework = targetFramework
        End Sub

        ''' <summary>
        ''' Host TFM: framework name of the core library (e.g. ".NETCoreApp,Version=v10.0"); tests may
        ''' inject the short form "net48".
        ''' </summary>
        Friend ReadOnly Property TargetFramework As String
            Get
                Return _targetFramework
            End Get
        End Property

        Friend ReadOnly Property IsNet48 As Boolean
            Get
                Return String.Equals(_targetFramework, "net48", StringComparison.OrdinalIgnoreCase) OrElse
                   _targetFramework.StartsWith(".NETFramework,", StringComparison.OrdinalIgnoreCase)
            End Get
        End Property

        ''' <summary>
        ''' Short target-framework moniker used in the temporary restore project (design §E): "net48" for the
        ''' .NET Framework host, "net10.0" for the default .NET (net10) host.
        ''' </summary>
        Friend ReadOnly Property ShortTargetFramework As String
            Get
                If IsNet48 Then
                    Return "net48"
                End If
                Dim shortName As String = Nothing
                If TryGetShortFrameworkName(_targetFramework, shortName) Then
                    Return shortName
                End If
                Return _targetFramework
            End Get
        End Property

        ''' <summary>
        ''' Framework-name string used to pick the matching entry in project.assets.json "targets" (design §E4).
        ''' The assets file keys targets by the long form, e.g. ".NETCoreApp,Version=v10.0" or
        ''' ".NETFramework,Version=v4.8".
        ''' </summary>
        Friend ReadOnly Property FrameworkNameForRestore As String
            Get
                If IsNet48 Then
                    Return ".NETFramework,Version=v4.8"
                End If
                Return _targetFramework
            End Get
        End Property

        Private Shared Function TryGetShortFrameworkName(frameworkName As String, ByRef shortName As String) As Boolean
            If String.IsNullOrEmpty(frameworkName) Then
                Return False
            End If

            Dim parsed As FrameworkName = Nothing
            Try
                parsed = New FrameworkName(frameworkName)
            Catch generatedExceptionName As Exception
                Return False
            End Try

            If parsed.Identifier.Equals(".NETCoreApp", StringComparison.OrdinalIgnoreCase) Then
                shortName = "net" & parsed.Version.Major.ToString() & "." & parsed.Version.Minor.ToString()
                Return True
            End If
            If parsed.Identifier.Equals(".NETFramework", StringComparison.OrdinalIgnoreCase) Then
                Dim minor = parsed.Version.Minor
                shortName = "net" & parsed.Version.Major.ToString() & If(minor = 0, "", minor.ToString())
                Return True
            End If
            Return False
        End Function

        Friend Shared Function CreateDefault() As NuGetHostCapability
            Dim frameworkName = GetType(Object).Assembly.GetCustomAttribute(Of TargetFrameworkAttribute)()
            Dim tfm = If(frameworkName Is Nothing, RuntimeInformation.FrameworkDescription, frameworkName.FrameworkName)
            Return New NuGetHostCapability(tfm)
        End Function
    End Class

    ''' <summary>
    ''' Session state behind NuGet package resolution for one script/REPL host run. Created empty; the
    ''' restore coordinator (design §D/§E) fills the package/asset state, the resolver reads it only.
    ''' </summary>
    Friend NotInheritable Class NuGetPackageSession

        Private ReadOnly _hostCapability As NuGetHostCapability

        ' Restore-populated state (design §D/§E replaces these after a restore). Package key = normalized
        ' "<name>,<version>"; asset arrays keep index 0 = main asset. Empty until a restore exits 0.
        Private _compilePaths As ImmutableDictionary(Of String, ImmutableArray(Of String)) = ImmutableDictionary.Create(Of String, ImmutableArray(Of String))(StringComparer.OrdinalIgnoreCase)
        Private _runtimePaths As ImmutableArray(Of String) = ImmutableArray(Of String).Empty
        Private _nativeRootDirectories As ImmutableArray(Of String) = ImmutableArray(Of String).Empty

        Friend Sub New(Optional hostCapability As NuGetHostCapability = Nothing)
            If hostCapability Is Nothing Then
                hostCapability = NuGetHostCapability.CreateDefault()
            End If
            _hostCapability = hostCapability
        End Sub

        Friend ReadOnly Property HostCapability As NuGetHostCapability
            Get
                Return _hostCapability
            End Get
        End Property

        ''' <summary>
        ''' Compile asset paths recorded for a restored package; index 0 is the package's main asset and
        ''' the remaining entries its dependency closure. Empty when the key is not restored.
        ''' </summary>
        Friend Function TryGetCompilePaths(packageName As String, packageVersion As String) As ImmutableArray(Of String)
            Dim paths As ImmutableArray(Of String) = ImmutableArray(Of String).Empty
            If _compilePaths.TryGetValue(MakeKey(packageName, packageVersion), paths) Then
                Return paths
            End If
            Return ImmutableArray(Of String).Empty
        End Function

        ''' <summary>
        ''' Runtime (implementation) asset paths registered with the interactive loader after a restore.
        ''' Empty when the current package set has no runtime assets.
        ''' </summary>
        Friend ReadOnly Property RuntimePaths As ImmutableArray(Of String)
            Get
                Return _runtimePaths
            End Get
        End Property

        ''' <summary>
        ''' Deduplicated native probe root directories (design §G, the <c>runtimes/&lt;rid&gt;/native</c>
        ''' folders) collected from the last restore. Empty when the package graph has no native assets.
        ''' </summary>
        Friend ReadOnly Property NativeRootDirectories As ImmutableArray(Of String)
            Get
                Return _nativeRootDirectories
            End Get
        End Property

        ''' <summary>
        ''' Writes the assets of a successful restore into the session (design §E4). Keys are canonical
        ''' package keys (lower-cased id + ',' + version) produced by
        ''' <see cref="NuGetPackageSet.CanonicalKey"/>; each value lists the requested package's own compile
        ''' asset first and its dependency closure afterwards. Runtime paths are the managed implementation
        ''' assets registered with the interactive loader; native root directories are the deduplicated
        ''' <c>runtimes/&lt;rid&gt;/native</c> probe roots handed to the loader (design §G).
        ''' </summary>
        Friend Sub WriteRestoredAssets(
            compilePathsByKey As ImmutableDictionary(Of String, ImmutableArray(Of String)),
            runtimePaths As ImmutableArray(Of String),
            nativeRootDirectories As ImmutableArray(Of String))

            Dim compile = ImmutableDictionary.CreateBuilder(Of String, ImmutableArray(Of String))(StringComparer.OrdinalIgnoreCase)
            For Each kvp In compilePathsByKey
                compile(kvp.Key) = kvp.Value
            Next
            _compilePaths = compile.ToImmutable()

            _runtimePaths = runtimePaths
            _nativeRootDirectories = nativeRootDirectories
        End Sub

        Friend Shared Function MakeKey(packageName As String, packageVersion As String) As String
            Return packageName & "," & packageVersion
        End Function
    End Class
End Namespace
