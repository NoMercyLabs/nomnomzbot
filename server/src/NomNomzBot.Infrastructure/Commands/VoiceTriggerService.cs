// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Assets.Services;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Commands.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Commands;

/// <inheritdoc cref="IVoiceTriggerService"/>
public sealed class VoiceTriggerService : IVoiceTriggerService
{
    private const int MaxCooldownSeconds = 3_600;

    private readonly IApplicationDbContext _db;
    private readonly IChannelAssetService _assets;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _clock;

    public VoiceTriggerService(
        IApplicationDbContext db,
        IChannelAssetService assets,
        IEventBus eventBus,
        TimeProvider clock
    )
    {
        _db = db;
        _assets = assets;
        _eventBus = eventBus;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<VoiceTriggerDto>>> ListAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<IReadOnlyList<VoiceTriggerDto>>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        List<VoiceTrigger> triggers = await _db
            .VoiceTriggers.Where(t => t.BroadcasterId == broadcaster)
            .OrderBy(t => t.Word)
            .ToListAsync(cancellationToken);

        List<VoiceTriggerDto> dtos = [];
        foreach (VoiceTrigger trigger in triggers)
            dtos.Add(await ToDtoAsync(trigger, cancellationToken));

        return Result.Success<IReadOnlyList<VoiceTriggerDto>>(dtos);
    }

    public async Task<Result<VoiceTriggerDto>> CreateAsync(
        string broadcasterId,
        CreateVoiceTriggerRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<VoiceTriggerDto>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        if (string.IsNullOrWhiteSpace(request.Word))
            return Result.Failure<VoiceTriggerDto>(
                "The word cannot be empty.",
                "VALIDATION_FAILED"
            );

        if (request.StickerAssetId is { } assetId)
        {
            Result<ChannelAssetDto> asset = await _assets.GetAsync(
                broadcaster,
                assetId,
                cancellationToken
            );
            if (asset.IsFailure)
                return Errors.NotFound<VoiceTriggerDto>("ChannelAsset", assetId.ToString());
        }

        VoiceTrigger trigger = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            Word = request.Word.Trim(),
            IsEnabled = request.IsEnabled,
            StartingCount = request.StartingCount,
            CurrentCount = request.StartingCount,
            CooldownSeconds = Math.Clamp(request.CooldownSeconds, 0, MaxCooldownSeconds),
            StickerAssetId = request.StickerAssetId,
        };
        _db.VoiceTriggers.Add(trigger);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(await ToDtoAsync(trigger, cancellationToken));
    }

    public async Task<Result<VoiceTriggerDto>> UpdateAsync(
        string broadcasterId,
        Guid triggerId,
        UpdateVoiceTriggerRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<VoiceTriggerDto>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        VoiceTrigger? trigger = await _db.VoiceTriggers.FirstOrDefaultAsync(
            t => t.Id == triggerId && t.BroadcasterId == broadcaster,
            cancellationToken
        );
        if (trigger is null)
            return Errors.NotFound<VoiceTriggerDto>("VoiceTrigger", triggerId.ToString());

        if (request.Word is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Word))
                return Result.Failure<VoiceTriggerDto>(
                    "The word cannot be empty.",
                    "VALIDATION_FAILED"
                );
            trigger.Word = request.Word.Trim();
        }

        if (request.IsEnabled.HasValue)
            trigger.IsEnabled = request.IsEnabled.Value;
        if (request.CooldownSeconds.HasValue)
            trigger.CooldownSeconds = Math.Clamp(
                request.CooldownSeconds.Value,
                0,
                MaxCooldownSeconds
            );

        // Absent (null) leaves the sticker unchanged; Guid.Empty clears it; a real id binds that asset —
        // same explicit-clear-sentinel convention ChatTriggerService/RewardService use for PipelineId.
        if (request.StickerAssetId.HasValue)
        {
            if (request.StickerAssetId.Value == Guid.Empty)
            {
                trigger.StickerAssetId = null;
            }
            else
            {
                Result<ChannelAssetDto> asset = await _assets.GetAsync(
                    broadcaster,
                    request.StickerAssetId.Value,
                    cancellationToken
                );
                if (asset.IsFailure)
                    return Errors.NotFound<VoiceTriggerDto>(
                        "ChannelAsset",
                        request.StickerAssetId.Value.ToString()
                    );
                trigger.StickerAssetId = request.StickerAssetId.Value;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(await ToDtoAsync(trigger, cancellationToken));
    }

    public async Task<Result> DeleteAsync(
        string broadcasterId,
        Guid triggerId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure($"Invalid channel ID '{broadcasterId}'.", "VALIDATION_FAILED");

        VoiceTrigger? trigger = await _db.VoiceTriggers.FirstOrDefaultAsync(
            t => t.Id == triggerId && t.BroadcasterId == broadcaster,
            cancellationToken
        );
        if (trigger is null)
            return Result.Failure($"VoiceTrigger '{triggerId}' was not found.", "NOT_FOUND");

        _db.VoiceTriggers.Remove(trigger);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<VoiceTriggerReportResultDto>> ReportAsync(
        Guid broadcasterId,
        string transcript,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return Result.Success(new VoiceTriggerReportResultDto(false, null, null));

        DateTimeOffset now = _clock.GetUtcNow();

        // The full-transcript ask (owner, 2026-09-14): the same continuous recognition loop that spots trigger
        // words hears everything, so every finalized chunk is kept against the channel's CURRENT live stream —
        // not only the ones that happen to match a trigger. No live stream → nothing to attribute it to, so the
        // segment is dropped rather than stored orphaned (truthful data, never a fabricated attribution).
        string? liveStreamId = await _db
            .Streams.Where(s => s.ChannelId == broadcasterId && s.EndedAt == null)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (liveStreamId is not null)
        {
            _db.VoiceTranscriptSegments.Add(
                new VoiceTranscriptSegment
                {
                    Id = Guid.CreateVersion7(),
                    BroadcasterId = broadcasterId,
                    StreamId = liveStreamId,
                    Text = transcript.Length > 1000 ? transcript[..1000] : transcript,
                    SpokenAt = now.UtcDateTime,
                }
            );
            await _db.SaveChangesAsync(cancellationToken);
        }

        List<VoiceTrigger> candidates = await _db
            .VoiceTriggers.Where(t => t.BroadcasterId == broadcasterId && t.IsEnabled)
            .ToListAsync(cancellationToken);

        VoiceTrigger? match = candidates.FirstOrDefault(t =>
            transcript.Contains(t.Word, StringComparison.OrdinalIgnoreCase)
        );
        if (match is null)
            return Result.Success(new VoiceTriggerReportResultDto(false, null, null));

        if (
            match.LastFiredAt is { } lastFired
            && now.UtcDateTime - lastFired < TimeSpan.FromSeconds(match.CooldownSeconds)
        )
            // Still cooling down — not an error, the listener keeps running unaware.
            return Result.Success(
                new VoiceTriggerReportResultDto(false, match.Word, match.CurrentCount)
            );

        match.CurrentCount += 1;
        match.LastFiredAt = now.UtcDateTime;
        await _db.SaveChangesAsync(cancellationToken);

        string? stickerUrl = null;
        if (match.StickerAssetId is { } assetId)
        {
            Result<ChannelAssetDto> asset = await _assets.GetAsync(
                broadcasterId,
                assetId,
                cancellationToken
            );
            if (asset.IsSuccess)
                stickerUrl = asset.Value.Url;
        }

        await _eventBus.PublishAsync(
            new VoiceTriggerFiredEvent
            {
                BroadcasterId = broadcasterId,
                OccurredAt = now,
                VoiceTriggerId = match.Id,
                Word = match.Word,
                NewCount = match.CurrentCount,
                StickerImageUrl = stickerUrl,
            },
            cancellationToken
        );

        return Result.Success(
            new VoiceTriggerReportResultDto(true, match.Word, match.CurrentCount)
        );
    }

    public async Task<Result<string>> GetOverlayTokenAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<string>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        string? token = await _db
            .Channels.Where(c => c.Id == broadcaster)
            .Select(c => c.OverlayToken)
            .FirstOrDefaultAsync(cancellationToken);

        return token is null
            ? Errors.NotFound<string>("Channel", broadcasterId)
            : Result.Success(token);
    }

    private async Task<VoiceTriggerDto> ToDtoAsync(
        VoiceTrigger t,
        CancellationToken cancellationToken
    )
    {
        string? stickerUrl = null;
        if (t.StickerAssetId is { } assetId)
        {
            Result<ChannelAssetDto> asset = await _assets.GetAsync(
                t.BroadcasterId,
                assetId,
                cancellationToken
            );
            if (asset.IsSuccess)
                stickerUrl = asset.Value.Url;
        }

        return new VoiceTriggerDto(
            t.Id,
            t.Word,
            t.IsEnabled,
            t.StartingCount,
            t.CurrentCount,
            t.CooldownSeconds,
            t.StickerAssetId,
            stickerUrl,
            t.LastFiredAt,
            t.CreatedAt,
            t.UpdatedAt
        );
    }
}
