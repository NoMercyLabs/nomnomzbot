// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Viewer;

/// <summary>One channel the caller watches: a channel where the caller has viewer activity.</summary>
public sealed record WatchedChannelDto(
    string ChannelId,
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
