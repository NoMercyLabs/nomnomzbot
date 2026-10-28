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

namespace NomNomzBot.Application.Chat.Services;

/// <summary>
/// Turns the target a chatter typed (<c>@name</c>) into the login a command looks the user up by. Viewers
/// type the display name they see in chat, which for many names (CJK, accents) is not the login.
/// </summary>
public interface IMentionTargetResolver
{
    /// <summary>
    /// Strips the <c>@</c>. A login-shaped name comes back lowercased. Any other name is matched against known
    /// display names; exactly one match returns that user's login. No match, or an ambiguous one, returns
    /// the parsed text unchanged so the caller keeps its own not-found handling.
    /// </summary>
    Task<string> ResolveLoginAsync(string? rawTarget, CancellationToken ct = default);
}
