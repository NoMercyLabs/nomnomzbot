// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.DevPlatform.Projects;

/// <summary>
/// Wraps a single authored source string into a one-file project (dev-platform.md §4.2). This is the ONE transformation
/// that (a) the compile/publish flow applies to legacy single-source authoring so it keeps working, and (b) the
/// <c>SourceCode → FilesJson+ManifestJson</c> backfill migration mirrors row-for-row. Kept here so both sides — and the
/// tests that prove them — share exactly one definition of "a one-file project".
/// </summary>
public static class ProjectScaffold
{
    /// <summary>
    /// The conventional entry filename for a single-file project of the given kind/framework. The extension drives
    /// esbuild's loader selection (react → <c>.tsx</c>, vue → <c>.vue</c>), so it must match the framework. Kept in
    /// lock-step with the backfill migration's <c>CASE</c> over <c>Framework</c>.
    /// </summary>
    public static string EntryFileName(string kind, string framework)
    {
        if (string.Equals(kind, "script", StringComparison.OrdinalIgnoreCase))
            return "index.ts";

        return framework.Trim().ToLowerInvariant() switch
        {
            "vue" => "index.vue",
            "react" => "index.tsx",
            "vanilla" => "index.html",
            _ => "index.js",
        };
    }

    /// <summary>
    /// Builds the one-file project (<c>{ entry: source }</c> + manifest) for a single authored source. Framework is
    /// normalized to lower-case; dependencies start empty (a single-file author declares none).
    /// </summary>
    public static (Dictionary<string, string> Files, ProjectManifest Manifest) SingleFile(
        string kind,
        string framework,
        string source
    )
    {
        string normalizedFramework = framework.Trim().ToLowerInvariant();
        string entry = EntryFileName(kind, normalizedFramework);
        Dictionary<string, string> files = new(StringComparer.Ordinal) { [entry] = source };
        ProjectManifest manifest = new(entry, kind, normalizedFramework, []);
        return (files, manifest);
    }
}
