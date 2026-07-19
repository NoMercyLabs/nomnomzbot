// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Sound.Services;

public interface ISoundClipService
{
    Task<Result<PagedList<SoundClipDto>>> ListAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    );

    Task<Result<SoundClipDto>> GetAsync(
        Guid broadcasterId,
        Guid id,
        CancellationToken ct = default
    );

    /// <summary>
    /// Validates format/size/limits, probes duration, stores the blob via <see cref="ISoundClipStore"/>,
    /// and persists the metadata row.
    /// </summary>
    Task<Result<SoundClipDto>> UploadAsync(
        Guid broadcasterId,
        Guid actorUserId,
        UploadSoundClipRequest request,
        CancellationToken ct = default
    );

    Task<Result<SoundClipDto>> UpdateAsync(
        Guid broadcasterId,
        Guid id,
        Guid actorUserId,
        UpdateSoundClipRequest request,
        CancellationToken ct = default
    );

    Task<Result> DeleteAsync(
        Guid broadcasterId,
        Guid id,
        Guid actorUserId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Resolves a clip reference (UUID or name slug) to a tokened playback URL + effective volume for the
    /// overlay. Returns a failure when the clip is unknown or disabled.
    /// </summary>
    Task<Result<SoundPlaybackDto>> ResolveForPlaybackAsync(
        Guid broadcasterId,
        string clipRef,
        int? volumeOverride,
        CancellationToken ct = default
    );

    /// <summary>Sends an immediate <c>PlaySound</c> to the overlay for dashboard preview / test.</summary>
    Task<Result> PreviewAsync(Guid broadcasterId, Guid id, CancellationToken ct = default);
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

public sealed record UploadSoundClipRequest(
    string Name,
    string DisplayName,
    string FileName,
    string MimeType,
    System.IO.Stream Content,
    int DefaultVolume,
    int CooldownSeconds = 0,
    int MinPermissionLevel = 0,
    string? TriggerWord = null
);

public sealed record UpdateSoundClipRequest(
    string DisplayName,
    int DefaultVolume,
    bool IsEnabled,
    int CooldownSeconds = 0,
    int MinPermissionLevel = 0,
    string? TriggerWord = null
);

public sealed record SoundClipDto(
    Guid Id,
    string Name,
    string DisplayName,
    string MimeType,
    int DurationMs,
    long SizeBytes,
    int DefaultVolume,
    bool IsEnabled,
    // Global per-clip cooldown (seconds) applied to the chat soundboard trigger; 0 = none.
    int CooldownSeconds,
    // Minimum community-standing ladder level to fire the chat trigger (0 = everyone).
    int MinPermissionLevel,
    // Optional bare, prefix-less chat trigger word that plays the clip; null = no chat trigger.
    string? TriggerWord,
    DateTime CreatedAt,
    // Ready-to-play relative URL for the (anonymous, range-enabled) stream endpoint, so the dashboard can
    // preview a clip in-browser — clicking Preview should be audible on the page, not only pushed to the overlay.
    string PreviewUrl
);

public sealed record SoundPlaybackDto(Guid ClipId, string PlaybackUrl, int Volume, int DurationMs);
