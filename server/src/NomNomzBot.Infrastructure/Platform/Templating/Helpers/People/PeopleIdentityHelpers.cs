// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

/// <summary>Names and links: the rows that read only the render's own seeds, no service.</summary>
internal sealed class PeopleIdentityHelpers()
    : PeopleHelperGroup(
        PeopleRow.Text("display", ViewerSides.User, "ViewerOne", call => Named(call.Who.Display)),
        PeopleRow.Text("login", ViewerSides.User, "viewerone", call => Named(call.Who.Login)),
        PeopleRow.Text(
            "mention",
            ViewerSides.Both,
            "@ViewerOne",
            call => Named(call.Who.Display) is { } name ? "@" + name : null
        ) with
        {
            TargetSample = "@ViewerTwo",
        },
        PeopleRow.Text(
            "platform",
            ViewerSides.User,
            "twitch",
            call => Named(call.Who.Provider)?.ToLowerInvariant()
        ),
        PeopleRow.Text(
            "platform.name",
            ViewerSides.User,
            "Twitch",
            call => Named(PeopleText.PlatformName(call.Who.Provider))
        ),
        PeopleRow.Text(
            "shoutoutLink",
            ViewerSides.Target,
            "https://twitch.tv/viewertwo",
            call =>
                IsTwitch(call.Who) && Named(call.Who.Login) is { } login
                    ? "https://twitch.tv/" + login
                    : null
        )
    )
{
    private static string? Named(string value) => value.Length > 0 ? value : null;

    private static bool IsTwitch(ViewerRef who) =>
        string.Equals(who.Provider, "twitch", StringComparison.OrdinalIgnoreCase);
}
