// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
