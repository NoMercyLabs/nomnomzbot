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

/// <summary>The connections themselves, the platform with the most viewers, and the stream title per platform.</summary>
internal sealed class PlatformConnectedHelpers()
    : PlatformHelperGroup(
        PlatformRow.Custom(
            "platform.connected",
            "Twitch, Kick, YouTube",
            PlatformSources.Links,
            async call =>
                string.Join(
                    PlatformText.ListSeparator,
                    (await call.Data.LinksAsync()).Select(PlatformText.NameOf)
                )
        ),
        PlatformRow.Custom(
            "platform.count",
            "3",
            PlatformSources.Links,
            async call =>
                await call.Data.LinksAsync() is { Count: > 0 } links
                    ? PlatformText.Count(call, links.Count)
                    : PlatformText.Nothing
        ),
        PlatformRow.Custom(
            "platform.top",
            "Twitch",
            PlatformSources.Status,
            async call =>
                (await PlatformText.WithViewersAsync(call.Data))
                    .OrderByDescending(c => c.Viewers)
                    .Select(c => PlatformText.NameOf(c.Link))
                    .FirstOrDefault()
                ?? PlatformText.Nothing
        ),
        PlatformRow.OfPlatform(
            "platform.title",
            "Building a bot",
            PlatformSources.Status,
            async (call, platform) =>
                await call.Data.ConnectionAsync(platform.Key) is { Status: { IsLive: true } status }
                    ? PlatformText.NothingIfEmpty(status.Title)
                    : PlatformText.Nothing
        )
    );
