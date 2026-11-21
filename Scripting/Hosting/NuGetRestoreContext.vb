' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.IO
Imports System.Reflection
Imports System.Runtime.InteropServices
Imports System.Runtime.Versioning
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' The host environment a NuGet restore is mirrored against: target framework, runtime identifier,
    ''' platform description, .NET SDK version and a deterministic fingerprint of the <c>NuGet.config</c> files
    ''' NuGet would see. It is a read-only snapshot for the host; the library fills it in and the default restore
    ''' implementation may fill <see cref="SdkVersion"/> when it performs the first real restore.
    ''' </summary>
    ''' <remarks>
    ''' <see cref="TargetFramework"/> is the running host's framework name, detected the same way the restore
    ''' graph detects it (entry assembly, then the runtime's own view, then the framework description mapped
    ''' onto a name); <see cref="RuntimeIdentifier"/> from <see cref="RuntimeInformation.RuntimeIdentifier"/>
    ''' (OS family plus process architecture on .NET Framework, which has no RID probe),
    ''' <see cref="PlatformVersion"/> from <see cref="RuntimeInformation.OSDescription"/>, and
    ''' <see cref="SourceFingerprint"/> from the paths and existence of the <c>NuGet.config</c> files found by
    ''' walking up from the working directory. No network access is performed. <see cref="SdkVersion"/> stays
    ''' empty until a default restore implementation observes the SDK it selected; hosts that never restore
    ''' therefore see an empty string.
    ''' </remarks>
    Public NotInheritable Class NuGetRestoreContext

        Private Shared ReadOnly s_configFileNames As String() = {"NuGet.config", "nuget.config"}

        Private ReadOnly _targetFramework As String
        Private ReadOnly _runtimeIdentifier As String
        Private ReadOnly _platformVersion As String
        Private ReadOnly _sync As New Object()
        Private _sdkVersion As String
        Private _sourceFingerprint As String

        Friend Sub New(targetFramework As String, runtimeIdentifier As String, platformVersion As String, sdkVersion As String, sourceFingerprint As String)
            _targetFramework = If(targetFramework, String.Empty)
            _runtimeIdentifier = If(runtimeIdentifier, String.Empty)
            _platformVersion = If(platformVersion, String.Empty)
            _sdkVersion = If(sdkVersion, String.Empty)
            _sourceFingerprint = If(sourceFingerprint, String.Empty)
        End Sub

        ''' <summary>
        ''' Framework name of the running core library, for example <c>.NETCoreApp,Version=v10.0</c>; the same
        ''' value the restore cache key and the temporary project's target framework are derived from.
        ''' </summary>
        Public ReadOnly Property TargetFramework As String
            Get
                Return _targetFramework
            End Get
        End Property

        ''' <summary>
        ''' Runtime identifier used to restore runtime/native assets. On .NET Framework builds
        ''' <see cref="RuntimeInformation.RuntimeIdentifier"/> is not available there, so it is the OS family
        ''' plus the process architecture (<c>win-x64</c>, <c>linux-arm64</c>, ...).
        ''' </summary>
        Public ReadOnly Property RuntimeIdentifier As String
            Get
                Return _runtimeIdentifier
            End Get
        End Property

        ''' <summary>Platform description of the host, for example the operating-system version string.</summary>
        Public ReadOnly Property PlatformVersion As String
            Get
                Return _platformVersion
            End Get
        End Property

        ''' <summary>
        ''' The .NET SDK version the default restore implementation selected on its first real restore, or an empty
        ''' string when no restore has run yet or no SDK could be detected.
        ''' </summary>
        Public ReadOnly Property SdkVersion As String
            Get
                SyncLock _sync
                    Return _sdkVersion
                End SyncLock
            End Get
        End Property

        ''' <summary>
        ''' Deterministic digest of the <c>NuGet.config</c> paths (and their existence) that NuGet would walk from
        ''' the working directory. It is never empty: a directory chain without any config still hashes to a
        ''' stable value, so "no config" is distinguishable from "not computed".
        ''' </summary>
        Public ReadOnly Property SourceFingerprint As String
            Get
                SyncLock _sync
                    Return _sourceFingerprint
                End SyncLock
            End Get
        End Property

        ''' <summary>
        ''' Creates the snapshot for the current process. <paramref name="baseDirectory"/> only affects
        ''' <see cref="SourceFingerprint"/>; when it is not supplied the process current directory is used.
        ''' </summary>
        Friend Shared Function CreateDefault(Optional baseDirectory As String = Nothing) As NuGetRestoreContext
            Return New NuGetRestoreContext(
                DetectTargetFramework(),
                DetectRuntimeIdentifier(),
                DetectPlatformVersion(),
                sdkVersion:=String.Empty,
                sourceFingerprint:=ComputeSourceFingerprint(baseDirectory))
        End Function

        ''' <summary>
        ''' Recomputes <see cref="SourceFingerprint"/> for a working directory that changed after the snapshot was
        ''' created (the options object is shared, while the working directory can be configured later).
        ''' </summary>
        Friend Sub UpdateSourceFingerprint(baseDirectory As String)
            Dim fingerprint = ComputeSourceFingerprint(baseDirectory)
            SyncLock _sync
                _sourceFingerprint = fingerprint
            End SyncLock
        End Sub

        ''' <summary>Records the SDK version observed by a real restore; an empty value is ignored.</summary>
        Friend Sub UpdateSdkVersion(sdkVersion As String)
            If String.IsNullOrWhiteSpace(sdkVersion) Then
                Return
            End If

            SyncLock _sync
                _sdkVersion = sdkVersion
            End SyncLock
        End Sub

        Private Shared Function DetectTargetFramework() As String
            ' Single source of truth with the restore graph: NuGetHostCapability detects the host TFM from the
            ' entry assembly (the core library is not usable on .NET Framework — mscorlib carries no
            ' TargetFrameworkAttribute at run time, and the free-form RuntimeInformation description is not a
            ' framework name MSBuild can consume). The scripting assembly grants this one access.
            Return Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting.NuGetHostCapability.CreateDefault().TargetFramework
        End Function

        Private Shared Function DetectRuntimeIdentifier() As String
#If NET10_0 Then
            Dim rid = RuntimeInformation.RuntimeIdentifier
            If Not String.IsNullOrWhiteSpace(rid) Then
                Return rid
            End If
#End If

            Dim family As String
            If RuntimeInformation.IsOSPlatform(OSPlatform.Windows) Then
                family = "win"
            ElseIf RuntimeInformation.IsOSPlatform(OSPlatform.Linux) Then
                family = "linux"
            ElseIf RuntimeInformation.IsOSPlatform(OSPlatform.OSX) Then
                family = "osx"
            Else
                Return String.Empty
            End If

            ' netstandard2.0 has no RuntimeIdentifier property, so add the process architecture to the OS
            ' family. The restore graph has to carry the host's native assets: with the bare family RID
            ' NuGet selects none, and a native package degrades to a runtime load failure instead of the
            ' host-capability diagnostic.
            Dim archSuffix As String = Nothing
            Select Case RuntimeInformation.ProcessArchitecture
                Case Architecture.X64
                    archSuffix = "x64"
                Case Architecture.X86
                    archSuffix = "x86"
                Case Architecture.Arm64
                    archSuffix = "arm64"
                Case Architecture.Arm
                    archSuffix = "arm"
            End Select

            Return If(archSuffix Is Nothing, family, family & "-" & archSuffix)
        End Function

        Private Shared Function DetectPlatformVersion() As String
            Dim description = RuntimeInformation.OSDescription
            If Not String.IsNullOrWhiteSpace(description) Then
                Return description
            End If

            Return Environment.OSVersion.VersionString
        End Function

        ''' <summary>
        ''' Hashes the path and existence flag of every <c>NuGet.config</c>/<c>nuget.config</c> from
        ''' <paramref name="baseDirectory"/> up to the volume root. The file contents are deliberately not read:
        ''' the fingerprint mirrors "which configuration files NuGet will see", not their bytes.
        ''' </summary>
        Private Shared Function ComputeSourceFingerprint(baseDirectory As String) As String
            Dim root As String
            Try
                root = If(String.IsNullOrWhiteSpace(baseDirectory), Directory.GetCurrentDirectory(), baseDirectory)
                root = Path.GetFullPath(root)
            Catch generatedException As Exception
                root = If(baseDirectory, String.Empty)
            End Try

            Dim builder As New StringBuilder()
            builder.Append("root=").Append(NormalizePath(root))

            Try
                Dim current = root
                Dim depth = 0
                Do
                    For Each fileName In s_configFileNames
                        Dim candidate = Path.Combine(current, fileName)
                        builder.Append("|").Append(NormalizePath(candidate)).Append("=").Append(If(File.Exists(candidate), "1", "0"))
                    Next

                    Dim parent = Directory.GetParent(current)
                    If parent Is Nothing OrElse String.Equals(parent.FullName, current, StringComparison.OrdinalIgnoreCase) Then
                        Exit Do
                    End If

                    current = parent.FullName
                    depth += 1
                    If depth > 128 Then
                        Exit Do
                    End If
                Loop
            Catch generatedException As Exception
                ' An unreadable or malformed directory chain still yields a stable non-empty fingerprint.
                builder.Append("|unreadable")
            End Try

            Return ComputeSha256(builder.ToString())
        End Function

        Private Shared Function NormalizePath(rawPath As String) As String
            Try
                Dim full = Path.GetFullPath(rawPath)
                If full.Length > 0 Then
                    full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                End If
                Return full.ToUpperInvariant()
            Catch generatedException As Exception
                Return If(rawPath, String.Empty).ToUpperInvariant()
            End Try
        End Function

        Private Shared Function ComputeSha256(value As String) As String
            Using algorithm = SHA256.Create()
                Dim hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(value))
                Return BitConverter.ToString(hash).Replace("-", String.Empty).ToLowerInvariant()
            End Using
        End Function

    End Class

End Namespace
