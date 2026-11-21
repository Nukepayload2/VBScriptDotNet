' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' L3 tests for the static VBScript facade (test-plan T-STA-1/2 plus the public-surface and output-routing
' invariants). No network, no process, no file written.

Imports System
Imports System.Linq
Imports System.Reflection
Imports System.Threading.Tasks
Imports Nukepayload2.VBScriptDotNet.Hosting
Imports Nukepayload2.VBScriptDotNet.Scripting
Imports Xunit

Public Class HostingStaticFacadeTests

    <Fact>
    Public Async Function T_STA_1_ReturnValueBecomesExitCode() As Task
        Dim result = Await VBScript.RunAsync("Return 7")

        Assert.Equal(7, result.ExitCode)
        Assert.Equal(7, CInt(result.ReturnValue))
        Assert.True(result.Diagnostics.IsEmpty)
        Assert.Null(result.Exception)
        Assert.Equal(0, result.Output.Length)
    End Function

    <Fact>
    Public Async Function T_STA_2_CompileErrorIsDataNotException() As Task
        Dim result = Await VBScript.CompileAsync("Dim x =")

        Assert.False(result.Diagnostics.IsEmpty)
        Assert.Equal(1, result.ExitCode)
        Assert.Null(result.Exception)
        Assert.Contains(result.Diagnostics, Function(diagnostic) diagnostic.Severity = VBScriptDiagnosticSeverity.Error)
        Assert.Contains(result.Diagnostics, Function(diagnostic) diagnostic.Id.StartsWith("BC", StringComparison.Ordinal))
    End Function

    <Fact>
    Public Async Function PrintTextIsCapturedIntoResultOutput() As Task
        Dim result = Await VBScript.RunAsync("Print(1)")

        Assert.Equal(0, result.ExitCode)
        Assert.Equal("1" & Environment.NewLine, result.Output)
    End Function

    <Fact>
    Public Async Function EvaluateAsyncReturnsTheTypedValue() As Task
        ' The type parameter must not become the script's return type: a bare expression only yields a value in
        ' an object-returning submission (test-plan T-RUN-6).
        Dim value = Await VBScript.EvaluateAsync(Of Integer)("1+1")

        Assert.Equal(2, value)
    End Function

    <Fact>
    Public Async Function EvaluateAsyncOfObjectReturnsTheTypedValue() As Task
        Dim value = Await VBScript.EvaluateAsync(Of Object)("1+1")

        Assert.Equal(2, CInt(value))
    End Function

    <Fact>
    Public Async Function EvaluateAsyncWithoutReturnValueGivesDefaultOfT() As Task
        Dim value = Await VBScript.EvaluateAsync(Of Integer)("Dim x = 1")

        Assert.Equal(0, value)
    End Function

    <Fact>
    Public Async Function EvaluateAsyncCompileErrorThrowsScriptExceptionWithDiagnostics() As Task
        Dim scriptException = Await Assert.ThrowsAsync(Of VBScriptScriptException)(
            Async Function() As Task
                Await VBScript.EvaluateAsync(Of Integer)("Dim x =")
            End Function)

        Assert.NotEmpty(scriptException.Diagnostics)
        Assert.Contains(scriptException.Diagnostics, Function(diagnostic) diagnostic.Id.StartsWith("BC", StringComparison.Ordinal))
        Assert.Contains(scriptException.Diagnostics(0).Id, scriptException.Message)
    End Function

    <Fact>
    Public Sub PublicSurfaceDoesNotExposeRoslynTypes()
        Dim facadeAssembly = GetType(VBScript).Assembly
        Dim exportedTypes = facadeAssembly.GetExportedTypes()

        Assert.NotEmpty(exportedTypes)

        For Each exportedType In exportedTypes
            AssertNoRoslynType(exportedType)
            AssertNoRoslynType(exportedType.BaseType)

            For Each constructorInfo In exportedType.GetConstructors(BindingFlags.Public Or BindingFlags.Instance)
                For Each parameterInfo In constructorInfo.GetParameters()
                    AssertNoRoslynType(parameterInfo.ParameterType)
                Next
            Next

            For Each methodInfo In exportedType.GetMethods(BindingFlags.Public Or BindingFlags.Instance Or BindingFlags.Static Or BindingFlags.DeclaredOnly)
                AssertNoRoslynType(methodInfo.ReturnType)
                For Each parameterInfo In methodInfo.GetParameters()
                    AssertNoRoslynType(parameterInfo.ParameterType)
                Next
            Next

            For Each propertyInfo In exportedType.GetProperties(BindingFlags.Public Or BindingFlags.Instance Or BindingFlags.Static Or BindingFlags.DeclaredOnly)
                AssertNoRoslynType(propertyInfo.PropertyType)
                For Each parameterInfo In propertyInfo.GetIndexParameters()
                    AssertNoRoslynType(parameterInfo.ParameterType)
                Next
            Next
        Next
    End Sub

    <Fact>
    Public Sub FacadeEntriesAreAnnotatedForTrimAndAot()
        Dim facadeType = GetType(VBScript)
        For Each methodName In {"RunAsync", "EvaluateAsync", "CompileAsync"}
            Dim entry = facadeType.GetMethods(BindingFlags.Public Or BindingFlags.Static).
                Single(Function(method) method.Name = methodName)

            Assert.NotEmpty(entry.GetCustomAttributes(GetType(System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute), inherit:=False))
            Assert.NotEmpty(entry.GetCustomAttributes(GetType(System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute), inherit:=False))
        Next
    End Sub

    Private Shared Sub AssertNoRoslynType(type As Type)
        If type Is Nothing Then
            Return
        End If

        If type.IsByRef OrElse type.IsArray OrElse type.IsPointer Then
            AssertNoRoslynType(type.GetElementType())
            Return
        End If

        If type.IsGenericType Then
            ' The definition itself would satisfy IsGenericType again, so check its namespace instead of recursing.
            AssertNamespaceIsNotRoslyn(type.GetGenericTypeDefinition())
            For Each argument In type.GetGenericArguments()
                AssertNoRoslynType(argument)
            Next

            Return
        End If

        AssertNamespaceIsNotRoslyn(type)
    End Sub

    Private Shared Sub AssertNamespaceIsNotRoslyn(type As Type)
        Dim ns = type.Namespace
        Dim isRoslyn = ns IsNot Nothing AndAlso
            (ns = "Microsoft.CodeAnalysis" OrElse ns.StartsWith("Microsoft.CodeAnalysis.", StringComparison.Ordinal))
        Assert.False(isRoslyn, "the public hosting surface exposes " & type.FullName)
    End Sub

End Class
