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

using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Platforms;

/// <summary>Which platforms are live now.</summary>
internal sealed class PlatformLiveHelpers()
    : PlatformHelperGroup(
        PlatformRow.Custom(
            "platform.live.list",
            "Twitch, Kick, YouTube",
            PlatformSources.Status,
            async call =>
                string.Join(
                    PlatformText.ListSeparator,
                    (await PlatformText.LiveAsync(call.Data)).Select(c =>
                        PlatformText.NameOf(c.Link)
                    )
                )
        ),
        PlatformRow.Custom(
            "platform.live.count",
            "3",
            PlatformSources.Status,
            async call =>
                (await call.Data.LinksAsync()).Count == 0
                    ? PlatformText.Nothing
                    : PlatformText.Count(call, (await PlatformText.LiveAsync(call.Data)).Count)
        ),
        PlatformRow.OfPlatform(
            "platform.isLive",
            "true",
            PlatformSources.Status,
            async (call, platform) =>
                PeopleText.Flag(
                    await call.Data.ConnectionAsync(platform.Key) is { Status.IsLive: true }
                )
        )
    );
