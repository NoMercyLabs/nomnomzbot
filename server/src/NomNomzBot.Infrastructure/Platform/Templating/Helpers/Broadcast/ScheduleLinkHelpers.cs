// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Broadcast;

/// <summary>The two links to the schedule: the Twitch page and the iCalendar feed. Both are pure URLs of the channel.</summary>
internal sealed class ScheduleLinkHelpers()
    : BroadcastHelperGroup<ScheduleData>(
        ScheduleRows.OfChannel(
            "schedule.link",
            "https://twitch.tv/samplechannel/schedule",
            channel => $"https://twitch.tv/{channel.ChannelName}/schedule"
        ),
        ScheduleRows.OfChannel(
            "schedule.ical",
            "https://api.twitch.tv/helix/schedule/icalendar?broadcaster_id=123456",
            channel =>
                $"https://api.twitch.tv/helix/schedule/icalendar?broadcaster_id={channel.TwitchChannelId}"
        )
    );
