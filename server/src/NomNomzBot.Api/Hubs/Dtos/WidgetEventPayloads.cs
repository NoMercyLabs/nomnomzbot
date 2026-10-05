// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Music.Events;

namespace NomNomzBot.Api.Hubs.Dtos;

// The widget-event payloads that have no richer dashboard twin. Each is the one shape both the live handler and
// the dashboard's Test button send, so a test fire can never carry fewer fields than the stream does.

/// <summary><c>tts_speak</c>: one dispatched utterance. <c>User</c> is the requester's platform user id.</summary>
public record TtsSpeakWidgetPayload(
    string Text,
    string Voice,
    string User,
    int DurationMs,
    string? AudioUrl
);

/// <summary><c>now_playing</c>: the standing music snapshot. <c>RequestedBy</c> is null when the streamer
/// started the track themselves.</summary>
public record NowPlayingWidgetPayload(
    bool IsPlaying,
    string? Track,
    string? Artist,
    string? ArtUrl,
    string? Provider,
    string? TrackUri,
    int DurationMs,
    int ProgressMs,
    DateTimeOffset ObservedAt,
    string? RequestedBy
);

/// <summary><c>track_saved_changed</c>: the current track was liked or unliked.</summary>
public record TrackSavedWidgetPayload(string TrackUri, string? Track, string? Artist, bool IsSaved);

/// <summary><c>ChatMessageEnriched</c>: the link preview resolved for a chat message after it was shown.</summary>
public record ChatMessageEnrichedWidgetPayload(
    string MessageId,
    string? LinkUrl,
    string? Title,
    string? Description,
    string? ImageUrl,
    string? Provider,
    string? UserDisplayName,
    string? UserLogin
);

/// <summary><c>sr_queue</c>: the whole song-request queue, in play order.</summary>
public record SrQueueWidgetPayload(IReadOnlyList<SongRequestQueueSnapshotItem> Items);

/// <summary><c>ad_schedule</c>: the channel's ad schedule, sent on every poll while live (times are UTC, null when none).</summary>
public record AdScheduleWidgetPayload(
    DateTimeOffset? NextAdAt,
    DateTimeOffset? LastAdAt,
    int DurationSeconds,
    int PrerollFreeTimeSeconds,
    int SnoozeCount,
    DateTimeOffset? SnoozeRefreshAt,
    int? TimeUntilNextAdSeconds
);

/// <summary><c>ad_upcoming</c>: an ad is near; sent once per warn threshold (300, 120, 60, 30 and 10 seconds) for each next-ad slot.</summary>
public record AdUpcomingWidgetPayload(
    int SecondsUntilAd,
    int ThresholdSeconds,
    int DurationSeconds,
    DateTimeOffset NextAdAt
);

/// <summary><c>custom.&lt;source&gt;</c>: one reading from a custom data source; every value arrives as text.</summary>
public record CustomDataWidgetPayload(IReadOnlyDictionary<string, string> Fields);
