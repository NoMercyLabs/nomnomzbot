// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Twitch;

// Helix "Streams" category wire models (GET /streams, /streams/key, /streams/followed,
// POST+GET /streams/markers). These records deserialize straight from Twitch's snake_case JSON via the
// transport's naming policy — no per-property annotations. Twitch ids stay strings (they identify other
// users / channels / games / videos); the owning tenant is always passed in as a Guid method argument,
// never here.

/// <summary>
/// One live stream (Get Streams / Get Followed Streams). Twitch returns these only for live channels —
/// an offline channel is simply absent from <c>data[]</c>, so an empty result means "offline".
/// </summary>
public sealed record TwitchStream(
    string Id,
    string UserId,
    string UserLogin,
    string UserName,
    string GameId,
    string GameName,
    string Type,
    string Title,
    IReadOnlyList<string> Tags,
    int ViewerCount,
    DateTimeOffset StartedAt,
    string Language,
    string ThumbnailUrl,
    bool IsMature
);

/// <summary>
/// Filters for Get Streams — all optional. Each list value is repeated as its own query parameter
/// (e.g. two <c>user_id</c> entries). The tenant never appears here; this is a subject-agnostic public read.
/// </summary>
public sealed record TwitchStreamsFilter(
    IReadOnlyList<string>? UserIds = null,
    IReadOnlyList<string>? UserLogins = null,
    IReadOnlyList<string>? GameIds = null,
    IReadOnlyList<string>? Languages = null,
    string? Type = null
);

/// <summary>The single <c>data[0]</c> element of Get Stream Key, wrapping the channel's RTMP key string.</summary>
public sealed record TwitchStreamKey(string StreamKey);

/// <summary>The created marker returned by Create Stream Marker (its id, timestamp, offset and description).</summary>
public sealed record TwitchStreamMarker(
    string Id,
    DateTimeOffset CreatedAt,
    int PositionSeconds,
    string Description
);

/// <summary>Create Stream Marker request body. The broadcaster is the Guid method argument, not part of this body.</summary>
public sealed record CreateStreamMarkerRequest(string UserId, string? Description = null);

/// <summary>
/// One marker within a video grouping (Get Stream Markers). Unlike the create response this carries a
/// <c>url</c> deep-link back to the VOD position and omits the create timestamp's role.
/// </summary>
public sealed record TwitchVideoMarker(
    string Id,
    DateTimeOffset CreatedAt,
    string Description,
    int PositionSeconds,
    string Url
);

/// <summary>One VOD/video grouping its markers (the inner <c>videos[]</c> element of Get Stream Markers).</summary>
public sealed record TwitchMarkedVideo(string VideoId, IReadOnlyList<TwitchVideoMarker> Markers);

/// <summary>
/// One <c>data[]</c> element of Get Stream Markers: a user, with the videos that hold their markers.
/// The endpoint nests markers two levels deep (<c>data[].videos[].markers[]</c>), so each page item is
/// this user-group rather than a flat marker — callers flatten <see cref="Videos"/> to reach individual markers.
/// </summary>
public sealed record TwitchStreamMarkerGroup(
    string UserId,
    string UserName,
    string UserLogin,
    IReadOnlyList<TwitchMarkedVideo> Videos
);
