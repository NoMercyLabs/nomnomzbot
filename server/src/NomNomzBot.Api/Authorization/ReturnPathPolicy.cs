// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
