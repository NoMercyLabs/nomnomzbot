// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace NomNomzBot.Infrastructure.Stream.ShoutoutSuggestions;

/// <summary>
/// The process-wide memory of the shoutout suggestions: Twitch lookups kept for one hour per user, so a busy
/// room does not spend the Twitch rate limit again on every dashboard refresh, and the chatters a streamer
/// dismissed during one stream.
/// </summary>
public sealed class ShoutoutSuggestionState
{
    public static readonly TimeSpan LookupLifetime = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, CachedStreams> _streams = new();
    private readonly ConcurrentDictionary<string, CachedCategory> _categories = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset?> _dismissed = new();

    public bool TryGetStreams(string twitchUserId, DateTimeOffset now, out int streams)
    {
        streams = 0;
        if (!_streams.TryGetValue(twitchUserId, out CachedStreams? hit))
            return false;
        if (now - hit.At >= LookupLifetime)
            return false;
        streams = hit.Count;
        return true;
    }

    public void SetStreams(string twitchUserId, int streams, DateTimeOffset now) =>
        _streams[twitchUserId] = new(streams, now);

    public bool TryGetChannelInfo(
        string twitchUserId,
        DateTimeOffset now,
        out CachedChannelInfo? info
    )
    {
        info = null;
        if (!_categories.TryGetValue(twitchUserId, out CachedCategory? hit))
            return false;
        if (now - hit.At >= LookupLifetime)
            return false;
        info = hit.Info;
        return true;
    }

    public void SetChannelInfo(string twitchUserId, CachedChannelInfo info, DateTimeOffset now) =>
        _categories[twitchUserId] = new(info, now);

    /// <summary>Hides one chatter for the stream that went live at <paramref name="streamStartedAt"/>.</summary>
    public void Dismiss(Guid channelId, string twitchUserId, DateTimeOffset? streamStartedAt) =>
        _dismissed[Key(channelId, twitchUserId)] = streamStartedAt;

    public bool IsDismissed(Guid channelId, string twitchUserId, DateTimeOffset? streamStartedAt) =>
        _dismissed.TryGetValue(Key(channelId, twitchUserId), out DateTimeOffset? at)
        && at == streamStartedAt;

    private static string Key(Guid channelId, string twitchUserId) =>
        $"{channelId:N}:{twitchUserId}";

    private sealed record CachedStreams(int Count, DateTimeOffset At);

    private sealed record CachedCategory(CachedChannelInfo Info, DateTimeOffset At);
}

/// <summary>What Get Channel Information said about one chatter's channel.</summary>
public sealed record CachedChannelInfo(string Login, string DisplayName, string? Category);
