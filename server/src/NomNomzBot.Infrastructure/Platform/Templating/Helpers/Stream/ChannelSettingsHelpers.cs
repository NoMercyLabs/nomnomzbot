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

/// <summary>
/// The channel's own settings, read from its live in-memory state: how to mention it, its language, time zone,
/// local time, personality and command prefix. An unset language reads <c>en</c> and an unset zone <c>UTC</c>.
/// </summary>
internal sealed class ChannelSettingsHelpers()
    : StreamHelperGroup(
        "channel",
        StreamHelperRow.Memory(
            "channel.mention",
            "@SampleChannel",
            (channel, _) =>
                "@"
                + (
                    string.IsNullOrWhiteSpace(channel.DisplayName)
                        ? channel.ChannelName
                        : channel.DisplayName
                )
        ),
        StreamHelperRow.Memory(
            "channel.language",
            "en",
            (channel, _) => string.IsNullOrWhiteSpace(channel.Language) ? "en" : channel.Language
        ),
        StreamHelperRow.Memory(
            "channel.timezone",
            "Europe/Amsterdam",
            (channel, _) => string.IsNullOrWhiteSpace(channel.Timezone) ? "UTC" : channel.Timezone
        ),
        StreamHelperRow.Memory(
            "channel.localTime",
            "15:30",
            (channel, call) => StreamText.LocalTime(call.Scope.Now, channel.Timezone)
        ),
        StreamHelperRow.Memory(
            "channel.personality",
            "Informative",
            (channel, _) => StreamText.Capitalized(channel.Personality)
        ),
        StreamHelperRow.Memory("channel.prefix", "!", (channel, _) => channel.CommandPrefix)
    );
