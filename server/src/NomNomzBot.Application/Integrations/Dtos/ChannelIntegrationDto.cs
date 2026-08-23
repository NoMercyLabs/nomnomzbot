// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Integrations.Dtos;

/// <summary>
/// The channel-wide connection state of a single integration entry (Twitch, custom bot, Spotify,
/// Discord, YouTube, OBS) as shown on the integrations screen and the dashboard shell. Carries only
/// the persisted connection facts — the request-specific OAuth connect URL is layered on by the
/// integrations controller. Distinct from <see cref="IntegrationStatusDto"/>, which is the per-OAuth
/// provider view (scope sets / capabilities / re-auth).
/// </summary>
public sealed record ChannelIntegrationDto(
    string Id,
    string Name,
    string Category,
    string Description,
    bool Connected,
    string? ConnectedAs,
    // S003 — the live-observed auth state of a CONNECTED integration: "needs_reauth" (a call came back
    // 401 — the token is dead), "forbidden" (a call came back 403 for a reason other than premium — the
    // grant lacks permission), or null when healthy/unobserved. Never populated for a disconnected entry
    // (Connected=false already says everything there is to say).
    string? AuthStatus = null
);
