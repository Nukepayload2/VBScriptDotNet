' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' L3 tests for the hosting default set and options (test-plan T-DEF-1/3/5). No network, no process, no file
' written: the only file read is vbi.rsp, which the test project already copies to its output directory.

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks
Imports Nukepayload2.VBScriptDotNet.Hosting
Imports Nukepayload2.VBScriptDotNet.Scripting
Imports Xunit

Public Class HostingDefaultsTests

    Private Shared ReadOnly s_referenceLine As New Regex("^/r:(.+)$", RegexOptions.CultureInvariant)
    Private Shared ReadOnly s_importLine As New Regex("^/imports:(.+)$", RegexOptions.CultureInvariant)

    <Fact>
    Public Sub T_DEF_1_EmbeddedDefaultsEqualRepositoryResponseFile()
        Dim rspPath = Path.Combine(AppContext.BaseDirectory, "vbi.rsp")
        Assert.True(File.Exists(rspPath), "vbi.rsp is missing from the test output directory.")

        Dim expectedReferences As New List(Of String)()
        Dim expectedImports As New List(Of String)()

        For Each rawLine In File.ReadAllLines(rspPath)
            Dim line = rawLine.Trim()

            Dim match = s_referenceLine.Match(line)
            If match.Success Then
                expectedReferences.Add(match.Groups(1).Value.Trim())
                Continue For
            End If

            match = s_importLine.Match(line)
            If match.Success Then
                expectedImports.Add(match.Groups(1).Value.Trim())
            End If
        Next

        Assert.NotEmpty(expectedReferences)
        Assert.NotEmpty(expectedImports)
        Assert.Equal(expectedReferences.Count, VBScriptDefaults.DefaultReferences.Length)
        Assert.Equal(expectedImports.Count, VBScriptDefaults.DefaultImports.Length)
        Assert.Equal(Of String)(expectedReferences, VBScriptDefaults.DefaultReferences, StringComparer.OrdinalIgnoreCase)
        Assert.Equal(Of String)(expectedImports, VBScriptDefaults.DefaultImports, StringComparer.OrdinalIgnoreCase)
    End Sub

    <Fact>
    Public Sub T_DEF_3_UserEntriesKeepTheirPlaceAndDefaultsAreDeduplicated()
        Dim options As New VBScriptOptions()
        options.[Imports].Add("System.Text.Json")
        options.[Imports].Add("System")
        options.References.Add("System.Text.Json")

        VBScriptDefaults.ApplyDefaults(options)

        ' Host entries keep their position and are not replaced by the default set.
        Assert.Equal("System.Text.Json", options.[Imports](0))
        Assert.Equal("System", options.[Imports](1))
        Assert.Equal(1, CountIgnoringCase(options.[Imports], "System.Text.Json"))
        Assert.Equal(1, CountIgnoringCase(options.[Imports], "System"))
        Assert.Equal(VBScriptDefaults.DefaultImports.Length + 1, options.[Imports].Count)

        For Each expected In VBScriptDefaults.DefaultImports
            Assert.True(
                options.[Imports].Any(Function(item) String.Equals(item, expected, StringComparison.OrdinalIgnoreCase)),
                "missing default import: " & expected)
        Next

        ' Same rule for references, and applying twice must not duplicate anything.
        Assert.Equal(VBScriptDefaults.DefaultReferences.Length + 1, options.References.Count)
        For Each expected In VBScriptDefaults.DefaultReferences
            Assert.True(
                options.References.Any(Function(item) String.Equals(item, expected, StringComparison.OrdinalIgnoreCase)),
                "missing default reference: " & expected)
        Next

        VBScriptDefaults.ApplyDefaults(options)
        Assert.Equal(VBScriptDefaults.DefaultImports.Length + 1, options.[Imports].Count)
        Assert.Equal(VBScriptDefaults.DefaultReferences.Length + 1, options.References.Count)
    End Sub

    ''' <summary>
    ''' T-DEF-3 boundary: the duplicate check is case-insensitive, so a host entry spelled "system" suppresses the
    ''' default "System" and survives unchanged (host entries are never replaced by the default set).
    ''' </summary>
    <Fact>
    Public Sub T_DEF_3_DuplicatesAreDroppedCaseInsensitively()
        Dim options As New VBScriptOptions()
        options.[Imports].Add("system")
        options.References.Add("system.runtime")

        VBScriptDefaults.ApplyDefaults(options)

        Assert.Equal("system", options.[Imports](0))
        Assert.Equal(1, CountIgnoringCase(options.[Imports], "System"))
        Assert.Equal(VBScriptDefaults.DefaultImports.Length, options.[Imports].Count)

        Assert.Equal("system.runtime", options.References(0))
        Assert.Equal(1, CountIgnoringCase(options.References, "System.Runtime"))
        Assert.Equal(VBScriptDefaults.DefaultReferences.Length, options.References.Count)
    End Sub

    <Fact>
    Public Async Function T_DEF_5_GlobalsTypeMustDeriveFromVBScriptGlobals() As Task
        Dim options As New VBScriptOptions() With {.GlobalsType = GetType(System.Text.StringBuilder)}

        Dim exception = Await Assert.ThrowsAsync(Of VBScriptHostException)(
            Async Function() As Task
                Await VBScript.RunAsync("Dim x = 1", options)
            End Function)

        Assert.Contains("VBScriptGlobals", exception.Message)
        Assert.Contains("Globals", exception.Message)
    End Function

    Private Shared Function CountIgnoringCase(items As IEnumerable(Of String), value As String) As Integer
        Return items.Count(Function(item) String.Equals(item, value, StringComparison.OrdinalIgnoreCase))
    End Function

End Class
