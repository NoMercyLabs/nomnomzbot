// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.MediaShare.Dtos;

/// <summary>Submit body — a Twitch-clip or YouTube URL (media-share.md §3).</summary>
public sealed record SubmitMediaRequest(string Url);

/// <summary>One media-share queue item (media-share.md §3).</summary>
public sealed record MediaShareRequestDto(
    Guid Id,
    Guid RequesterUserId,
    string SourceType,
    string SourceUrl,
    string MediaRef,
    string? Title,
    int DurationSeconds,
    string? ThumbnailUrl,
    string Status,
    int? QueuePosition,
    DateTime RequestedAt
);

/// <summary>Queue filter by status (media-share.md §5).</summary>
public sealed record MediaShareFilter(string? Status);

/// <summary>Reorder body — the new 1-based play position (media-share.md §5).</summary>
public sealed record ReorderMediaRequest(int Position);

/// <summary>The per-channel media-share config (media-share.md §3).</summary>
public sealed record MediaShareConfigDto(
    bool IsEnabled,
    bool RequireApproval,
    bool AllowTwitchClips,
    bool AllowYouTube,
    int MaxDurationSeconds,
    long? EntryCost,
    int MaxQueueLength,
    int PerUserCooldownSeconds
);

/// <summary>Update body for the media-share config (media-share.md §5).</summary>
public sealed record UpdateMediaShareConfigRequest(
    bool IsEnabled,
    bool RequireApproval,
    bool AllowTwitchClips,
    bool AllowYouTube,
    int MaxDurationSeconds,
    long? EntryCost,
    int MaxQueueLength,
    int PerUserCooldownSeconds
);
