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

/// <summary>
/// The Twitch user record of the person (<c>ITwitchUsersApi</c>) and their chat colour (<c>ITwitchChatApi</c>).
/// A person without a Twitch id, or a call that fails, reads as empty.
/// </summary>
internal sealed class PeopleTwitchHelpers()
    : PeopleHelperGroup(
        PeopleRow.Live(
            "color",
            ViewerSides.Both,
            "#1E90FF",
            "chatColor",
            PeopleRow.Map(d => d.Twitch.ColorAsync(), (color, _) => color ?? string.Empty)
        ) with
        {
            TargetSample = "#FF4500",
        },
        PeopleRow.Live(
            "avatar",
            ViewerSides.Both,
            "https://static-cdn.jtvnw.net/jtv_user_pictures/viewerone.png",
            "twitchUser",
            PeopleRow.Map(d => d.Twitch.UserAsync(), (u, _) => u?.ProfileImageUrl ?? string.Empty)
        ) with
        {
            TargetSample = "https://static-cdn.jtvnw.net/jtv_user_pictures/viewertwo.png",
        },
        PeopleRow.Live(
            "bio",
            ViewerSides.Both,
            "I like bots",
            "twitchUser",
            PeopleRow.Map(d => d.Twitch.UserAsync(), (u, _) => u?.Description ?? string.Empty)
        ) with
        {
            TargetSample = "I build castles",
        },
        PeopleRow.Live(
            "createdAt",
            ViewerSides.Both,
            "2022-08-14",
            "twitchUser",
            PeopleRow.Map(
                d => d.Twitch.UserAsync(),
                (u, call) =>
                    u is null ? string.Empty : PeopleText.Date(u.CreatedAt, call.Scope.Timezone)
            )
        ) with
        {
            TargetSample = "2019-05-02",
        },
        PeopleRow.Live(
            "accountAge.days",
            ViewerSides.User,
            "1534",
            "twitchUser",
            PeopleRow.Map(
                d => d.Twitch.UserAsync(),
                (u, call) =>
                    u is null
                        ? string.Empty
                        : PeopleText.Raw(PeopleText.Days(u.CreatedAt, call.Now))
            )
        ),
        PeopleRow.Live(
            "accountAge",
            ViewerSides.Target,
            "7 years, 5 months",
            "twitchUser",
            PeopleRow.Map(
                d => d.Twitch.UserAsync(),
                (u, call) =>
                    u is null ? string.Empty : PeopleText.Age(u.CreatedAt, call.Now, call.Culture)
            )
        )
    );
