' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable
Imports Xunit

Namespace Microsoft.CodeAnalysis.Test.Utilities

    Public NotInheritable Class AssertEx
        Private Sub New()
        End Sub

        Public Shared Sub Equal(expected As String, actual As String)
            Assert.Equal(expected, actual)
        End Sub

        Public Shared Sub Equal(Of T)(expected As T, actual As T)
            Assert.Equal(expected, actual)
        End Sub

        Public Shared Sub Equal(Of T)(expected As IEnumerable(Of T), actual As IEnumerable(Of T), Optional comparer As IEqualityComparer(Of T) = Nothing, Optional message As String = Nothing, Optional itemSeparator As String = Nothing, Optional itemInspector As Func(Of T, String) = Nothing)
            Dim expectedArray = expected.ToArray()
            Dim actualArray = actual.ToArray()
            Dim equalityComparer = If(comparer, Global.System.Collections.Generic.EqualityComparer(Of T).Default)

            If expectedArray.Length <> actualArray.Length Then
                Assert.Fail(BuildMessage(expectedArray, actualArray, message, itemSeparator, itemInspector))
            End If

            For i = 0 To expectedArray.Length - 1
                If Not DirectCast(equalityComparer, IEqualityComparer(Of T)).Equals(expectedArray(i), actualArray(i)) Then
                    Assert.Fail(BuildMessage(expectedArray, actualArray, message, itemSeparator, itemInspector))
                End If
            Next
        End Sub

        Public Shared Sub Equal(Of T)(expected As ImmutableArray(Of T), actual As IEnumerable(Of T), Optional comparer As IEqualityComparer(Of T) = Nothing, Optional message As String = Nothing)
            Equal(expected.AsEnumerable(), actual, comparer, message)
        End Sub

        Public Shared Sub SetEqual(Of T)(expected As IEnumerable(Of T), actual As IEnumerable(Of T), Optional comparer As IEqualityComparer(Of T) = Nothing)
            Dim equalityComparer = If(comparer, Global.System.Collections.Generic.EqualityComparer(Of T).Default)
            Assert.Equal(expected.OrderBy(Function(item) If(item Is Nothing, String.Empty, item.ToString())),
                         actual.OrderBy(Function(item) If(item Is Nothing, String.Empty, item.ToString())),
                         equalityComparer)
        End Sub

        Public Shared Sub All(Of T)(items As IEnumerable(Of T), inspector As Action(Of T))
            For Each item In items
                inspector(item)
            Next
        End Sub

        Private Shared Function BuildMessage(Of T)(expected As IEnumerable(Of T), actual As IEnumerable(Of T), message As String, itemSeparator As String, itemInspector As Func(Of T, String)) As String
            Dim separator = If(itemSeparator, Environment.NewLine)
            Dim inspect = If(itemInspector, Function(item As T) If(item Is Nothing, "<null>", item.ToString()))
            Return String.Join(Environment.NewLine,
                {
                    If(message, "Sequences differ."),
                    "Expected:",
                    String.Join(separator, expected.Select(inspect)),
                    "Actual:",
                    String.Join(separator, actual.Select(inspect))
                })
        End Function
    End Class
End Namespace
