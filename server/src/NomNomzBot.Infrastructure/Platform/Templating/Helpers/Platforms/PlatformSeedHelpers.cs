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
