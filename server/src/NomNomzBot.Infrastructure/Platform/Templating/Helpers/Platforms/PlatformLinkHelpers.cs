// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Platforms;

/// <summary>The addresses of the connected channels, and of the YouTube stream that is live.</summary>
internal sealed class PlatformLinkHelpers()
    : PlatformHelperGroup(
        Of(AuthEnums.Platform.Twitch, "https://twitch.tv/samplechannel"),
        Of(AuthEnums.Platform.Kick, "https://kick.com/samplechannel"),
        Of(AuthEnums.Platform.YouTube, "https://youtube.com/channel/UCsample"),
        PlatformRow.Custom(
            "platform.link.youtube.live",
            "https://youtube.com/watch?v=sample",
            PlatformSources.Status,
            async call =>
                await call.Data.ConnectionAsync(AuthEnums.Platform.YouTube)
                    is { Status: { IsLive: true, WatchUrl: { } url } }
                    ? url
                    : PlatformText.Nothing
        ),
        PlatformRow.Custom(
            "platform.links",
            "twitch.tv/samplechannel · kick.com/samplechannel · youtube.com/channel/UCsample",
            PlatformSources.Links,
            async call =>
                string.Join(
                    PlatformText.PartSeparator,
                    (await call.Data.LinksAsync()).Select(PlatformNames.Address).OfType<string>()
                )
        )
    )
{
    private static PlatformRow Of(string provider, string sample) =>
        PlatformRow.Custom(
            "platform.link." + provider,
            sample,
            PlatformSources.Links,
            async call =>
                await call.Data.LinkAsync(provider) is { } link
                    ? PlatformNames.Url(link) ?? PlatformText.Nothing
                    : PlatformText.Nothing
        );
}
