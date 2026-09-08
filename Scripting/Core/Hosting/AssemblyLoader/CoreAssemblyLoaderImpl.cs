// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Microsoft.CodeAnalysis.Scripting.Hosting
{
    internal sealed class CoreAssemblyLoaderImpl : AssemblyLoaderImpl
    {
        private readonly LoadContext _inMemoryAssemblyContext;

        // Native probe roots shared by every LoadContext. The host pushes the NuGet
        // runtimes/<rid>/native directories here (design §G2); an empty set keeps the loader probing
        // nothing, exactly like before the seam existed.
        private ImmutableArray<string> _nativeProbeRoots = ImmutableArray<string>.Empty;

        internal CoreAssemblyLoaderImpl(InteractiveAssemblyLoader loader)
            : base(loader)
        {
            _inMemoryAssemblyContext = new LoadContext(this, null);
        }

        internal override void AddNativeProbeRoot(string directory)
        {
            Debug.Assert(directory != null);

            if (!_nativeProbeRoots.Contains(directory, StringComparer.Ordinal))
            {
                _nativeProbeRoots = _nativeProbeRoots.Add(directory);
            }
        }

        internal override ImmutableArray<string> NativeProbeRoots => _nativeProbeRoots;

        internal override void ResetNativeProbeRoots()
        {
            _nativeProbeRoots = ImmutableArray<string>.Empty;
        }

        public override Assembly LoadFromStream(Stream peStream, Stream pdbStream)
        {
            return _inMemoryAssemblyContext.LoadFromStream(peStream, pdbStream);
        }

        public override AssemblyAndLocation LoadFromPath(string path)
        {
            // Create a new context that knows the directory where the assembly was loaded from
            // and uses it to resolve dependencies of the assembly. We could create one context per directory,
            // but there is no need to reuse contexts.
            var assembly = new LoadContext(this, Path.GetDirectoryName(path)).LoadFromAssemblyPath(path);

            return new AssemblyAndLocation(assembly, path, fromGac: false);
        }

        public override void Dispose()
        {
            // nop
        }

        private sealed class LoadContext : AssemblyLoadContext
        {
            private readonly CoreAssemblyLoaderImpl _owner;
            private readonly string _loadDirectoryOpt;
            private readonly InteractiveAssemblyLoader _loader;

            internal LoadContext(CoreAssemblyLoaderImpl owner, string loadDirectoryOpt)
            {
                Debug.Assert(owner != null);

                _owner = owner;
                _loader = owner.Loader;
                _loadDirectoryOpt = loadDirectoryOpt;

                // CoreCLR resolves assemblies in steps:
                //
                //   1) Call AssemblyLoadContext.Load -- our context returns null
                //   2) TPA list
                //   3) Default.Resolving event
                //   4) AssemblyLoadContext.Resolving event -- hooked below
                //
                // What we want is to let the default context load assemblies it knows about (this includes already loaded assemblies,
                // assemblies in AppPath, platform assemblies, assemblies explciitly resolved by the App by hooking Default.Resolving, etc.).
                // Only if the assembly can't be resolved that way, the interactive resolver steps in.
                //
                // This order is necessary to avoid loading assemblies twice (by the host App and by interactive loader).

                Resolving += (_, assemblyName) =>
                    _loader.ResolveAssembly(AssemblyIdentity.FromAssemblyReference(assemblyName), _loadDirectoryOpt);
            }

            protected override Assembly Load(AssemblyName assemblyName) => null;

#if NET10_0
            // Native (unmanaged) probing seam (design §G2). The runtime calls this when a P/Invoke name has
            // to be bound in this context. Each probe root is tried with the verbatim name, the name plus the
            // platform extension, and the Unix-convention "lib"+name+extension; on a hit the library is loaded
            // from that path, otherwise default resolution decides. With an empty root set this forwards to the
            // base implementation, keeping the no-nuget behavior byte-identical.
            protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
            {
                var roots = _owner._nativeProbeRoots;
                if (!roots.IsDefaultOrEmpty && !string.IsNullOrEmpty(unmanagedDllName))
                {
                    var candidates = NativeLibraryProbe.GetProbeCandidates(roots, unmanagedDllName, NativeLibraryProbe.GetPlatformNativeExtension());
                    foreach (var candidate in candidates)
                    {
                        if (File.Exists(candidate))
                        {
                            return LoadUnmanagedDllFromPath(candidate);
                        }
                    }
                }

                return base.LoadUnmanagedDll(unmanagedDllName);
            }
#endif
        }
    }
}
