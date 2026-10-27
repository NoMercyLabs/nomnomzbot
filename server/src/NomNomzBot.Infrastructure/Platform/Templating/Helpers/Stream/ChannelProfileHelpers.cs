// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

/// <summary>
/// What Twitch holds about the channel: its tags, content labels, branded-content flag, team, latest past
/// broadcast and the age of the account. A channel without a team or a past broadcast has no value for those.
/// </summary>
internal sealed class ChannelProfileHelpers()
    : StreamHelperGroup(
        "channel",
        Info("channel.tags", "English, Coding", info => string.Join(", ", info.Tags)),
        Info(
            "channel.ccls",
            "MatureGame",
            info => string.Join(", ", info.ContentClassificationLabels)
        ),
        Info("channel.isBranded", "false", info => NumberText.Flag(info.IsBrandedContent)),
        StreamHelperRow.Read(
            "channel.team",
            "SampleTeam",
            StreamSources.Teams,
            call => call.Data.TeamsAsync(call.Ct),
            (teams, _) => teams.FirstOrDefault()?.TeamDisplayName
        ),
        Video("channel.vod.latest", "https://www.twitch.tv/videos/123", video => video.Url),
        Video("channel.vod.latest.title", "Chill Saturday", video => video.Title),
        StreamHelperRow.Read(
            "channel.age",
            "6 years, 2 months",
            StreamSources.Owner,
            call => call.Data.OwnerAsync(call.Ct),
            (owner, call) => StreamText.AgeInWords(owner.CreatedAt, call.Scope.Now, call.Culture)
        )
    )
{
    private static StreamHelperRow Info(
        string key,
        string sample,
        Func<TwitchChannelInformation, string> render
    ) =>
        StreamHelperRow.Read(
            key,
            sample,
            StreamSources.ChannelInfo,
            call => call.Data.ChannelInfoAsync(call.Ct),
            (info, _) => render(info)
        );

    private static StreamHelperRow Video(
        string key,
        string sample,
        Func<TwitchVideo, string> render
    ) =>
        StreamHelperRow.Read(
            key,
            sample,
            StreamSources.Videos,
            call => call.Data.LatestVideoAsync(call.Ct),
            (video, _) => render(video)
        );
}
