' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Generic
Imports System.Collections.Immutable

Namespace Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

    ''' <summary>
    ''' A requested package that ships native assets, but none for the current RID (design §G2.4).
    ''' </summary>
    Friend NotInheritable Class MissingNativeAssetsInfo

        Friend Sub New(packageName As String, candidateRids As ImmutableArray(Of String))
            _packageName = packageName
            _candidateRids = If(candidateRids.IsDefault, ImmutableArray(Of String).Empty, candidateRids)
        End Sub

        Private ReadOnly _packageName As String
        Private ReadOnly _candidateRids As ImmutableArray(Of String)

        Friend ReadOnly Property PackageName As String
            Get
                Return _packageName
            End Get
        End Property

        Friend ReadOnly Property CandidateRids As ImmutableArray(Of String)
            Get
                Return _candidateRids
            End Get
        End Property
    End Class

    ''' <summary>
    ''' Pure detector over a restore's <c>project.assets.json</c> (design §G2.4): finds requested packages
    ''' that carry native assets (a <c>runtimeTargets</c>/<c>runtime</c> entry under a
    ''' <c>runtimes/&lt;rid&gt;/native</c> path or an <c>assetType</c> of "native") but none for the host
    ''' RID. RID fallback already happened inside NuGet restore; this only makes the outcome legible so the
    ''' coordinator can report a missing probe root up front. No file access; parses the JSON text given to it.
    ''' </summary>
    Friend NotInheritable Class NuGetMissingNativeAssetsDetector

        Private Sub New()
        End Sub

        Friend Shared Function FindMissingNativeForRid(
            jsonText As String,
            rid As String,
            requests As ImmutableArray(Of NuGetPackageRequest)) As ImmutableArray(Of MissingNativeAssetsInfo)

            If String.IsNullOrEmpty(jsonText) OrElse String.IsNullOrEmpty(rid) OrElse requests.IsDefaultOrEmpty Then
                Return ImmutableArray(Of MissingNativeAssetsInfo).Empty
            End If

            Dim root = TryCast(NuGetJson.Parse(jsonText), NuGetJsonObject)
            If root Is Nothing Then
                Return ImmutableArray(Of MissingNativeAssetsInfo).Empty
            End If

            Dim targets = TryCast(root.TryGetMember("targets"), NuGetJsonObject)
            If targets Is Nothing Then
                Return ImmutableArray(Of MissingNativeAssetsInfo).Empty
            End If

            Dim result As New List(Of MissingNativeAssetsInfo)()
            For Each request In requests
                Dim nativeRids As New SortedSet(Of String)(StringComparer.OrdinalIgnoreCase)
                Dim hasNativeAssets As Boolean = False

                For Each targetMember In targets.EnumerateMembers()
                    Dim target = TryCast(targetMember.Value, NuGetJsonObject)
                    If target Is Nothing Then
                        Continue For
                    End If

                    Dim identity = FindPackageIdentity(target, request.Name)
                    If identity Is Nothing Then
                        Continue For
                    End If

                    Dim entry = TryCast(target.TryGetMember(identity), NuGetJsonObject)
                    If entry Is Nothing Then
                        Continue For
                    End If

                    CollectNativeRids(entry, hasNativeAssets, nativeRids)
                Next

                If hasNativeAssets AndAlso Not nativeRids.Contains(rid) Then
                    result.Add(New MissingNativeAssetsInfo(request.Name, nativeRids.ToImmutableArray()))
                End If
            Next

            Return result.ToImmutableArray()
        End Function

        Private Shared Sub CollectNativeRids(entry As NuGetJsonObject, ByRef hasNativeAssets As Boolean, nativeRids As SortedSet(Of String))
            Dim runtimeTargets = TryCast(entry.TryGetMember("runtimeTargets"), NuGetJsonObject)
            If runtimeTargets IsNot Nothing Then
                For Each member In runtimeTargets.EnumerateMembers()
                    Dim native = IsNativeAsset(member.Key, TryCast(member.Value, NuGetJsonObject))
                    If native Then
                        hasNativeAssets = True
                        Dim nativeRid As String = Nothing
                        If TryGetEntryRid(member.Key, TryCast(member.Value, NuGetJsonObject), nativeRid) Then
                            nativeRids.Add(nativeRid)
                        End If
                    End If
                Next
            End If

            Dim runtime = TryCast(entry.TryGetMember("runtime"), NuGetJsonObject)
            If runtime IsNot Nothing Then
                For Each member In runtime.EnumerateMembers()
                    If member.Key.IndexOf("/native/", StringComparison.OrdinalIgnoreCase) >= 0 Then
                        hasNativeAssets = True
                        Dim nativeRid As String = Nothing
                        If TryGetRidFromPath(member.Key, nativeRid) Then
                            nativeRids.Add(nativeRid)
                        End If
                    End If
                Next
            End If
        End Sub

        Private Shared Function IsNativeAsset(path As String, asset As NuGetJsonObject) As Boolean
            If path.IndexOf("/native/", StringComparison.OrdinalIgnoreCase) >= 0 Then
                Return True
            End If
            Dim assetType As String = Nothing
            Return asset IsNot Nothing AndAlso asset.TryGetString("assetType", assetType) AndAlso
                String.Equals(assetType, "native", StringComparison.OrdinalIgnoreCase)
        End Function

        Private Shared Function TryGetEntryRid(path As String, asset As NuGetJsonObject, ByRef rid As String) As Boolean
            If asset IsNot Nothing Then
                Dim declaredRid As String = Nothing
                If asset.TryGetString("rid", declaredRid) AndAlso Not String.IsNullOrEmpty(declaredRid) Then
                    rid = declaredRid
                    Return True
                End If
            End If
            Return TryGetRidFromPath(path, rid)
        End Function

        Private Shared Function TryGetRidFromPath(path As String, ByRef rid As String) As Boolean
            Const marker As String = "/native/"
            Const prefix As String = "runtimes/"
            If String.IsNullOrEmpty(path) Then
                rid = Nothing
                Return False
            End If

            Dim markerIndex = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase)
            If markerIndex < 0 Then
                rid = Nothing
                Return False
            End If

            ' rid = text between "runtimes/" and "/native/".
            Dim ridStart = path.IndexOf(prefix, StringComparison.OrdinalIgnoreCase)
            If ridStart < 0 Then
                rid = Nothing
                Return False
            End If
            ridStart += prefix.Length
            If ridStart >= markerIndex Then
                rid = Nothing
                Return False
            End If

            rid = path.Substring(ridStart, markerIndex - ridStart)
            Return rid.Length > 0
        End Function

        Private Shared Function FindPackageIdentity(target As NuGetJsonObject, packageName As String) As String
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
    End Class
End Namespace
