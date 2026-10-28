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

/// <summary>The platform of the viewer and of the event, read from the render's seeds.</summary>
internal sealed class PlatformSeedHelpers()
    : PlatformHelperGroup(
        PlatformRow.Seed("platform.user", "twitch", data => Key(data.UserProvider)),
        PlatformRow.Seed(
            "platform.user.name",
            "Twitch",
            data => PlatformNames.Find(data.UserProvider)?.Name
        ),
        PlatformRow.Seed(
            "platform.user.icon",
            "🟣",
            data => PlatformNames.Find(data.UserProvider)?.Icon
        ),
        PlatformRow.Seed("platform.event", "twitch", data => Key(data.EventProvider))
    )
{
    private static string? Key(string seed) =>
        PlatformNames.Find(seed)?.Key ?? (seed.Length == 0 ? null : seed.ToLowerInvariant());
}
