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
    ''' net48 host branch on a net10 runner; defaults to a running-host self-check.
    ''' </summary>
    Friend NotInheritable Class NuGetHostCapability

        Private ReadOnly _targetFramework As String

        Friend Sub New(targetFramework As String)
            _targetFramework = targetFramework
        End Sub

        ''' <summary>
        ''' Host TFM: the framework the host process runs on, e.g. <c>.NETCoreApp,Version=v10.0</c> or
        ''' <c>.NETFramework,Version=v4.7.2</c>; tests may inject the short form "net48".
        ''' </summary>
        Friend ReadOnly Property TargetFramework As String
            Get
                Return _targetFramework
            End Get
        End Property

        ''' <summary>
        ''' True when the host runs on .NET Framework (any version, e.g. net472), which is the capability
        ''' boundary called "net48": the .NET Framework host cannot probe native assets. Also true
        ''' for the injected short form "net48".
        ''' </summary>
        Friend ReadOnly Property IsNet48 As Boolean
            Get
                Dim parsed = ParseFrameworkName(_targetFramework)
                If parsed IsNot Nothing Then
                    Return parsed.Identifier.Equals(".NETFramework", StringComparison.OrdinalIgnoreCase)
                End If
                Return String.Equals(_targetFramework, "net48", StringComparison.OrdinalIgnoreCase) OrElse
                   _targetFramework.StartsWith(".NETFramework,", StringComparison.OrdinalIgnoreCase)
            End Get
        End Property

        ''' <summary>
        ''' Short target-framework moniker used in the temporary restore project: the version the
        ''' host actually runs on (<c>net472</c>, <c>net48</c>, <c>net10.0</c>), so a 4.7.2 host restores the
        ''' 4.7.2 graph instead of a hard-coded net48 one. An unparseable injected short form is returned
        ''' unchanged.
        ''' </summary>
        Friend ReadOnly Property ShortTargetFramework As String
            Get
                Dim shortName As String = Nothing
                If TryGetShortFrameworkName(_targetFramework, shortName) Then
                    Return shortName
                End If
                Return _targetFramework
            End Get
        End Property

        ''' <summary>
        ''' Framework name mirrored into the restore cache key and the reference-assemblies package mapping:
        ''' the canonical long form of the running host, so the key stays stable and
        ''' machine-independent. It is <em>not</em> used to key the assets "targets" section, which the reader
        ''' addresses by the short target-framework moniker (see <see cref="ShortTargetFramework"/>).
        ''' </summary>
        Friend ReadOnly Property FrameworkNameForRestore As String
            Get
                Dim parsed = ParseFrameworkName(_targetFramework)
                If parsed IsNot Nothing Then
                    Return parsed.FullName
                End If

                If IsNet48 Then
                    ' Injected short form ("net48"): keep the long name that branch has always used.
                    Return ".NETFramework,Version=v4.8"
                End If

                Return _targetFramework
            End Get
        End Property

        Private Shared Function ParseFrameworkName(frameworkName As String) As FrameworkName
            If String.IsNullOrWhiteSpace(frameworkName) Then
                Return Nothing
            End If

            Try
                Return New FrameworkName(frameworkName)
            Catch generatedExceptionName As Exception
                Return Nothing
            End Try
        End Function

        Private Shared Function TryGetShortFrameworkName(frameworkName As String, ByRef shortName As String) As Boolean
            Dim parsed = ParseFrameworkName(frameworkName)
            If parsed Is Nothing Then
                Return False
            End If

            If parsed.Identifier.Equals(".NETCoreApp", StringComparison.OrdinalIgnoreCase) Then
                shortName = "net" & parsed.Version.Major.ToString() & "." & parsed.Version.Minor.ToString()
                Return True
            End If
            If parsed.Identifier.Equals(".NETFramework", StringComparison.OrdinalIgnoreCase) Then
                shortName = GetNetFrameworkShortName(parsed.Version)
                Return True
            End If
            Return False
        End Function

        ''' <summary>
        ''' .NET Framework monikers carry every non-zero version component: 4.0 → net40, 4.5 → net45,
        ''' 4.5.1 → net451, 4.7.2 → net472, 4.8 → net48.
        ''' </summary>
        Private Shared Function GetNetFrameworkShortName(version As Version) As String
            Dim moniker = "net" & version.Major.ToString()
            If version.Minor > 0 Then
                moniker &= version.Minor.ToString()
            ElseIf version.Build <= 0 Then
                ' 4.0 -> net40 (a bare "net4" is not a TFM).
                moniker &= "0"
            End If
            If version.Build > 0 Then
                moniker &= version.Build.ToString()
            End If
            Return moniker
        End Function

        ''' <summary>
        ''' Detects the running host's framework name, in descending order of reliability:
        ''' <list type="number">
        ''' <item>the entry assembly's <see cref="TargetFrameworkAttribute"/> (the host application's own target);</item>
        ''' <item><see cref="AppContext.TargetFrameworkName"/> (the runtime's view of that target);</item>
        ''' <item><see cref="RuntimeInformation.FrameworkDescription"/>, mapped onto a framework name
        ''' (".NET Framework 4.8.9363.0" → ".NETFramework,Version=v4.8"; ".NET 10.0.15" →
        ''' ".NETCoreApp,Version=v10.0").</item>
        ''' </list>
        ''' <para>
        ''' The core library is deliberately not a source: mscorlib carries no
        ''' <see cref="TargetFrameworkAttribute"/> at run time on .NET Framework, so reading
        ''' <c>GetType(Object).Assembly</c> fell through to the free-form framework description and wrote a
        ''' value MSBuild cannot parse (".NET Framework 4.8.9363.0" → NETSDK1013) into the temporary restore
        ''' project. Every branch here returns either a name <see cref="FrameworkName"/> can parse or
        ''' <see langword="Nothing"/>.
        ''' </para>
        ''' </summary>
        Friend Shared Function CreateDefault() As NuGetHostCapability
            Return New NuGetHostCapability(DetectHostFrameworkName())
        End Function

        Private Shared Function DetectHostFrameworkName() As String
            Dim entry = Assembly.GetEntryAssembly()
            If entry IsNot Nothing Then
                Dim attribute = entry.GetCustomAttribute(Of TargetFrameworkAttribute)()
                If attribute IsNot Nothing AndAlso ParseFrameworkName(attribute.FrameworkName) IsNot Nothing Then
                    Return attribute.FrameworkName
                End If
            End If

            Dim contextName = AppContext.TargetFrameworkName
            If ParseFrameworkName(contextName) IsNot Nothing Then
                Return contextName
            End If

            Return MapFrameworkDescription(RuntimeInformation.FrameworkDescription)
        End Function

        Private Shared Function MapFrameworkDescription(description As String) As String
            If String.IsNullOrWhiteSpace(description) Then
                Return Nothing
            End If

            Dim text = description.Trim()
            If text.StartsWith(".NET Framework", StringComparison.OrdinalIgnoreCase) Then
                Dim version = ParseDescriptionVersion(text)
                If version IsNot Nothing Then
                    Return ".NETFramework,Version=v" & version.Major.ToString() & "." & version.Minor.ToString()
                End If
                Return Nothing
            End If

            If text.StartsWith(".NET ", StringComparison.OrdinalIgnoreCase) OrElse
               text.StartsWith(".NET Core", StringComparison.OrdinalIgnoreCase) Then
                Dim version = ParseDescriptionVersion(text)
                If version IsNot Nothing Then
                    Return ".NETCoreApp,Version=v" & version.Major.ToString() & "." & version.Minor.ToString()
                End If
            End If

            Return Nothing
        End Function

        Private Shared Function ParseDescriptionVersion(description As String) As Version
            For Each token In description.Split({" "c}, StringSplitOptions.RemoveEmptyEntries)
                Dim candidate As Version = Nothing
                If Version.TryParse(token, candidate) Then
                    Return candidate
                End If
            Next
            Return Nothing
        End Function
    End Class

    ''' <summary>
    ''' Session state behind NuGet package resolution for one script/REPL host run. Created empty; the
    ''' restore coordinator fills the package/asset state, the resolver reads it only.
    ''' </summary>
    Friend NotInheritable Class NuGetPackageSession

        Private ReadOnly _hostCapability As NuGetHostCapability

        ' Restore-populated state, replaced after a restore. Package key = normalized
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
        ''' Deduplicated native probe root directories (the <c>runtimes/&lt;rid&gt;/native</c>
        ''' folders) collected from the last restore. Empty when the package graph has no native assets.
        ''' </summary>
        Friend ReadOnly Property NativeRootDirectories As ImmutableArray(Of String)
            Get
                Return _nativeRootDirectories
            End Get
        End Property

        ''' <summary>
        ''' Writes the assets of a successful restore into the session. Keys are canonical
        ''' package keys (lower-cased id + ',' + version) produced by
        ''' <see cref="NuGetPackageSet.CanonicalKey"/>; each value lists the requested package's own compile
        ''' asset first and its dependency closure afterwards. Runtime paths are the managed implementation
        ''' assets registered with the interactive loader; native root directories are the deduplicated
        ''' <c>runtimes/&lt;rid&gt;/native</c> probe roots handed to the loader.
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
