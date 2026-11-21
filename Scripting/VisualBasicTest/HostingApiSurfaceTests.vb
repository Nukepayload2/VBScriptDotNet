' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' L3 public-surface tests (test-plan T-API-1): reflection over every exported type of the facade proves that no
' public signature leaks a Roslyn type or the Roslyn interactive assembly loader, that the runner and static
' entry points carry the trim/AOT annotations, and that the Roslyn Script object model is not re-exposed.
' No network, no process, no file written.

Imports System
Imports System.Collections.Generic
Imports System.Diagnostics.CodeAnalysis
Imports System.Linq
Imports System.Reflection
Imports Nukepayload2.VBScriptDotNet.Hosting
Imports Nukepayload2.VBScriptDotNet.Scripting
Imports Xunit

Public Class HostingApiSurfaceTests

    Private Const RoslynNamespace As String = "Microsoft.CodeAnalysis"
    Private Const InteractiveAssemblyLoaderFullName As String = "Microsoft.CodeAnalysis.Scripting.Hosting.InteractiveAssemblyLoader"

    ''' <summary>
    ''' T-API-1: no public method/property/field/event/constructor/base type/interface/generic argument of any
    ''' exported facade type mentions <c>Microsoft.CodeAnalysis.*</c> or <c>InteractiveAssemblyLoader</c>.
    ''' </summary>
    <Fact>
    Public Sub T_API_1_ExportedSignaturesDoNotExposeRoslynOrTheInteractiveAssemblyLoader()
        Dim exportedTypes = GetType(VBScript).Assembly.GetExportedTypes()
        Assert.NotEmpty(exportedTypes)

        For Each exportedType In exportedTypes
            Dim owner = exportedType.FullName
            AssertAllowed(exportedType, owner & " (exported type)")
            AssertAllowed(exportedType.BaseType, owner & " (base type)")

            For Each implemented As Type In exportedType.GetInterfaces()
                AssertAllowed(implemented, owner & " (interface)")
            Next

            AssertGenericParameterConstraints(exportedType.GetGenericArguments(), owner)

            For Each fieldInfo In exportedType.GetFields(
                    BindingFlags.Public Or BindingFlags.Instance Or BindingFlags.Static Or BindingFlags.DeclaredOnly)
                AssertAllowed(fieldInfo.FieldType, owner & "." & fieldInfo.Name & " (field)")
            Next

            For Each propertyInfo In exportedType.GetProperties(
                    BindingFlags.Public Or BindingFlags.Instance Or BindingFlags.Static Or BindingFlags.DeclaredOnly)
                AssertAllowed(propertyInfo.PropertyType, owner & "." & propertyInfo.Name & " (property)")
                For Each parameterInfo In propertyInfo.GetIndexParameters()
                    AssertAllowed(parameterInfo.ParameterType, owner & "." & propertyInfo.Name & " (index parameter)")
                Next
            Next

            For Each eventInfo In exportedType.GetEvents(
                    BindingFlags.Public Or BindingFlags.Instance Or BindingFlags.Static Or BindingFlags.DeclaredOnly)
                AssertAllowed(eventInfo.EventHandlerType, owner & "." & eventInfo.Name & " (event)")
            Next

            For Each methodInfo In exportedType.GetMethods(
                    BindingFlags.Public Or BindingFlags.Instance Or BindingFlags.Static Or BindingFlags.DeclaredOnly)
                AssertMethodSignature(methodInfo, owner)
            Next

            For Each constructorInfo In exportedType.GetConstructors(
                    BindingFlags.Public Or BindingFlags.Instance Or BindingFlags.Static)
                For Each parameterInfo In constructorInfo.GetParameters()
                    AssertAllowed(parameterInfo.ParameterType, owner & " (constructor parameter " & parameterInfo.Name & ")")
                Next
            Next
        Next
    End Sub

    ''' <summary>
    ''' T-API-1: every <see cref="IVBScriptRunner"/> method and all three static <see cref="VBScript"/> entry
    ''' points are annotated for trim and AOT. The test project targets net10.0, where the attributes exist.
    ''' </summary>
    <Fact>
    Public Sub T_API_1_TrimAndAotAnnotationsCoverRunnerAndFacadeEntries()
        Dim runnerMethods = GetType(IVBScriptRunner).GetMethods(
            BindingFlags.Public Or BindingFlags.Instance Or BindingFlags.DeclaredOnly)
        Assert.Equal(5, runnerMethods.Length)
        For Each methodInfo In runnerMethods
            AssertAnnotated(methodInfo, "IVBScriptRunner." & methodInfo.Name)
        Next

        Dim facadeMethods = GetType(VBScript).GetMethods(
            BindingFlags.Public Or BindingFlags.Static Or BindingFlags.DeclaredOnly)
        Assert.Equal(
            Of String)(
                {"CompileAsync", "EvaluateAsync", "RunAsync"},
                facadeMethods.Select(Function(methodInfo) methodInfo.Name).OrderBy(Function(name) name, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal)
        For Each methodInfo In facadeMethods
            AssertAnnotated(methodInfo, "VBScript." & methodInfo.Name)
        Next
    End Sub

    ''' <summary>
    ''' T-API-1: the session API is annotated for trim and AOT too. <c>Variables</c> is the one member that cannot be
    ''' annotated <em>on the interface</em>: both attributes target methods, and a VB interface property has no
    ''' accessor declaration to attach them to (an accessor block inside an interface is BC30603), while the property
    ''' itself is BC30662. The annotation therefore lives on the implementing getter, and this fact checks it there -
    ''' so the skip below is a placement decision, not a coverage hole. The reflection entry point
    ''' <see cref="IVBScriptSession.GetVariable"/> is annotated as well, and values are only reachable through the
    ''' annotated session.
    ''' </summary>
    <Fact>
    Public Sub T_API_1_TrimAndAotAnnotationsCoverTheSessionApi()
        Dim factoryMethods = GetType(IVBScriptSessionFactory).GetMethods(
            BindingFlags.Public Or BindingFlags.Instance Or BindingFlags.DeclaredOnly)
        Assert.Equal(1, factoryMethods.Length)
        Assert.Equal("CreateSession", factoryMethods(0).Name)
        AssertAnnotated(factoryMethods(0), "IVBScriptSessionFactory.CreateSession")

        Dim sessionMethods = GetType(IVBScriptSession).GetMethods(
            BindingFlags.Public Or BindingFlags.Instance Or BindingFlags.DeclaredOnly)
        Assert.Equal(6, sessionMethods.Length)
        Assert.Contains(sessionMethods, Function(methodInfo) methodInfo.Name = "get_Variables")

        For Each methodInfo In sessionMethods
            If methodInfo.Name = "get_Variables" Then
                ' No accessor declaration exists on a VB interface property, so this getter cannot carry the
                ' attributes; the implementation getter asserted below does.
                Continue For
            End If

            AssertAnnotated(methodInfo, "IVBScriptSession." & methodInfo.Name)
        Next

        Dim implementationProperty = GetType(VBScriptSession).GetProperty(
            "Variables", BindingFlags.Public Or BindingFlags.Instance)
        Assert.NotNull(implementationProperty)
        AssertAnnotated(implementationProperty.GetMethod, "VBScriptSession.Variables")

        Assert.Contains(GetType(IAsyncDisposable), GetType(IVBScriptSession).GetInterfaces())
    End Sub

    ''' <summary>
    ''' T-API-1: the facade does not re-expose Roslyn's scripting object model: no <c>Create</c> entry point and no
    ''' exported <c>Script</c>/<c>Script(Of T)</c>/<c>ScriptState</c>/<c>ScriptOptions</c> type.
    ''' </summary>
    <Fact>
    Public Sub T_API_1_FacadeDoesNotExposeTheRoslynScriptObjectModel()
        Dim facade = GetType(VBScript)

        Assert.Empty(facade.GetMember("Create", BindingFlags.Public Or BindingFlags.Static Or BindingFlags.Instance))
        Assert.Empty(facade.GetMember("Script", BindingFlags.Public Or BindingFlags.Static Or BindingFlags.Instance))

        Dim forbiddenNames = {"Script", "ScriptState", "ScriptOptions"}
        For Each exportedType In facade.Assembly.GetExportedTypes()
            Dim simpleName = exportedType.Name
            Dim tick = simpleName.IndexOf("`"c)
            If tick >= 0 Then
                simpleName = simpleName.Substring(0, tick)
            End If

            Assert.False(
                forbiddenNames.Any(Function(name) String.Equals(name, simpleName, StringComparison.Ordinal)),
                "the facade exports the Roslyn scripting type " & exportedType.FullName)
        Next
    End Sub

    Private Shared Sub AssertMethodSignature(methodInfo As MethodInfo, owner As String)
        Dim label = owner & "." & methodInfo.Name
        AssertAllowed(methodInfo.ReturnType, label & " (return)")

        For Each parameterInfo In methodInfo.GetParameters()
            AssertAllowed(parameterInfo.ParameterType, label & " (parameter " & parameterInfo.Name & ")")
        Next

        If methodInfo.IsGenericMethodDefinition Then
            AssertGenericParameterConstraints(methodInfo.GetGenericArguments(), label)
        End If
    End Sub

    Private Shared Sub AssertGenericParameterConstraints(genericArguments As Type(), label As String)
        For Each argument In genericArguments
            If Not argument.IsGenericParameter Then
                Continue For
            End If

            For Each constraintType In argument.GetGenericParameterConstraints()
                AssertAllowed(constraintType, label & " (constraint of " & argument.Name & ")")
            Next
        Next
    End Sub

    ''' <summary>Unwraps by-ref/array/pointer and generic types, then rejects Roslyn namespaces and the loader.</summary>
    Private Shared Sub AssertAllowed(type As Type, label As String)
        If type Is Nothing Then
            Return
        End If

        If type.IsByRef OrElse type.IsArray OrElse type.IsPointer Then
            AssertAllowed(type.GetElementType(), label)
            Return
        End If

        If type.IsGenericParameter Then
            Return
        End If

        If type.IsGenericType Then
            AssertTypeIsAllowed(type.GetGenericTypeDefinition(), label)
            For Each argument In type.GetGenericArguments()
                AssertAllowed(argument, label)
            Next
            Return
        End If

        AssertTypeIsAllowed(type, label)
    End Sub

    Private Shared Sub AssertTypeIsAllowed(type As Type, label As String)
        Assert.NotEqual(InteractiveAssemblyLoaderFullName, type.FullName)

        Dim ns = type.Namespace
        Dim isRoslyn = ns IsNot Nothing AndAlso
            (String.Equals(ns, RoslynNamespace, StringComparison.Ordinal) OrElse
             ns.StartsWith(RoslynNamespace & ".", StringComparison.Ordinal))

        Assert.False(isRoslyn, label & " exposes " & type.FullName)
    End Sub

    Private Shared Sub AssertAnnotated(methodInfo As MethodInfo, label As String)
        Assert.False(
            methodInfo.GetCustomAttributes(GetType(RequiresUnreferencedCodeAttribute), inherit:=False).Length = 0,
            label & " is missing [RequiresUnreferencedCode]")
        Assert.False(
            methodInfo.GetCustomAttributes(GetType(RequiresDynamicCodeAttribute), inherit:=False).Length = 0,
            label & " is missing [RequiresDynamicCode]")
    End Sub

End Class
