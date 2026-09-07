// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.CodeAnalysis.Scripting.Hosting
{
    /// <summary>
    /// Pure candidate builder for probing unmanaged (native) libraries out of the NuGet native probe roots
    /// (design §G2.3). The loader calls <see cref="GetProbeCandidates"/> and checks each candidate on disk;
    /// the candidate list itself is a pure function so the cross-platform naming table is unit-testable
    /// without loading anything. RID fallback is intentionally absent: the host hands over the restore
    /// already picked <c>runtimes/&lt;rid&gt;/native</c> directories (NuGet did the RID graph work).
    /// </summary>
    internal static class NativeLibraryProbe
    {
        /// <summary>
        /// Platform shared-library extension: ".dll" on Windows, ".dylib" on macOS, ".so" elsewhere.
        /// </summary>
        internal static string GetPlatformNativeExtension()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return ".dll";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return ".dylib";
            }

            return ".so";
        }

        /// <summary>
        /// Builds the ordered list of file paths to probe for <paramref name="libraryName"/> across
        /// <paramref name="rootDirectories"/>. For every root the verbatim name, the name plus
        /// <paramref name="nativeExtension"/>, and the Unix-convention "lib" + name + extension are tried.
        /// Names that are rooted or contain a directory separator are left to default resolution (nothing to
        /// probe). No file access happens here.
        /// </summary>
        internal static ImmutableArray<string> GetProbeCandidates(ImmutableArray<string> rootDirectories, string libraryName, string nativeExtension)
        {
            if (rootDirectories.IsDefaultOrEmpty || string.IsNullOrEmpty(libraryName) || string.IsNullOrEmpty(nativeExtension))
            {
                return ImmutableArray<string>.Empty;
            }

            if (Path.IsPathRooted(libraryName) || libraryName.IndexOf('/') >= 0 || libraryName.IndexOf('\\') >= 0)
            {
                return ImmutableArray<string>.Empty;
            }

            var builder = ImmutableArray.CreateBuilder<string>(rootDirectories.Length * 3);
            foreach (var root in rootDirectories)
            {
                builder.Add(Path.Combine(root, libraryName));
                builder.Add(Path.Combine(root, libraryName + nativeExtension));
                builder.Add(Path.Combine(root, "lib" + libraryName + nativeExtension));
            }

            return builder.ToImmutable();
        }
    }
}
