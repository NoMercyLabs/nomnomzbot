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

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// Lazy, cache-gated pronoun resolution (spec D3). When a viewer appears in chat, call
/// <see cref="ResolveAndApplyAsync"/> once per session to back-fill their pronouns from the provider
/// without blocking the chat pipeline. Subsequent calls for the same viewer within the cooldown
/// window return immediately without hitting the external API.
/// </summary>
public interface IPronounResolutionService
{
    /// <summary>
    /// Look up <paramref name="twitchLogin"/> on the configured pronoun provider and write the result
    /// back to <c>User.PronounId</c> / <c>User.AltPronounId</c> when the provider returns data.
    /// A no-op when: (a) the user has <c>PronounManualOverride = true</c>, (b) the provider returns
    /// null for the viewer, or (c) the cooldown window has not yet elapsed since the last resolution.
    /// </summary>
    Task ResolveAndApplyAsync(Guid userId, string twitchLogin, CancellationToken ct = default);
}
