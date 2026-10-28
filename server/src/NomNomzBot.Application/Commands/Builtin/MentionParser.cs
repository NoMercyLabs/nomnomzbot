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

namespace NomNomzBot.Application.Commands.Builtin;

/// <summary>
/// Parses an "@username"-style mention out of a chat-command argument (e.g. the first word of
/// <c>!whisper @user message</c> or a shoutout/raid <c>target</c> parameter). Every existing call site
/// independently re-implemented the same "trim whitespace, strip one leading '@'" step before handing
/// the bare login/name on to its own platform-specific resolution (Helix login lookup, local user
/// lookup, etc.) — this consolidates that shared parsing step only; resolving the parsed token to a
/// user stays call-site-specific.
/// </summary>
public static class MentionParser
{
    /// <summary>
    /// Trims surrounding whitespace and strips one leading '@' from a raw mention argument. A <c>null</c>
    /// input, or one with no non-'@' content, returns an empty string — matching every existing call
    /// site's "no target given" handling, so callers can keep testing for <see cref="string.Length"/> == 0
    /// / <see cref="string.IsNullOrWhiteSpace(string?)"/> exactly as before.
    /// </summary>
    public static string ParseUserMention(string? raw) =>
        raw is null ? string.Empty : raw.Trim().TrimStart('@');
}
