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

namespace NomNomzBot.Api.Authorization;

/// <summary>
/// The open-redirect boundary for the served-web RETURN path — the dashboard page the browser lands back on
/// after an OAuth round-trip. Unlike <see cref="ClientRedirectPolicy"/> (which validates absolute deep-link
/// targets), this accepts ONLY a same-origin relative path: it must start with a single <c>/</c>, may carry a
/// query, and is rejected on anything that could re-anchor the origin — <c>//host</c>, <c>/\host</c>,
/// backslashes, CR/LF header-splitting, or a fragment (the callback appends its own fragment carrying the
/// access token). Returns the path unchanged when safe, or null (callers fall back to <c>/</c>).
/// </summary>
public static class ReturnPathPolicy
{
    private const int MaxLength = 512;

    public static string? Normalize(string? returnTo)
    {
        if (string.IsNullOrWhiteSpace(returnTo) || returnTo.Length > MaxLength)
            return null;
        if (returnTo[0] != '/')
            return null;
        // '//evil.com' and '/\evil.com' are scheme-relative in browsers — they re-anchor the host.
        if (returnTo.Length > 1 && (returnTo[1] == '/' || returnTo[1] == '\\'))
            return null;
        if (returnTo.AsSpan().IndexOfAny('\\', '\r', '\n') >= 0)
            return null;
        if (returnTo.Contains('#'))
            return null;
        return returnTo;
    }
}
