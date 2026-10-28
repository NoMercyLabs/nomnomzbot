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
