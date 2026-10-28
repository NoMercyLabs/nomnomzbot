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
