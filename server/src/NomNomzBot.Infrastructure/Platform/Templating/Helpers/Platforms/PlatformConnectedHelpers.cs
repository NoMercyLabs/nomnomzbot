// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
