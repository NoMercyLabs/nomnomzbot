// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Chat;

/// <summary>The chat modes of the channel, shared chat and the pinned line. A source that fails leaves the token as written.</summary>
internal sealed class ChatModeHelpers()
    : ChatHelperGroup(
        ChatRow.Read(
            "chat.isShared",
            "false",
            ChatSources.Tracker,
            TemplateHelperFreshness.Memory,
            (data, _) => Task.FromResult<string?>(PeopleText.Flag(data.IsShared()))
        ),
        ChatRow.Read(
            "chat.shared.channels",
            "OtherStreamer",
            ChatSources.Shared,
            TemplateHelperFreshness.Live,
            async (data, _) =>
                (await data.SharedChannelsAsync()) is { Ok: true, Value: { } names }
                    ? string.Join(", ", names)
                    : null
        ),
        Mode(
            "chat.settings.slow",
            "3s",
            s => s.SlowMode ? ChatText.Span(s.SlowModeWaitTime ?? 0) : "off"
        ),
        Mode("chat.settings.followersOnly", "10m", FollowersOnly),
        Mode("chat.settings.subOnly", "false", s => PeopleText.Flag(s.SubscriberMode)),
        Mode("chat.settings.emoteOnly", "false", s => PeopleText.Flag(s.EmoteMode)),
        ChatRow.Read(
            "chat.pinned",
            "Welcome to the stream",
            ChatSources.Pinned,
            TemplateHelperFreshness.Live,
            async (data, _) =>
                (await data.PinnedAsync()) is { Ok: true } read
                    ? read.Value?.Content ?? string.Empty
                    : null
        )
    )
{
    /// <summary>A row that formats one value of the channel's chat settings.</summary>
    private static ChatRow Mode(
        string key,
        string sample,
        Func<TwitchChatSettings, string> format
    ) =>
        ChatRow.Read(
            key,
            sample,
            ChatSources.Settings,
            TemplateHelperFreshness.Live,
            async (data, _) =>
                (await data.SettingsAsync()) is { Ok: true, Value: { } settings }
                    ? format(settings)
                    : null
        );

    /// <summary>The follower time is in minutes; follower mode with no time means any follower may chat.</summary>
    private static string FollowersOnly(TwitchChatSettings settings) =>
        !settings.FollowerMode ? "off"
        : settings.FollowerModeDuration is > 0 and int minutes ? ChatText.Span(minutes * 60L)
        : "on";
}
