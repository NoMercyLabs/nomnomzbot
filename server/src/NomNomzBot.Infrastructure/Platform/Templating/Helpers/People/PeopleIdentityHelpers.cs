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
