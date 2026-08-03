' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Threading.Tasks
Imports Xunit
Imports Xunit.Sdk
Imports Xunit.v3

Namespace Roslyn.Test.Utilities

    <AttributeUsage(AttributeTargets.All, AllowMultiple:=True)>
    Public Class WorkItemAttribute
        Inherits Attribute

        Public Sub New(link As String)
        End Sub

        Public Sub New(id As Integer, name As String)
        End Sub
    End Class

    <AttributeUsage(AttributeTargets.Method, AllowMultiple:=False)>
    Public NotInheritable Class CombinatorialDataAttribute
        Inherits DataAttribute

        Public Overrides Function GetData(method As Reflection.MethodInfo, disposalTracker As DisposalTracker) As ValueTask(Of IReadOnlyCollection(Of ITheoryDataRow))
            Dim parameters = method.GetParameters()
            If parameters.Length = 0 Then
                Return New ValueTask(Of IReadOnlyCollection(Of ITheoryDataRow))({New TheoryDataRow(Array.Empty(Of Object)())})
            End If

            Dim values As New List(Of Object())
            BuildRows(parameters, 0, New Object(parameters.Length - 1) {}, values)
            Return New ValueTask(Of IReadOnlyCollection(Of ITheoryDataRow))(values.Select(Function(row) DirectCast(New TheoryDataRow(row), ITheoryDataRow)).ToArray())
        End Function

        Public Overrides Function SupportsDiscoveryEnumeration() As Boolean
            Return True
        End Function

        Private Shared Sub BuildRows(parameters As Reflection.ParameterInfo(), index As Integer, current As Object(), rows As List(Of Object()))
            If index = parameters.Length Then
                rows.Add(DirectCast(current.Clone(), Object()))
                Return
            End If

            If parameters(index).ParameterType Is GetType(Boolean) Then
                current(index) = False
                BuildRows(parameters, index + 1, current, rows)
                current(index) = True
                BuildRows(parameters, index + 1, current, rows)
                Return
            End If

            Dim valuesAttribute = parameters(index).GetCustomAttributes(GetType(CombinatorialValuesAttribute), inherit:=False).Cast(Of CombinatorialValuesAttribute)().SingleOrDefault()
            If valuesAttribute IsNot Nothing Then
                For Each value In valuesAttribute.Values
                    current(index) = value
                    BuildRows(parameters, index + 1, current, rows)
                Next

                Return
            End If

            Throw New NotSupportedException($"CombinatorialData only supports Boolean parameters in this local test harness. Parameter '{parameters(index).Name}' has type '{parameters(index).ParameterType}'.")
        End Sub
    End Class

    <AttributeUsage(AttributeTargets.Parameter, AllowMultiple:=False)>
    Public NotInheritable Class CombinatorialValuesAttribute
        Inherits Attribute

        Public ReadOnly Property Values As Object()

        Public Sub New(ParamArray values As Object())
            Me.Values = values
        End Sub
    End Class
End Namespace
