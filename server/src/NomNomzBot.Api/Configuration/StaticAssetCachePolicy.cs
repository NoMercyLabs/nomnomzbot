// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

namespace NomNomzBot.Api.Configuration;

/// <summary>
/// The <c>Cache-Control</c> a file in the dashboard bundle is served with, chosen from its request path alone.
/// </summary>
public static class StaticAssetCachePolicy
{
    /// <summary>Always re-fetched: the shell document is stable across builds, so a cached copy is a stale build.</summary>
    public const string EntryPoint = "no-store, no-cache, must-revalidate";

    /// <summary>Content-addressed: the file name carries its hash, so the bytes behind a name never change.</summary>
    public const string Immutable = "public, max-age=31536000, immutable";

    /// <summary>Changes with a build under a stable name: edge-cacheable, but revalidated against the ETag.</summary>
    public const string Revalidated = "public, no-cache, must-revalidate";

    // The build tool writes every hashed file (scripts, styles, fonts) under this folder and nothing else.
    // The service worker (sw.js), the manifest and the icons sit outside it under stable names, so they
    // stay revalidated: a deploy lands on the next load and a repeat visit costs a 304.
    private const string HashedAssetFolder = "/assets/";

    private static readonly string[] EntryPointNames = ["index.html"];

    /// <summary>The policy for <paramref name="requestPath"/> (the address the file is served at).</summary>
    public static string For(string requestPath)
    {
        string fileName = requestPath[(requestPath.LastIndexOf('/') + 1)..];

        foreach (string entryPoint in EntryPointNames)
        {
            if (fileName.Equals(entryPoint, StringComparison.OrdinalIgnoreCase))
                return EntryPoint;
        }

        if (requestPath.StartsWith(HashedAssetFolder, StringComparison.OrdinalIgnoreCase))
            return Immutable;

        return Revalidated;
    }
}
