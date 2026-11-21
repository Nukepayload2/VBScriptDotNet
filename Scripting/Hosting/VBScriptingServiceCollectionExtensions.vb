' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Runtime.CompilerServices
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting
Imports Microsoft.Extensions.Configuration
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.DependencyInjection.Extensions
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Logging.Abstractions
Imports Microsoft.Extensions.Options

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' Registers the scripting host services. Only Abstractions packages are used here: the concrete host (and with
    ''' it <see cref="IHostApplicationLifetime"/> and logging) is supplied by the application. A host that only
    ''' has an <see cref="IServiceCollection"/> must register an <see cref="IHostApplicationLifetime"/> and logging
    ''' itself before resolving <see cref="IVBScriptRunner"/>.
    ''' </summary>
    Public Module VBScriptingServiceCollectionExtensions

        ''' <summary>
        ''' Registers the options, the default output (<see cref="NullScriptOutput"/>), one transient
        ''' <see cref="IVBScriptRunner"/>, and the options validator. Existing registrations win: every default uses
        ''' <c>TryAdd</c>, so an output registered earlier stays in place, and an output registered later wins by the
        ''' container's last-registration rule (or use <c>UseConsoleOutput</c>).
        ''' </summary>
        <Extension>
        Public Function AddVBScripting(services As IServiceCollection, Optional configure As Action(Of VBScriptOptions) = Nothing) As IServiceCollection
            Return AddVBScriptingCore(services, configure, configurationSection:=Nothing, validateOnStart:=False)
        End Function

        ''' <summary>
        ''' Same registration plus binding of the <c>VBScript</c> configuration section to
        ''' <see cref="IOptions(Of VBScriptOptions)"/> and fail-fast validation when the host starts. Returns the
        ''' builder so calls can be chained.
        ''' </summary>
        <Extension>
        Public Function AddVBScripting(builder As IHostApplicationBuilder, Optional configure As Action(Of VBScriptOptions) = Nothing) As IHostApplicationBuilder
            If builder Is Nothing Then
                Throw New ArgumentNullException(NameOf(builder))
            End If

            AddVBScriptingCore(builder.Services, configure, builder.Configuration.GetSection("VBScript"), validateOnStart:=True)
            Return builder
        End Function

        ''' <summary>
        ''' Registers the three default NuGet services (restore coordination, restore execution, package
        ''' resolution) and the shared package session as one coherent group, using <c>Replace</c> semantics so
        ''' the group is a known-good baseline the host can then override one service at a time.
        ''' </summary>
        ''' <remarks>
        ''' Call it after <c>AddVBScripting</c>; replacing only <see cref="INuGetRestoreService"/> without also
        ''' replacing <see cref="INuGetPackageResolver"/> (or calling this method) makes the runner constructor throw
        ''' <see cref="VBScriptHostException"/>. The runner compares the resolved service types, so a fully replaced
        ''' trio is accepted and only a half-replaced configuration is rejected.
        ''' </remarks>
        <Extension>
        Public Function AddVBScriptNuGetRestore(services As IServiceCollection) As IServiceCollection
            If services Is Nothing Then
                Throw New ArgumentNullException(NameOf(services))
            End If

            services.Replace(ServiceDescriptor.Singleton(Of NuGetPackageSession)(Function(sp) New NuGetPackageSession()))
            services.Replace(ServiceDescriptor.Singleton(Of INuGetRestoreRunner)(Function(sp) New DotNetRestoreRunnerAdapter()))
            services.Replace(ServiceDescriptor.Singleton(Of INuGetPackageResolver)(
                Function(sp) New SessionNuGetPackageResolver(sp.GetRequiredService(Of NuGetPackageSession)())))
            services.Replace(ServiceDescriptor.Singleton(Of INuGetRestoreService)(
                Function(sp) CreateRestoreService(sp)))

            Return services
        End Function

        ''' <summary>
        ''' Opts into writing script <c>Print</c> output to the process console by replacing the default
        ''' <see cref="NullScriptOutput"/>. Without this call a library never touches stdout/stderr. The captured
        ''' text still comes back in <see cref="VBScriptResult.Output"/>.
        ''' </summary>
        <Extension>
        Public Function UseConsoleOutput(services As IServiceCollection) As IServiceCollection
            If services Is Nothing Then
                Throw New ArgumentNullException(NameOf(services))
            End If

            services.Replace(ServiceDescriptor.Singleton(Of IScriptOutput, ConsoleScriptOutput)())
            Return services
        End Function

        Private Function AddVBScriptingCore(services As IServiceCollection, configure As Action(Of VBScriptOptions), configurationSection As IConfiguration, validateOnStart As Boolean) As IServiceCollection
            If services Is Nothing Then
                Throw New ArgumentNullException(NameOf(services))
            End If

            Dim optionsBuilder = services.AddOptions(Of VBScriptOptions)()

            ' Post-configure runs after the host's own Configure actions, so the vbi default set is appended last and
            ' the host's entries keep their position.
            optionsBuilder.PostConfigure(Sub(options) VBScriptDefaults.ApplyDefaults(options))

            If configurationSection IsNot Nothing Then
                optionsBuilder.Bind(configurationSection)
            End If

            If configure IsNot Nothing Then
                optionsBuilder.Configure(configure)
            End If

            If validateOnStart Then
                optionsBuilder.ValidateOnStart()
            End If

            services.TryAddSingleton(Of IScriptOutput, NullScriptOutput)()
            services.TryAddTransient(Of IVBScriptRunner, VBScriptRunner)()
            services.TryAddSingleton(Of IVBScriptSessionFactory, VBScriptSessionFactory)()
            services.TryAddEnumerable(ServiceDescriptor.Singleton(Of IValidateOptions(Of VBScriptOptions), VBScriptOptionsValidator)())

            ' NuGet three-layer seam. These are the defaults; AddVBScriptNuGetRestore replaces them as one
            ' group, and a host may also replace an individual service (the runner checks the combination).
            services.TryAddSingleton(Of NuGetPackageSession)(Function(sp) New NuGetPackageSession())
            services.TryAddSingleton(Of INuGetRestoreRunner)(Function(sp) New DotNetRestoreRunnerAdapter())
            services.TryAddSingleton(Of INuGetPackageResolver)(
                Function(sp) New SessionNuGetPackageResolver(sp.GetRequiredService(Of NuGetPackageSession)()))
            services.TryAddSingleton(Of INuGetRestoreService)(Function(sp) CreateRestoreService(sp))

            Return services
        End Function

        ''' <summary>
        ''' Logging is optional for the restore default: a bare <see cref="IServiceCollection"/> that registers only
        ''' the logger categories the runner needs must not fail to resolve the restore service. The first-trigger
        ''' Information entry is still emitted whenever an <see cref="ILogger(Of DotnetCliRestoreService)"/> is present.
        ''' </summary>
        Private Function CreateRestoreService(provider As IServiceProvider) As INuGetRestoreService
            Return New DotnetCliRestoreService(
                provider.GetRequiredService(Of IOptions(Of VBScriptOptions))(),
                provider.GetRequiredService(Of NuGetPackageSession)(),
                If(provider.GetService(Of ILogger(Of DotnetCliRestoreService))(), NullLogger(Of DotnetCliRestoreService).Instance),
                provider.GetRequiredService(Of INuGetRestoreRunner)())
        End Function

    End Module

End Namespace
