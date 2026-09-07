' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Generic
Imports System.Collections.Immutable

Namespace Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

    ''' <summary>
    ''' One <c>#R "nuget:name, version"</c> directive that survived the pre-scan classification (parse
    ''' succeeded, version present, not host-managed). Carries its directive location so restore diagnostics
    ''' can be anchored at the exact <c>#R</c> line.
    ''' </summary>
    Friend NotInheritable Class NuGetPackageRequest

        Private ReadOnly _name As String
        Private ReadOnly _version As String
        Private ReadOnly _location As Location
        Private ReadOnly _canonicalKey As String

        Friend Sub New(name As String, version As String, location As Location)
            If String.IsNullOrWhiteSpace(name) Then
                Throw New ArgumentException("Package name required.", NameOf(name))
            End If
            _name = name
            _version = If(version, String.Empty)
            _location = If(location, Location.None)
            _canonicalKey = NuGetPackageSet.CanonicalKey(_name, _version)
        End Sub

        Friend ReadOnly Property Name As String
            Get
                Return _name
            End Get
        End Property

        Friend ReadOnly Property Version As String
            Get
                Return _version
            End Get
        End Property

        Friend ReadOnly Property Location As Location
            Get
                Return _location
            End Get
        End Property

        ''' <summary>
        ''' Canonical cache-set key (lower-cased id + ',' + version). Case of the id never affects identity.
        ''' </summary>
        Friend ReadOnly Property CanonicalKey As String
            Get
                Return _canonicalKey
            End Get
        End Property
    End Class

    ''' <summary>
    ''' Canonical package-set helpers shared by the cache key, the restore trigger policy and the session.
    ''' </summary>
    Friend NotInheritable Class NuGetPackageSet

        Private Sub New()
        End Sub

        Friend Shared Function CanonicalKey(packageName As String, packageVersion As String) As String
            Return packageName.Trim().ToLowerInvariant() & "," & packageVersion.Trim()
        End Function

        ''' <summary>
        ''' Sorts and de-duplicates a collection of canonical package keys so set equality is
        ''' order-independent (design §E1: the exact package set is canonical-sorted before hashing).
        ''' </summary>
        Friend Shared Function Normalize(keys As IEnumerable(Of String)) As ImmutableArray(Of String)
            Dim ordered = New List(Of String)()
            For Each key In keys
                If Not ContainsOrdinalIgnoreCase(ordered, key) Then
                    ordered.Add(key)
                End If
            Next
            ordered.Sort(StringComparer.OrdinalIgnoreCase)
            Return ordered.ToImmutableArray()
        End Function

        Private Shared Function ContainsOrdinalIgnoreCase(items As List(Of String), candidate As String) As Boolean
            For Each item In items
                If String.Equals(item, candidate, StringComparison.OrdinalIgnoreCase) Then
                    Return True
                End If
            Next
            Return False
        End Function

        Friend Shared Function AreEqual(left As ImmutableArray(Of String), right As ImmutableArray(Of String)) As Boolean
            If left.IsDefault OrElse right.IsDefault Then
                Return False
            End If
            If left.Length <> right.Length Then
                Return False
            End If
            For i = 0 To left.Length - 1
                If Not String.Equals(left(i), right(i), StringComparison.OrdinalIgnoreCase) Then
                    Return False
                End If
            Next
            Return True
        End Function
    End Class

    ''' <summary>
    ''' Restore trigger policy (design §E3): a pure decision function over the last exit-0 package set and
    ''' the set referenced by the current submission. v1 deliberately does not persist the exit-0 set across
    ''' processes, so a fresh host always sees an empty previous set and re-runs restore (NuGet no-op restore
    ''' is a cheap re-verification, design E7).
    ''' </summary>
    Friend NotInheritable Class NuGetRestorePolicy

        Private Sub New()
        End Sub

        ''' <summary>
        ''' True when a restore must run: the current set is non-empty and differs from the previous exit-0
        ''' set. Same set (in-process) skips; a set change or an empty previous set (fresh process / previous
        ''' failure) always restores.
        ''' </summary>
        Friend Shared Function ShouldRestore(prevExit0Set As ImmutableArray(Of String), currentSet As ImmutableArray(Of String)) As Boolean
            If currentSet.IsDefaultOrEmpty Then
                Return False
            End If
            If prevExit0Set.IsDefaultOrEmpty Then
                Return True
            End If
            Return Not NuGetPackageSet.AreEqual(prevExit0Set, currentSet)
        End Function
    End Class
End Namespace
