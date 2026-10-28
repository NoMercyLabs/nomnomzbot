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

using System.Globalization;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Platforms;

/// <summary>The viewers now: per platform, in total, as a breakdown and as a share.</summary>
internal sealed class PlatformViewerHelpers()
    : PlatformHelperGroup(
        PlatformRow.Custom(
            "platform.viewers.total",
            "60",
            PlatformSources.Status,
            async call =>
            {
                List<(PlatformLink Link, int Viewers)> counted =
                    await PlatformText.WithViewersAsync(call.Data);
                return counted.Count == 0
                    ? PlatformText.Nothing
                    : PlatformText.Count(call, counted.Sum(c => (long)c.Viewers));
            }
        ),
        Of(AuthEnums.Platform.Twitch, "42"),
        Of(AuthEnums.Platform.Kick, "10"),
        Of(AuthEnums.Platform.YouTube, "8"),
        PlatformRow.Custom(
            "platform.viewers.breakdown",
            "Twitch 42 · Kick 10 · YouTube 8",
            PlatformSources.Status,
            async call =>
                string.Join(
                    PlatformText.PartSeparator,
                    (await PlatformText.WithViewersAsync(call.Data)).Select(c =>
                        PlatformText.NameOf(c.Link) + " " + PlatformText.Count(call, c.Viewers)
                    )
                )
        ),
        PlatformRow.OfPlatform(
            "platform.viewers.share",
            "17%",
            PlatformSources.Status,
            async (call, platform) =>
            {
                List<(PlatformLink Link, int Viewers)> counted =
                    await PlatformText.WithViewersAsync(call.Data);
                long total = counted.Sum(c => (long)c.Viewers);
                (PlatformLink Link, int Viewers) one = counted.FirstOrDefault(c =>
                    c.Link.Provider == platform.Key
                );
                return one.Link is null || total == 0
                    ? PlatformText.Nothing
                    : Math.Round(100.0 * one.Viewers / total, MidpointRounding.AwayFromZero)
                        .ToString("0", CultureInfo.InvariantCulture) + "%";
            }
        )
    )
{
    private static PlatformRow Of(string provider, string sample) =>
        PlatformRow.Custom(
            "platform.viewers." + provider,
            sample,
            PlatformSources.Status,
            async call =>
                await call.Data.ConnectionAsync(provider)
                    is { Status: { IsLive: true, Viewers: { } viewers } }
                    ? PlatformText.Count(call, viewers)
                    : PlatformText.Nothing
        );
}
