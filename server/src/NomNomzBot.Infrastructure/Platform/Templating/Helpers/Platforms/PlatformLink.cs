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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Platforms;

/// <summary>One platform connection of the streamer: a channel row of one provider that the owner connected.</summary>
/// <param name="Provider">The provider key (<c>twitch</c>, <c>kick</c>, <c>youtube</c>).</param>
/// <param name="ChannelId">The channel row of this connection (the tenant id the platform services key on).</param>
/// <param name="ExternalChannelId">The platform's own channel id.</param>
/// <param name="Slug">The channel's lower-case login name on the platform.</param>
/// <param name="IsLive">The live flag the platform webhooks and pollers keep on the channel row.</param>
public sealed record PlatformLink(
    string Provider,
    Guid ChannelId,
    string ExternalChannelId,
    string Slug,
    bool IsLive
);

/// <summary>What one connection shows right now. Viewers is null when the platform reports no count.</summary>
public sealed record PlatformStatus(bool IsLive, int? Viewers, string? Title, string? WatchUrl);
