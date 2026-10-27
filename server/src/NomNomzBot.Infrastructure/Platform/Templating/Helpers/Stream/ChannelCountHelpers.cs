// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

/// <summary>The channel's follower, subscriber, editor, moderator and VIP counts, as Twitch reports them.</summary>
internal sealed class ChannelCountHelpers()
    : StreamHelperGroup(
        "channel",
        StreamHelperRow.ReadNumber(
            "channel.followers",
            "1,234",
            StreamSources.Followers,
            call => call.Data.FollowerCountAsync(call.Ct),
            (count, call) => StreamText.Grouped(count, call.Culture)
        ),
        StreamHelperRow.ReadNumber(
            "channel.followers.compact",
            "1.2k",
            StreamSources.Followers,
            call => call.Data.FollowerCountAsync(call.Ct),
            (count, _) => StreamText.Compact(count)
        ),
        StreamHelperRow.ReadNumber(
            "channel.subs",
            "80",
            StreamSources.Subscribers,
            call => call.Data.SubscriberCountAsync(call.Ct),
            (count, call) => StreamText.Grouped(count, call.Culture)
        ),
        StreamHelperRow.ReadNumber(
            "channel.editors.count",
            "2",
            StreamSources.Staff,
            call => call.Data.EditorCountAsync(call.Ct),
            (count, call) => StreamText.Grouped(count, call.Culture)
        ),
        StreamHelperRow.ReadNumber(
            "channel.mods.count",
            "6",
            StreamSources.Staff,
            call => call.Data.ModeratorCountAsync(call.Ct),
            (count, call) => StreamText.Grouped(count, call.Culture)
        ),
        StreamHelperRow.ReadNumber(
            "channel.vips.count",
            "4",
            StreamSources.Staff,
            call => call.Data.VipCountAsync(call.Ct),
            (count, call) => StreamText.Grouped(count, call.Culture)
        )
    );
