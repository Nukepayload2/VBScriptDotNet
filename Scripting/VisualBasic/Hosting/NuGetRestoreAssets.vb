' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Generic
Imports System.Collections.Immutable

Namespace Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

    ''' <summary>
    ''' Parsed view of a successful restore's <c>project.assets.json</c> (design §E4). Compile assets are
    ''' grouped per requested package (canonical key) with the package's own asset(s) first and its
    ''' dependency closure afterwards; runtime assets are the managed implementation assemblies; native root
    ''' directories are the deduplicated <c>runtimes/&lt;rid&gt;/native</c> folders fed to the loader.
    ''' </summary>
    Friend NotInheritable Class NuGetRestoreAssets

        Friend Sub New(
            compilePathsByCanonicalKey As ImmutableDictionary(Of String, ImmutableArray(Of String)),
            runtimePaths As ImmutableArray(Of String),
            nativeRootDirectories As ImmutableArray(Of String))

            _compilePathsByCanonicalKey = compilePathsByCanonicalKey
            _runtimePaths = runtimePaths
            _nativeRootDirectories = nativeRootDirectories
        End Sub

        Private ReadOnly _compilePathsByCanonicalKey As ImmutableDictionary(Of String, ImmutableArray(Of String))
        Private ReadOnly _runtimePaths As ImmutableArray(Of String)
        Private ReadOnly _nativeRootDirectories As ImmutableArray(Of String)

        Friend ReadOnly Property CompilePathsByCanonicalKey As ImmutableDictionary(Of String, ImmutableArray(Of String))
            Get
                Return _compilePathsByCanonicalKey
            End Get
        End Property

        Friend ReadOnly Property RuntimePaths As ImmutableArray(Of String)
            Get
                Return _runtimePaths
            End Get
        End Property

        Friend ReadOnly Property NativeRootDirectories As ImmutableArray(Of String)
            Get
                Return _nativeRootDirectories
            End Get
        End Property

        Friend ReadOnly Property HasNativeAssets As Boolean
            Get
                Return Not _nativeRootDirectories.IsDefaultOrEmpty
            End Get
        End Property
    End Class

    ''' <summary>
    ''' Pure reader over the restore output's <c>project.assets.json</c> (design §E4). No file I/O: it takes
    ''' the JSON text and the host framework name (the exact "targets" key, e.g. ".NETCoreApp,Version=v10.0")
    ''' and RID, and produces the session-friendly asset view.
    ''' </summary>
    Friend NotInheritable Class NuGetRestoreAssetsReader

        Private Sub New()
        End Sub

        Friend Shared Function ReadAssets(
            jsonText As String,
            hostTargetFramework As String,
            rid As String,
            requests As ImmutableArray(Of NuGetPackageRequest)) As NuGetRestoreAssets

            Dim root = TryCast(NuGetJson.Parse(jsonText), NuGetJsonObject)
            If root Is Nothing Then
                Throw New ArgumentException("assets JSON root must be an object.", NameOf(jsonText))
            End If

            Dim packageRoot = GetPackageFolder(root)
            Dim libraryPaths = GetLibraryPaths(root)

            Dim targets = TryCast(root.TryGetMember("targets"), NuGetJsonObject)
            If targets Is Nothing Then
                Throw New ArgumentException("assets JSON has no 'targets' section.", NameOf(jsonText))
            End If

            Dim compileTargetKey = SelectTargetKey(targets, hostTargetFramework, rid, preferRidSpecific:=False)
            Dim runtimeTargetKey = SelectTargetKey(targets, hostTargetFramework, rid, preferRidSpecific:=True)
            Dim compileTarget = TryCast(targets.TryGetMember(compileTargetKey), NuGetJsonObject)
            Dim runtimeTarget = TryCast(targets.TryGetMember(runtimeTargetKey), NuGetJsonObject)

            ' Compile assets per requested package: own asset first, dependency closure after.
            Dim compileMap = ImmutableDictionary.CreateBuilder(Of String, ImmutableArray(Of String))(StringComparer.OrdinalIgnoreCase)
            If compileTarget IsNot Nothing Then
                For Each request In requests
                    Dim resolvedIdentity = FindResolvedIdentity(compileTarget, request.Name)
                    If resolvedIdentity IsNot Nothing Then
                        Dim collected = New List(Of String)()
                        Dim visited As New HashSet(Of String)(StringComparer.Ordinal)
                        CollectCompileClosure(compileTarget, packageRoot, libraryPaths, resolvedIdentity, collected, visited)
                        If collected.Count > 0 Then
                            compileMap(request.CanonicalKey) = collected.ToImmutableArray()
                        End If
                    End If
                Next
            End If

            ' Runtime + native assets from the RID-specific target (falls back to the plain target).
            Dim runtimeSet As New HashSet(Of String)(StringComparer.Ordinal)
            Dim nativeDirSet As New HashSet(Of String)(StringComparer.Ordinal)
            If runtimeTarget IsNot Nothing Then
                For Each member In runtimeTarget.EnumerateMembers()
                    Dim entry = TryCast(member.Value, NuGetJsonObject)
                    If entry Is Nothing Then
                        Continue For
                    End If

                    Dim libraryPath As String = Nothing
                    If Not libraryPaths.TryGetValue(member.Key, libraryPath) Then
                        libraryPath = member.Key.ToLowerInvariant()
                    End If

                    For Each runtimeFile In GetRuntimeFiles(entry, packageRoot, libraryPath)
                        runtimeSet.Add(runtimeFile)
                    Next
                    For Each nativeDir In GetNativeDirectories(entry, packageRoot, libraryPath)
                        nativeDirSet.Add(nativeDir)
                    Next
                Next
            End If

            Dim runtimeList = runtimeSet.ToList()
            runtimeList.Sort(StringComparer.Ordinal)
            Dim nativeList = nativeDirSet.ToList()
            nativeList.Sort(StringComparer.Ordinal)

            Return New NuGetRestoreAssets(
                compileMap.ToImmutable(),
                runtimeList.ToImmutableArray(),
                nativeList.ToImmutableArray())
        End Function

        Private Shared Function GetPackageFolder(root As NuGetJsonObject) As String
            Dim folders = TryCast(root.TryGetMember("packageFolders"), NuGetJsonArray)
            If folders IsNot Nothing AndAlso folders.Count > 0 Then
                Dim first = TryCast(folders(0), NuGetJsonString)
                If first IsNot Nothing Then
                    Return first.Value
                End If
            End If
            Return String.Empty
        End Function

        Private Shared Function GetLibraryPaths(root As NuGetJsonObject) As Dictionary(Of String, String)
            Dim result = New Dictionary(Of String, String)(StringComparer.Ordinal)
            Dim libraries = TryCast(root.TryGetMember("libraries"), NuGetJsonObject)
            If libraries IsNot Nothing Then
                For Each member In libraries.EnumerateMembers()
                    Dim entry = TryCast(member.Value, NuGetJsonObject)
                    Dim path As String = Nothing
                    If entry IsNot Nothing AndAlso entry.TryGetString("path", path) Then
                        result(member.Key) = path
                    End If
                Next
            End If
            Return result
        End Function

        Private Shared Function SelectTargetKey(targets As NuGetJsonObject, hostTargetFramework As String, rid As String, preferRidSpecific As Boolean) As String
            Dim ridKey As String = Nothing
            If Not String.IsNullOrEmpty(rid) Then
                ridKey = hostTargetFramework & "/" & rid
            End If

            If preferRidSpecific AndAlso ridKey IsNot Nothing AndAlso targets.TryGetMember(ridKey) IsNot Nothing Then
                Return ridKey
            End If
            If targets.TryGetMember(hostTargetFramework) IsNot Nothing Then
                Return hostTargetFramework
            End If
            If ridKey IsNot Nothing AndAlso targets.TryGetMember(ridKey) IsNot Nothing Then
                Return ridKey
            End If

            ' No exact match: fall back to the first target entry so a hand-authored or trimmed assets file
            ' still parses deterministically.
            For Each member In targets.EnumerateMembers()
                Return member.Key
            Next
            Throw New ArgumentException("assets JSON has no matching 'targets' entry for '" & hostTargetFramework & "'.")
        End Function

        Private Shared Function FindResolvedIdentity(target As NuGetJsonObject, packageName As String) As String
            For Each member In target.EnumerateMembers()
                Dim identity = member.Key
                Dim slash = identity.IndexOf("/"c)
                Dim id As String = If(slash < 0, identity, identity.Substring(0, slash))
                If String.Equals(id, packageName, StringComparison.OrdinalIgnoreCase) Then
                    Return identity
                End If
            Next
            Return Nothing
        End Function

        Private Shared Sub CollectCompileClosure(
            target As NuGetJsonObject,
            packageRoot As String,
            libraryPaths As Dictionary(Of String, String),
            identity As String,
            collected As List(Of String),
            visited As HashSet(Of String))

            If Not visited.Add(identity) Then
                Return
            End If

            Dim entry = TryCast(target.TryGetMember(identity), NuGetJsonObject)
            If entry Is Nothing Then
                Return
            End If

            Dim libraryPath As String = Nothing
            If Not libraryPaths.TryGetValue(identity, libraryPath) Then
                libraryPath = identity.ToLowerInvariant()
            End If

            collected.AddRange(GetCompileFiles(entry, packageRoot, libraryPath))

            Dim dependencies = TryCast(entry.TryGetMember("dependencies"), NuGetJsonObject)
            If dependencies IsNot Nothing Then
                Dim depList = New List(Of String)()
                For Each dep In dependencies.EnumerateMembers()
                    depList.Add(dep.Key)
                Next
                depList.Sort(StringComparer.Ordinal)
                For Each dep In depList
                    CollectCompileClosure(target, packageRoot, libraryPaths, dep, collected, visited)
                Next
            End If
        End Sub

        ''' <summary>
        ''' Compile asset files for one package. When the assets entry lists both <c>ref/</c> and <c>lib/</c>
        ''' files NuGet has already picked a single compile target, but the preference rule is kept so a file
        ''' that carries both resolves to the <c>ref/</c> reference assembly (same as the dotnet CLI).
        ''' </summary>
        Private Shared Function GetCompileFiles(entry As NuGetJsonObject, packageRoot As String, libraryPath As String) As ImmutableArray(Of String)
            Dim compile = TryCast(entry.TryGetMember("compile"), NuGetJsonObject)
            If compile Is Nothing Then
                Return ImmutableArray(Of String).Empty
            End If

            Dim keys = New List(Of String)()
            For Each member In compile.EnumerateMembers()
                If member.Key.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) Then
                    keys.Add(member.Key)
                End If
            Next
            keys.Sort(StringComparer.Ordinal)

            Dim hasRef = keys.Exists(Function(k) k.StartsWith("ref/", StringComparison.OrdinalIgnoreCase))
            Dim builder = ImmutableArray.CreateBuilder(Of String)()
            For Each key In keys
                If hasRef AndAlso Not key.StartsWith("ref/", StringComparison.OrdinalIgnoreCase) Then
                    Continue For
                End If
                builder.Add(CombinePackagePath(packageRoot, libraryPath, key))
            Next
            Return builder.ToImmutable()
        End Function

        Private Shared Function GetRuntimeFiles(entry As NuGetJsonObject, packageRoot As String, libraryPath As String) As ImmutableArray(Of String)
            Dim runtime = TryCast(entry.TryGetMember("runtime"), NuGetJsonObject)
            If runtime Is Nothing Then
                Return ImmutableArray(Of String).Empty
            End If

            Dim keys = New List(Of String)()
            For Each member In runtime.EnumerateMembers()
                If member.Key.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) Then
                    keys.Add(member.Key)
                End If
            Next
            keys.Sort(StringComparer.Ordinal)

            Dim builder = ImmutableArray.CreateBuilder(Of String)()
            For Each key In keys
                builder.Add(CombinePackagePath(packageRoot, libraryPath, key))
            Next
            Return builder.ToImmutable()
        End Function

        ''' <summary>
        ''' Native asset directories for one package entry. Both the RID-specific "runtimeTargets" section
        ''' and a plain "runtime" file path are scanned for <c>runtimes/&lt;rid&gt;/native/</c> entries; the
        ''' returned directory is the folder that contains the native library.
        ''' </summary>
        Private Shared Function GetNativeDirectories(entry As NuGetJsonObject, packageRoot As String, libraryPath As String) As ImmutableArray(Of String)
            Dim candidates = New List(Of String)()

            Dim runtimeTargets = TryCast(entry.TryGetMember("runtimeTargets"), NuGetJsonObject)
            If runtimeTargets IsNot Nothing Then
                For Each member In runtimeTargets.EnumerateMembers()
                    If member.Key.IndexOf("/native/", StringComparison.OrdinalIgnoreCase) >= 0 Then
                        candidates.Add(member.Key)
                    End If
                Next
            End If

            Dim runtime = TryCast(entry.TryGetMember("runtime"), NuGetJsonObject)
            If runtime IsNot Nothing Then
                For Each member In runtime.EnumerateMembers()
                    If member.Key.IndexOf("/native/", StringComparison.OrdinalIgnoreCase) >= 0 Then
                        candidates.Add(member.Key)
                    End If
                Next
            End If

            Dim dirSet As New HashSet(Of String)(StringComparer.Ordinal)
            For Each candidate In candidates
                Dim slash = candidate.LastIndexOf("/"c)
                If slash > 0 Then
                    dirSet.Add(CombinePackagePath(packageRoot, libraryPath, candidate.Substring(0, slash)))
                End If
            Next

            Dim result = dirSet.ToList()
            result.Sort(StringComparer.Ordinal)
            Return result.ToImmutableArray()
        End Function

        Private Shared Function CombinePackagePath(packageRoot As String, libraryPath As String, relativeFile As String) As String
            Dim root = packageRoot.TrimEnd("/"c)
            Dim libPath = libraryPath.TrimEnd("/"c)
            Dim rel = relativeFile.TrimStart("/"c)
            Return root & "/" & libPath & "/" & rel
        End Function
    End Class
End Namespace
