' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' L3 tests for the hosting registration surface (test-plan T-DEF-2, T-DEF-4 net10 branch, T-DI-1/2/5/6) plus the
' options validator and the IHostApplicationBuilder overload. No network, no process, no file written.

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading.Tasks
Imports Microsoft.Extensions.Configuration
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options
Imports Nukepayload2.VBScriptDotNet.Hosting
Imports Xunit

Public Class HostingRegistrationTests

    ''' <summary>T-DEF-2: after AddVBScripting the default options carry the vbi.coreclr.rsp default set.</summary>
    <Fact>
    Public Sub T_DEF_2_AddVBScriptingOptionsCarryTheVbiDefaultSet()
        Dim services As New ServiceCollection()
        services.AddVBScripting()

        Using provider = services.BuildServiceProvider()
            Dim options = provider.GetRequiredService(Of IOptions(Of VBScriptOptions)).Value

            For Each expected In {
                    "System",
                    "System.IO",
                    "System.Collections.Generic",
                    "System.Console",
                    "System.Diagnostics",
                    "System.Dynamic",
                    "System.Linq",
                    "System.Text",
                    "System.Threading.Tasks"}
                Assert.True(options.Imports.Contains(expected), "missing default import: " & expected)
            Next

            For Each expected In {"System.Runtime", "Microsoft.VisualBasic", "System.Private.Xml.Linq"}
                Assert.True(options.References.Contains(expected), "missing default reference: " & expected)
            Next

            Assert.Equal(VBScriptDefaults.DefaultImports.Length, options.Imports.Count)
            Assert.Equal(VBScriptDefaults.DefaultReferences.Length, options.References.Count)

            ' The default output is the null one; nothing is written to the console unless the host opts in.
            Assert.IsType(Of NullScriptOutput)(provider.GetRequiredService(Of IScriptOutput)())

            ' The options validator travels with the registration (IValidateOptions is what ValidateOnStart and
            ' IOptions.Value both consult).
            Dim validators = provider.GetServices(Of IValidateOptions(Of VBScriptOptions))().ToArray()
            Assert.Single(validators)
            Assert.IsType(Of VBScriptOptionsValidator)(validators(0))
        End Using
    End Sub

    ''' <summary>
    ''' The IHostApplicationBuilder overload returns the builder (chaining), binds the VBScript configuration
    ''' section into IOptions(Of VBScriptOptions), and still appends the default set after the host's entries.
    ''' </summary>
    <Fact>
    Public Sub AddVBScriptingOnHostApplicationBuilderBindsConfigurationAndReturnsTheBuilder()
        Dim builder = Host.CreateApplicationBuilder()
        builder.Logging.ClearProviders()
        builder.Configuration.AddInMemoryCollection(New Dictionary(Of String, String) From {
            {"VBScript:WorkingDirectory", AppContext.BaseDirectory}
        })

        Dim returned = builder.AddVBScripting(Sub(options) options.OptimizationLevel = VBScriptOptimizationLevel.Release)
        Assert.Same(builder, returned)

        Using host = builder.Build()
            Dim options = host.Services.GetRequiredService(Of IOptions(Of VBScriptOptions)).Value

            Assert.Equal(AppContext.BaseDirectory, options.WorkingDirectory)
            Assert.Equal(VBScriptOptimizationLevel.Release, options.OptimizationLevel)
            Assert.True(options.Imports.Contains("System"), "the default set was not applied")
            Assert.Equal(VBScriptDefaults.DefaultImports.Length, options.Imports.Count)
        End Using
    End Sub

    ''' <summary>The builder overload validates options when the host starts (ValidateOnStart).</summary>
    <Fact>
    Public Async Function AddVBScriptingOnHostApplicationBuilderValidatesOptionsOnStart() As Task
        Dim builder = Host.CreateApplicationBuilder()
        builder.Logging.ClearProviders()
        builder.Configuration.AddInMemoryCollection(New Dictionary(Of String, String) From {
            {"VBScript:WorkingDirectory", Path.Combine(Path.GetTempPath(), "vbscript-missing-" & Guid.NewGuid().ToString("N"))}
        })
        builder.AddVBScripting()

        Using host = builder.Build()
            Dim failure = Await Assert.ThrowsAsync(Of OptionsValidationException)(
                Async Function() As Task
                    Await host.StartAsync()
                End Function)

            Assert.Contains("WorkingDirectory", failure.Message)
        End Using
    End Function

    ''' <summary>
    ''' T-DEF-4 (net10 branch): UseDesktopReferences adds nothing on net10 and the runner logs exactly one warning.
    ''' </summary>
    <Fact>
    Public Sub T_DEF_4_UseDesktopReferencesIsIgnoredOnNet10WithOneWarning()
        ' This test asserts the net10 branch; the test project only targets net10.0.
        Assert.False(VBScriptDefaults.SupportsDesktopReferences)

        Dim sink As New FakeLogSink()
        Dim services As New ServiceCollection()
        services.AddSingleton(Of IHostApplicationLifetime)(New FakeHostApplicationLifetime())
        services.AddSingleton(Of ILogger(Of VBScriptRunner))(New FakeLogger(Of VBScriptRunner)(sink))
        services.AddVBScripting(Sub(options) options.UseDesktopReferences = True)

        Using provider = services.BuildServiceProvider()
            Dim options = provider.GetRequiredService(Of IOptions(Of VBScriptOptions)).Value
            Assert.DoesNotContain(options.References, Function(reference) String.Equals(reference, "System.Drawing", StringComparison.OrdinalIgnoreCase))
            Assert.DoesNotContain(options.References, Function(reference) String.Equals(reference, "System.Windows.Forms", StringComparison.OrdinalIgnoreCase))

            ' The warning is emitted when the runner is constructed (the option only matters when something runs).
            Dim runner = provider.GetRequiredService(Of IVBScriptRunner)()
            Assert.NotNull(runner)

            Dim warnings = sink.Records.Where(Function(record) record.Level = LogLevel.Warning).ToArray()
            Assert.Single(warnings)
            Assert.Contains("UseDesktopReferences", warnings(0).Message)
        End Using
    End Sub

    ''' <summary>T-DI-1: an IScriptOutput registered before AddVBScripting wins and receives the script's Print text.</summary>
    <Fact>
    Public Async Function T_DI_1_OutputRegisteredBeforeAddVBScriptingWins() As Task
        Using fixture As New RunnerFixture()
            Assert.Same(fixture.Output, fixture.Provider.GetRequiredService(Of IScriptOutput)())

            Dim result = Await fixture.Runner.RunCodeAsync("Print(""routed"")")

            ' vbi's formatter quotes strings, so Print("routed") writes "routed".
            Dim expected = """routed""" & Environment.NewLine
            Assert.Equal(expected, result.Output)
            Assert.Equal(expected, fixture.Output.OutText)
        End Using
    End Function

    ''' <summary>T-DI-2: an IScriptOutput registered after AddVBScripting wins (the container's last registration).</summary>
    <Fact>
    Public Sub T_DI_2_OutputRegisteredAfterAddVBScriptingWins()
        Dim custom As New RecordingScriptOutput()
        Dim services As New ServiceCollection()
        services.AddVBScripting()
        services.AddSingleton(Of IScriptOutput)(custom)

        Using provider = services.BuildServiceProvider()
            Assert.Same(custom, provider.GetRequiredService(Of IScriptOutput)())
        End Using
    End Sub

    ''' <summary>T-DI-5: IVBScriptRunner is transient; two resolutions give two instances.</summary>
    <Fact>
    Public Sub T_DI_5_RunnerIsTransient()
        Using fixture As New RunnerFixture()
            Dim first = fixture.Provider.GetRequiredService(Of IVBScriptRunner)()
            Dim second = fixture.Provider.GetRequiredService(Of IVBScriptRunner)()

            Assert.NotNull(first)
            Assert.NotNull(second)
            Assert.NotSame(first, second)
        End Using
    End Sub

    ''' <summary>T-DI-6: UseConsoleOutput replaces the default null output with the console one.</summary>
    <Fact>
    Public Sub T_DI_6_UseConsoleOutputReplacesTheDefaultOutput()
        Dim services As New ServiceCollection()
        services.AddVBScripting()
        Assert.Same(services, services.UseConsoleOutput())

        Using provider = services.BuildServiceProvider()
            Assert.IsType(Of ConsoleScriptOutput)(provider.GetRequiredService(Of IScriptOutput)())
        End Using
    End Sub

    <Fact>
    Public Sub OptionsValidatorAcceptsTheDefaultOptions()
        Dim result = New VBScriptOptionsValidator().Validate(String.Empty, New VBScriptOptions())

        Assert.True(result.Succeeded)
    End Sub

    <Fact>
    Public Sub OptionsValidatorReportsGlobalsTypeWorkingDirectoryAndBlankEntries()
        Dim options As New VBScriptOptions() With {
            .GlobalsType = GetType(System.Text.StringBuilder),
            .WorkingDirectory = Path.Combine(Path.GetTempPath(), "vbscript-missing-" & Guid.NewGuid().ToString("N"))
        }
        options.Imports.Add(String.Empty)
        options.References.Add(" ")

        Dim result = New VBScriptOptionsValidator().Validate(String.Empty, options)

        Assert.True(result.Failed)
        Assert.Contains("GlobalsType", result.FailureMessage)
        Assert.Contains("WorkingDirectory", result.FailureMessage)
        Assert.Contains("Imports", result.FailureMessage)
        Assert.Contains("References", result.FailureMessage)
    End Sub

    ''' <summary>
    ''' The default-set merge must not silently repair host input: blank entries survive it, so the validator is what
    ''' reports them, and the DI path fails options resolution instead of running without the entry.
    ''' </summary>
    <Fact>
    Public Sub BlankEntriesSurviveTheDefaultMergeAndFailValidation()
        Dim options As New VBScriptOptions()
        options.Imports.Add(String.Empty)
        options.References.Add(" ")

        VBScriptDefaults.ApplyDefaults(options)

        Assert.True(options.Imports.Contains(String.Empty), "the default-set merge silently dropped the blank import")
        Assert.True(options.References.Contains(" "), "the default-set merge silently dropped the blank reference")

        Dim validation = New VBScriptOptionsValidator().Validate(String.Empty, options)
        Assert.True(validation.Failed)
        Assert.Contains("Imports", validation.FailureMessage)
        Assert.Contains("References", validation.FailureMessage)

        ' End to end: AddVBScripting + a blank entry fails when the options are resolved.
        Dim services As New ServiceCollection()
        services.AddVBScripting(Sub(configured) configured.Imports.Add(String.Empty))

        Using provider = services.BuildServiceProvider()
            Dim failure = Assert.Throws(Of OptionsValidationException)(
                Sub()
                    Dim unused = provider.GetRequiredService(Of IOptions(Of VBScriptOptions)).Value
                End Sub)

            Assert.True(failure.Failures.Any(Function(message) message.Contains("Imports")), failure.Message)
        End Using
    End Sub

End Class
