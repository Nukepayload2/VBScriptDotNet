// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System;
using System.Collections.Immutable;

namespace Microsoft.CodeAnalysis.Scripting.Hosting
{
    internal abstract class NuGetPackageResolver
    {
        private const string ReferencePrefix = "nuget:";

        /// <summary>
        /// Syntax is "nuget:name[, version]".
        ///
        /// 本 fork 有意偏离上游：前缀用 <c>OrdinalIgnoreCase</c> 匹配（原 <c>Ordinal</c>）、包名与版本按首个逗号拆分
        /// （原斜杠）、整体及各段 Trim；理由：VB 标识符大小写不敏感，逗号对齐 .NET Interactive/csx。
        /// 上游若改回斜杠或仅接受小写前缀，属误回滚。
        /// </summary>
        internal static bool TryParsePackageReference(string reference, out string name, out string version)
        {
            string trimmed = reference.Trim(' ', '\t', '"');
            if (!trimmed.StartsWith(ReferencePrefix, StringComparison.OrdinalIgnoreCase))
            {
                name = null;
                version = null;
                return false;
            }

            string body = trimmed.Substring(ReferencePrefix.Length);
            int commaIndex = body.IndexOf(',');
            string nameSegment = commaIndex < 0 ? body : body.Substring(0, commaIndex);
            string versionSegment = commaIndex < 0 ? null : body.Substring(commaIndex + 1);

            name = nameSegment.Trim(' ', '\t');
            if (name.Length == 0)
            {
                name = null;
                version = null;
                return false;
            }

            version = versionSegment is null ? string.Empty : versionSegment.Trim(' ', '\t');
            return true;
        }

        internal abstract ImmutableArray<string> ResolveNuGetPackage(string packageName, string packageVersion);
    }
}
