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

// Helix "Videos" category wire models (GET /videos, DELETE /videos). These records deserialize
// straight from Twitch's snake_case JSON via the transport's naming policy — no per-property
// annotations. Twitch ids stay strings; the owning tenant is always passed in as a Guid method
// argument, never here. The opaque "duration" field (e.g. "3h8m33s") stays a string as Twitch sends it.

/// <summary>One published video — a past broadcast, highlight, or upload (Get Videos).</summary>
public sealed record TwitchVideo(
    string Id,
    string StreamId,
    string UserId,
    string UserLogin,
    string UserName,
    string Title,
    string Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset PublishedAt,
    string Url,
    string ThumbnailUrl,
    string Viewable,
    int ViewCount,
    string Language,
    string Type,
    string Duration,
    IReadOnlyList<TwitchVideoMutedSegment>? MutedSegments
);

/// <summary>One muted segment within a video: where it starts (<c>offset</c>) and how long it runs, in seconds.</summary>
public sealed record TwitchVideoMutedSegment(int Duration, int Offset);
