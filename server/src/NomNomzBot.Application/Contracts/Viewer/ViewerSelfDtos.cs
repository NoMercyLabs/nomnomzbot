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

namespace NomNomzBot.Application.Contracts.Viewer;

/// <summary>One channel the caller watches: a channel where the caller has viewer activity.</summary>
public sealed record WatchedChannelDto(
    Guid ChannelId,
    string Name,
    string DisplayName,
    string? ProfileImageUrl,
    bool IsLive,
    DateTime? LastSeenAt
);

/// <summary>
/// The caller's OWN tiles in one channel. Never carries another viewer's data. <c>Balance</c> is null when the
/// channel has points off or the caller has no account; <c>FollowedAt</c> is null when unknown; <c>SubTier</c> is
/// the raw tier string the analytics profile keeps (null when not subscribed).
/// </summary>
public sealed record ViewerSelfSummaryDto(
    DateTime? FirstSeenAt,
    DateTime? LastSeenAt,
    long TotalMessages,
    long TotalCommandsUsed,
    long TotalSongRequests,
    bool PointsEnabled,
    long? Balance,
    bool SongRequestsEnabled,
    DateTime? FollowedAt,
    string? SubTier,
    bool IsLive
);

/// <summary>One of the caller's own songs in the queue. <c>Position</c> is 1-based: 1 plays next.</summary>
public sealed record ViewerQueuedSongDto(
    int Position,
    string TrackName,
    string Artist,
    string? ImageUrl,
    int DurationMs,
    string Code
);

/// <summary>The caller's own songs in one channel's queue, plus the full queue length for context.</summary>
public sealed record ViewerQueueDto(int QueueLength, IReadOnlyList<ViewerQueuedSongDto> Songs);
