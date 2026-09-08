// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis.Scripting.Hosting
{
    /// <summary>
    /// Optional host seam invoked by <see cref="CommandLineRunner"/> before a submission or file script is
    /// compiled. A host that supports <c>#R "nuget:…"</c> restores the referenced packages into its session
    /// and returns any blocking, <c>#R</c>-anchored diagnostics; an empty result lets compilation proceed.
    /// A null coordinator (the default) leaves the runner byte-for-byte identical to the no-nuget baseline.
    /// </summary>
    internal interface INuGetRestoreCoordinator
    {
        /// <summary>
        /// Pre-scans the code about to be compiled, restores any referenced NuGet packages, and returns
        /// diagnostics that should block the submission (anchored at the offending <c>#R</c> directive).
        /// </summary>
        /// <param name="code">The submission or file-script source text about to be compiled.</param>
        /// <param name="filePath">The file path of the source, or empty for an interactive submission.</param>
        /// <param name="options">
        /// The <see cref="ScriptOptions"/> the submission will be compiled with, or null when the caller has
        /// no options (legacy shape). When non-null the host may expand <c>#load</c> directives through
        /// <see cref="ScriptOptions.SourceResolver"/> / <see cref="ScriptOptions.ParseOptions"/> so NuGet
        /// references nested inside loaded files are restored before compilation.
        /// </param>
        Task<ImmutableArray<Diagnostic>> PrepareCompilationAsync(SourceText code, string? filePath, ScriptOptions? options, CancellationToken cancellationToken);
    }
}
