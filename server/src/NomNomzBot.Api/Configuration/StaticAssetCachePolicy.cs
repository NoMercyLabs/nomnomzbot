// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Api.Configuration;

/// <summary>
/// The <c>Cache-Control</c> a file in the dashboard bundle is served with, chosen from its name alone.
/// </summary>
public static class StaticAssetCachePolicy
{
    /// <summary>Always re-fetched: these names are stable across builds, so a cached copy is a stale build.</summary>
    public const string EntryPoint = "no-store, no-cache, must-revalidate";

    /// <summary>Content-addressed or content-stable, and the bulk of the bytes: never revalidated, edge-cacheable.</summary>
    public const string Immutable = "public, max-age=31536000, immutable";

    /// <summary>Changes with a build under a stable name: edge-cacheable, but revalidated against the ETag.</summary>
    public const string Revalidated = "public, no-cache, must-revalidate";

    private static readonly string[] EntryPointNames = ["composeApp.js", "index.html"];

    private static readonly string[] ImmutableExtensions =
    [
        ".wasm",
        ".ttf",
        ".otf",
        ".woff",
        ".woff2",
    ];

    /// <summary>The policy for <paramref name="fileName"/> (the file's own name, not its path).</summary>
    public static string For(string fileName)
    {
        foreach (string entryPoint in EntryPointNames)
        {
            if (fileName.Equals(entryPoint, StringComparison.OrdinalIgnoreCase))
                return EntryPoint;
        }

        foreach (string extension in ImmutableExtensions)
        {
            if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return Immutable;
        }

        return Revalidated;
    }
}
