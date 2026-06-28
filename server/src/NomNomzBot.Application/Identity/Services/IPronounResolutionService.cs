// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
