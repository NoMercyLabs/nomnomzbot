// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.IO;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Domain.Sound.Entities;
using NomNomzBot.Infrastructure.Sound.Audio;

namespace NomNomzBot.Infrastructure.Sound;

internal sealed class SoundClipService : ISoundClipService
{
    private const int MaxSizeBytes = 10 * 1024 * 1024; // 10 MB per clip (spec D4 safe baseline)
    private const int MaxClipsPerChannel = 100;

    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio/mpeg",
        "audio/ogg",
        "audio/wav",
        "audio/wave",
        "audio/x-wav",
    };

    private readonly IApplicationDbContext _db;
    private readonly ISoundClipStore _store;
    private readonly ISoundClipOverlayNotifier _overlay;

    public SoundClipService(
        IApplicationDbContext db,
        ISoundClipStore store,
        ISoundClipOverlayNotifier overlay
    )
    {
        _db = db;
        _store = store;
        _overlay = overlay;
    }

    public async Task<Result<PagedList<SoundClipDto>>> ListAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        List<SoundClipDto> items = await _db
            .SoundClips.Where(c => c.BroadcasterId == broadcasterId)
            .OrderBy(c => c.Name)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(c => ToDto(c))
            .ToListAsync(ct);

        int total = await _db.SoundClips.CountAsync(c => c.BroadcasterId == broadcasterId, ct);

        return Result<PagedList<SoundClipDto>>.Success(
            new PagedList<SoundClipDto>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    public async Task<Result<SoundClipDto>> GetAsync(
        Guid broadcasterId,
        Guid id,
        CancellationToken ct = default
    )
    {
        SoundClip? clip = await _db.SoundClips.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.Id == id,
            ct
        );

        return clip is null
            ? Result<SoundClipDto>.Failure("Sound clip not found.", "NOT_FOUND")
            : Result<SoundClipDto>.Success(ToDto(clip));
    }

    public async Task<Result<SoundClipDto>> UploadAsync(
        Guid broadcasterId,
        Guid actorUserId,
        UploadSoundClipRequest request,
        CancellationToken ct = default
    )
    {
        // Sniff the first bytes to validate format before doing anything else.
        byte[] header = new byte[12];
        int read = await request.Content.ReadAsync(header, 0, header.Length, ct);
        if (read < 4)
            return Result<SoundClipDto>.Failure(
                "Upload too small to be a valid audio file.",
                "INVALID_FORMAT"
            );

        string? sniffedMime = AudioSniffer.Sniff(header);
        if (sniffedMime is null)
            return Result<SoundClipDto>.Failure(
                "File content is not a supported audio format (mp3, ogg, wav).",
                "INVALID_FORMAT"
            );

        // Reconstruct stream: header bytes + remainder.
        System.IO.Stream combined = new CombinedReadStream(header, read, request.Content);

        // Read entire content to validate size and probe duration.
        using MemoryStream ms = new();
        await combined.CopyToAsync(ms, ct);
        ms.Position = 0;

        if (ms.Length > MaxSizeBytes)
            return Result<SoundClipDto>.Failure(
                $"Clip exceeds the {MaxSizeBytes / 1024 / 1024} MB size limit.",
                "SIZE_EXCEEDED"
            );

        // Validate clip count per channel.
        int clipCount = await _db.SoundClips.CountAsync(c => c.BroadcasterId == broadcasterId, ct);
        if (clipCount >= MaxClipsPerChannel)
            return Result<SoundClipDto>.Failure(
                $"Channel has reached the {MaxClipsPerChannel} clip limit.",
                "LIMIT_REACHED"
            );

        int durationMs = AudioSniffer.ProbeDurationMs(ms, sniffedMime);
        ms.Position = 0;

        // Validate name uniqueness.
        bool nameExists = await _db.SoundClips.AnyAsync(
            c => c.BroadcasterId == broadcasterId && c.Name == request.Name,
            ct
        );
        if (nameExists)
            return Result<SoundClipDto>.Failure(
                $"A clip named '{request.Name}' already exists in this channel.",
                "DUPLICATE_NAME"
            );

        Result<string> storeResult = await _store.PutAsync(
            broadcasterId,
            request.FileName,
            ms,
            sniffedMime,
            ct
        );
        if (!storeResult.IsSuccess)
            return Result<SoundClipDto>.Failure(storeResult.ErrorMessage, storeResult.ErrorCode);

        SoundClip clip = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = broadcasterId,
            Name = request.Name,
            DisplayName = request.DisplayName,
            StorageKey = storeResult.Value,
            MimeType = sniffedMime,
            DurationMs = durationMs,
            SizeBytes = ms.Length,
            DefaultVolume = Math.Clamp(request.DefaultVolume, 0, 100),
            IsEnabled = true,
            CreatedByUserId = actorUserId,
        };

        _db.SoundClips.Add(clip);
        await _db.SaveChangesAsync(ct);

        return Result<SoundClipDto>.Success(ToDto(clip));
    }

    public async Task<Result<SoundClipDto>> UpdateAsync(
        Guid broadcasterId,
        Guid id,
        Guid actorUserId,
        UpdateSoundClipRequest request,
        CancellationToken ct = default
    )
    {
        SoundClip? clip = await _db.SoundClips.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.Id == id,
            ct
        );

        if (clip is null)
            return Result<SoundClipDto>.Failure("Sound clip not found.", "NOT_FOUND");

        clip.DisplayName = request.DisplayName;
        clip.DefaultVolume = Math.Clamp(request.DefaultVolume, 0, 100);
        clip.IsEnabled = request.IsEnabled;

        await _db.SaveChangesAsync(ct);
        return Result<SoundClipDto>.Success(ToDto(clip));
    }

    public async Task<Result> DeleteAsync(
        Guid broadcasterId,
        Guid id,
        Guid actorUserId,
        CancellationToken ct = default
    )
    {
        SoundClip? clip = await _db.SoundClips.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.Id == id,
            ct
        );

        if (clip is null)
            return Result.Failure("Sound clip not found.", "NOT_FOUND");

        string storageKey = clip.StorageKey;

        // Soft-delete the row first; then remove the blob.
        clip.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _store.DeleteAsync(storageKey, ct);
        return Result.Success();
    }

    public async Task<Result<SoundPlaybackDto>> ResolveForPlaybackAsync(
        Guid broadcasterId,
        string clipRef,
        int? volumeOverride,
        CancellationToken ct = default
    )
    {
        SoundClip? clip = Guid.TryParse(clipRef, out Guid clipId)
            ? await _db.SoundClips.FirstOrDefaultAsync(
                c => c.BroadcasterId == broadcasterId && c.Id == clipId && c.IsEnabled,
                ct
            )
            : await _db.SoundClips.FirstOrDefaultAsync(
                c => c.BroadcasterId == broadcasterId && c.Name == clipRef && c.IsEnabled,
                ct
            );

        if (clip is null)
            return Result<SoundPlaybackDto>.Failure(
                $"Sound clip '{clipRef}' not found or disabled.",
                "NOT_FOUND"
            );

        Result<string> urlResult = await _store.GetPlaybackUrlAsync(clip.StorageKey, ct);
        if (!urlResult.IsSuccess)
            return Result<SoundPlaybackDto>.Failure(urlResult.ErrorMessage, urlResult.ErrorCode);

        int volume = volumeOverride.HasValue
            ? Math.Clamp(volumeOverride.Value, 0, 100)
            : clip.DefaultVolume;

        return Result<SoundPlaybackDto>.Success(
            new SoundPlaybackDto(clip.Id, urlResult.Value, volume, clip.DurationMs)
        );
    }

    public async Task<Result> PreviewAsync(
        Guid broadcasterId,
        Guid id,
        CancellationToken ct = default
    )
    {
        Result<SoundPlaybackDto> resolveResult = await ResolveForPlaybackAsync(
            broadcasterId,
            id.ToString(),
            null,
            ct
        );
        if (!resolveResult.IsSuccess)
            return Result.Failure(resolveResult.ErrorMessage, resolveResult.ErrorCode);

        await _overlay.PlaySoundAsync(broadcasterId, resolveResult.Value, ct);
        return Result.Success();
    }

    private static SoundClipDto ToDto(SoundClip c) =>
        new(
            c.Id,
            c.Name,
            c.DisplayName,
            c.MimeType,
            c.DurationMs,
            c.SizeBytes,
            c.DefaultVolume,
            c.IsEnabled,
            c.CreatedAt
        );
}
