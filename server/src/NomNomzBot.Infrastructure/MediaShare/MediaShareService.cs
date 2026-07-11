// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.MediaShare.Dtos;
using NomNomzBot.Application.MediaShare.Services;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.MediaShare.Entities;
using NomNomzBot.Domain.MediaShare.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.MediaShare;

/// <summary>
/// <see cref="IMediaShareService"/> (media-share.md §3). The viewer clip/video queue: safe-by-default
/// approval, a closed source set, a hard duration cap, optional entry cost + eligibility, FIFO play order
/// with mod control. Distinct from music song-requests.
/// </summary>
public sealed class MediaShareService : IMediaShareService
{
    private readonly IApplicationDbContext _db;
    private readonly IMediaSourceResolver _resolver;
    private readonly ICurrencyAccountService _accounts;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _clock;

    public MediaShareService(
        IApplicationDbContext db,
        IMediaSourceResolver resolver,
        ICurrencyAccountService accounts,
        IEventBus eventBus,
        TimeProvider clock
    )
    {
        _db = db;
        _resolver = resolver;
        _accounts = accounts;
        _eventBus = eventBus;
        _clock = clock;
    }

    public async Task<Result<MediaShareRequestDto>> SubmitAsync(
        Guid broadcasterId,
        Guid requesterUserId,
        SubmitMediaRequest request,
        CancellationToken ct = default
    )
    {
        MediaShareConfig? config = await _db.MediaShareConfigs.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId,
            ct
        );
        if (config is null || !config.IsEnabled)
            return Result.Failure<MediaShareRequestDto>(
                "Media share is not enabled on this channel.",
                "DISABLED"
            );

        Result<ResolvedMedia> resolved = await _resolver.ResolveAsync(
            request.Url,
            config.AllowTwitchClips,
            config.AllowYouTube,
            ct
        );
        if (resolved.IsFailure)
            return Result.Failure<MediaShareRequestDto>(resolved.ErrorMessage!, resolved.ErrorCode);

        ResolvedMedia media = resolved.Value;
        if (media.DurationSeconds > config.MaxDurationSeconds)
            return Result.Failure<MediaShareRequestDto>(
                $"That clip is {media.DurationSeconds}s — the limit is {config.MaxDurationSeconds}s.",
                "DURATION_EXCEEDED"
            );

        Result eligibility = await CheckEligibilityAsync(
            broadcasterId,
            requesterUserId,
            config.EligibilityJson,
            ct
        );
        if (eligibility.IsFailure)
            return Result.Failure<MediaShareRequestDto>(
                eligibility.ErrorMessage!,
                eligibility.ErrorCode
            );

        DateTime now = _clock.GetUtcNow().UtcDateTime;
        if (config.PerUserCooldownSeconds > 0)
        {
            DateTime? lastAt = await _db
                .MediaShareRequests.AsNoTracking()
                .Where(r =>
                    r.BroadcasterId == broadcasterId && r.RequesterUserId == requesterUserId
                )
                .OrderByDescending(r => r.RequestedAt)
                .Select(r => (DateTime?)r.RequestedAt)
                .FirstOrDefaultAsync(ct);
            if (lastAt is { } last && (now - last).TotalSeconds < config.PerUserCooldownSeconds)
                return Result.Failure<MediaShareRequestDto>(
                    "You're submitting too fast — wait a moment before the next one.",
                    "COOLDOWN"
                );
        }

        int liveCount = await _db.MediaShareRequests.CountAsync(
            r =>
                r.BroadcasterId == broadcasterId
                && (
                    r.Status == MediaShareStatus.Pending
                    || r.Status == MediaShareStatus.Approved
                    || r.Status == MediaShareStatus.Playing
                ),
            ct
        );
        if (liveCount >= config.MaxQueueLength)
            return Result.Failure<MediaShareRequestDto>(
                "The media queue is full — try again after some items play.",
                "QUEUE_FULL"
            );

        bool autoApproved = !config.RequireApproval;
        Guid requestId = Guid.CreateVersion7();

        // Debit the entry cost up front against the pre-allocated request id, so a failed debit needs no
        // compensation (nothing is persisted yet). The refund on reject/skip references the same id.
        long? costLedgerEntryId = null;
        if (config.EntryCost is > 0)
        {
            Result<CurrencyLedgerEntryDto> debit = await _accounts.PostLedgerEntryAsync(
                broadcasterId,
                new PostLedgerEntryCommand(
                    requesterUserId,
                    -config.EntryCost.Value,
                    nameof(CurrencyEntryType.SpendMedia),
                    nameof(CurrencyLedgerSourceType.MediaShare),
                    SourceId: requestId,
                    EventId: null,
                    Reason: "Media share entry",
                    ActorUserId: null,
                    IdempotencyKey: $"media-entry:{requestId}"
                ),
                ct
            );
            if (debit.IsFailure)
                return Result.Failure<MediaShareRequestDto>(debit.ErrorMessage!, debit.ErrorCode);
            costLedgerEntryId = debit.Value.Id;
        }

        MediaShareRequest entity = new()
        {
            Id = requestId,
            BroadcasterId = broadcasterId,
            RequesterUserId = requesterUserId,
            RequesterTwitchUserId = HashViewer(requesterUserId),
            SourceType = media.SourceType,
            SourceUrl = request.Url.Trim(),
            MediaRef = media.MediaRef,
            Title = media.Title,
            DurationSeconds = media.DurationSeconds,
            ThumbnailUrl = media.ThumbnailUrl,
            Status = autoApproved ? MediaShareStatus.Approved : MediaShareStatus.Pending,
            QueuePosition = autoApproved ? await NextQueuePositionAsync(broadcasterId, ct) : null,
            EntryCostLedgerEntryId = costLedgerEntryId,
            RequestedAt = now,
            DecidedAt = autoApproved ? now : null,
        };
        await _db.MediaShareRequests.AddAsync(entity, ct);
        await _db.SaveChangesAsync(ct);

        await _eventBus.PublishAsync(
            new MediaShareSubmittedEvent
            {
                BroadcasterId = broadcasterId,
                RequestId = entity.Id,
                RequesterUserId = requesterUserId,
                SourceType = entity.SourceType,
                AutoApproved = autoApproved,
                OccurredAt = now,
            },
            ct
        );
        if (autoApproved)
            await PublishPlaybackAsync(
                broadcasterId,
                entity.Id,
                MediaShareStatus.Approved,
                now,
                ct
            );

        return Result.Success(ToDto(entity));
    }

    public async Task<Result<MediaShareRequestDto>> ApproveAsync(
        Guid broadcasterId,
        Guid requestId,
        Guid moderatorUserId,
        CancellationToken ct = default
    )
    {
        MediaShareRequest? entity = await FindAsync(broadcasterId, requestId, ct);
        if (entity is null)
            return Result.Failure<MediaShareRequestDto>("Media request not found.", "NOT_FOUND");
        if (entity.Status != MediaShareStatus.Pending)
            return Result.Failure<MediaShareRequestDto>(
                "Only a pending request can be approved.",
                "VALIDATION_FAILED"
            );

        DateTime now = _clock.GetUtcNow().UtcDateTime;
        entity.Status = MediaShareStatus.Approved;
        entity.QueuePosition = await NextQueuePositionAsync(broadcasterId, ct);
        entity.DecidedAt = now;
        entity.DecidedByUserId = moderatorUserId;
        await _db.SaveChangesAsync(ct);

        await PublishPlaybackAsync(broadcasterId, entity.Id, MediaShareStatus.Approved, now, ct);
        return Result.Success(ToDto(entity));
    }

    public async Task<Result> RejectAsync(
        Guid broadcasterId,
        Guid requestId,
        Guid moderatorUserId,
        CancellationToken ct = default
    )
    {
        MediaShareRequest? entity = await FindAsync(broadcasterId, requestId, ct);
        if (entity is null)
            return Result.Failure("Media request not found.", "NOT_FOUND");
        if (
            entity.Status
            is MediaShareStatus.Rejected
                or MediaShareStatus.Played
                or MediaShareStatus.Skipped
        )
            return Result.Failure("That request is already resolved.", "VALIDATION_FAILED");

        DateTime now = _clock.GetUtcNow().UtcDateTime;
        await RefundIfChargedAsync(broadcasterId, entity, ct);
        entity.Status = MediaShareStatus.Rejected;
        entity.QueuePosition = null;
        entity.DecidedAt = now;
        entity.DecidedByUserId = moderatorUserId;
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SkipAsync(
        Guid broadcasterId,
        Guid requestId,
        CancellationToken ct = default
    )
    {
        MediaShareRequest? entity = await FindAsync(broadcasterId, requestId, ct);
        if (entity is null)
            return Result.Failure("Media request not found.", "NOT_FOUND");
        if (entity.Status is not (MediaShareStatus.Approved or MediaShareStatus.Playing))
            return Result.Failure(
                "Only an approved or playing item can be skipped.",
                "VALIDATION_FAILED"
            );

        DateTime now = _clock.GetUtcNow().UtcDateTime;
        await RefundIfChargedAsync(broadcasterId, entity, ct);
        entity.Status = MediaShareStatus.Skipped;
        entity.QueuePosition = null;
        await _db.SaveChangesAsync(ct);

        await PublishPlaybackAsync(broadcasterId, entity.Id, MediaShareStatus.Skipped, now, ct);
        return Result.Success();
    }

    public async Task<Result> ReorderAsync(
        Guid broadcasterId,
        Guid requestId,
        int newPosition,
        CancellationToken ct = default
    )
    {
        if (newPosition < 1)
            return Result.Failure("Position must be 1 or greater.", "VALIDATION_FAILED");

        List<MediaShareRequest> approved = await _db
            .MediaShareRequests.Where(r =>
                r.BroadcasterId == broadcasterId && r.Status == MediaShareStatus.Approved
            )
            .OrderBy(r => r.QueuePosition)
            .ThenBy(r => r.RequestedAt)
            .ToListAsync(ct);

        MediaShareRequest? target = approved.FirstOrDefault(r => r.Id == requestId);
        if (target is null)
            return Result.Failure("That approved item is not in the queue.", "NOT_FOUND");

        approved.Remove(target);
        int insertAt = Math.Min(newPosition - 1, approved.Count);
        approved.Insert(insertAt, target);

        // Renumber contiguously from 1 so the play order is stable and gap-free.
        for (int i = 0; i < approved.Count; i++)
            approved[i].QueuePosition = i + 1;
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<PagedList<MediaShareRequestDto>>> GetQueueAsync(
        Guid broadcasterId,
        MediaShareFilter filter,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<MediaShareRequest> query = _db
            .MediaShareRequests.AsNoTracking()
            .Where(r => r.BroadcasterId == broadcasterId);
        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(r => r.Status == filter.Status);

        query = query.OrderBy(r => r.QueuePosition ?? int.MaxValue).ThenBy(r => r.RequestedAt);

        int total = await query.CountAsync(ct);
        List<MediaShareRequest> rows = await query
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        return Result.Success(
            new PagedList<MediaShareRequestDto>(
                rows.Select(ToDto).ToList(),
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    public async Task<Result<MediaShareRequestDto?>> GetNextAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        // If something is already playing, that IS the current item — don't advance past it.
        MediaShareRequest? playing = await _db.MediaShareRequests.FirstOrDefaultAsync(
            r => r.BroadcasterId == broadcasterId && r.Status == MediaShareStatus.Playing,
            ct
        );
        if (playing is not null)
            return Result.Success<MediaShareRequestDto?>(ToDto(playing));

        MediaShareRequest? next = await _db
            .MediaShareRequests.Where(r =>
                r.BroadcasterId == broadcasterId && r.Status == MediaShareStatus.Approved
            )
            .OrderBy(r => r.QueuePosition ?? int.MaxValue)
            .ThenBy(r => r.RequestedAt)
            .FirstOrDefaultAsync(ct);
        if (next is null)
            return Result.Success<MediaShareRequestDto?>(null);

        DateTime now = _clock.GetUtcNow().UtcDateTime;
        next.Status = MediaShareStatus.Playing;
        await _db.SaveChangesAsync(ct);

        await PublishPlaybackAsync(broadcasterId, next.Id, MediaShareStatus.Playing, now, ct);
        return Result.Success<MediaShareRequestDto?>(ToDto(next));
    }

    public async Task<Result> MarkPlayedAsync(
        Guid broadcasterId,
        Guid requestId,
        CancellationToken ct = default
    )
    {
        MediaShareRequest? entity = await FindAsync(broadcasterId, requestId, ct);
        if (entity is null)
            return Result.Failure("Media request not found.", "NOT_FOUND");
        if (entity.Status is not (MediaShareStatus.Playing or MediaShareStatus.Approved))
            return Result.Failure("Only a playing item can be marked played.", "VALIDATION_FAILED");

        DateTime now = _clock.GetUtcNow().UtcDateTime;
        entity.Status = MediaShareStatus.Played;
        entity.QueuePosition = null;
        await _db.SaveChangesAsync(ct);

        await PublishPlaybackAsync(broadcasterId, entity.Id, MediaShareStatus.Played, now, ct);
        return Result.Success();
    }

    public async Task<Result<MediaShareConfigDto>> GetConfigAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        MediaShareConfig? config = await _db
            .MediaShareConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.BroadcasterId == broadcasterId, ct);
        return Result.Success(config is null ? Defaults() : ToConfigDto(config));
    }

    public async Task<Result<MediaShareConfigDto>> UpdateConfigAsync(
        Guid broadcasterId,
        UpdateMediaShareConfigRequest request,
        CancellationToken ct = default
    )
    {
        if (request.MaxDurationSeconds < 1)
            return Result.Failure<MediaShareConfigDto>(
                "MaxDurationSeconds must be at least 1.",
                "VALIDATION_FAILED"
            );
        if (request.MaxQueueLength < 1)
            return Result.Failure<MediaShareConfigDto>(
                "MaxQueueLength must be at least 1.",
                "VALIDATION_FAILED"
            );
        if (request.PerUserCooldownSeconds < 0)
            return Result.Failure<MediaShareConfigDto>(
                "PerUserCooldownSeconds cannot be negative.",
                "VALIDATION_FAILED"
            );
        if (request.EntryCost is < 0)
            return Result.Failure<MediaShareConfigDto>(
                "EntryCost cannot be negative.",
                "VALIDATION_FAILED"
            );

        MediaShareConfig? config = await _db.MediaShareConfigs.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId,
            ct
        );
        if (config is null)
        {
            config = new MediaShareConfig { BroadcasterId = broadcasterId };
            await _db.MediaShareConfigs.AddAsync(config, ct);
        }

        config.IsEnabled = request.IsEnabled;
        config.RequireApproval = request.RequireApproval;
        config.AllowTwitchClips = request.AllowTwitchClips;
        config.AllowYouTube = request.AllowYouTube;
        config.MaxDurationSeconds = request.MaxDurationSeconds;
        config.EntryCost = request.EntryCost is > 0 ? request.EntryCost : null;
        config.MaxQueueLength = request.MaxQueueLength;
        config.PerUserCooldownSeconds = request.PerUserCooldownSeconds;
        await _db.SaveChangesAsync(ct);

        return Result.Success(ToConfigDto(config));
    }

    // ─── Internals ────────────────────────────────────────────────────────────

    private Task<MediaShareRequest?> FindAsync(
        Guid broadcasterId,
        Guid requestId,
        CancellationToken ct
    ) =>
        _db.MediaShareRequests.FirstOrDefaultAsync(
            r => r.Id == requestId && r.BroadcasterId == broadcasterId,
            ct
        );

    private async Task<int> NextQueuePositionAsync(Guid broadcasterId, CancellationToken ct)
    {
        int? max = await _db
            .MediaShareRequests.Where(r =>
                r.BroadcasterId == broadcasterId && r.Status == MediaShareStatus.Approved
            )
            .MaxAsync(r => (int?)r.QueuePosition, ct);
        return (max ?? 0) + 1;
    }

    /// <summary>Refunds the entry cost once (clears the ledger reference so a double reject/skip can't).</summary>
    private async Task RefundIfChargedAsync(
        Guid broadcasterId,
        MediaShareRequest entity,
        CancellationToken ct
    )
    {
        if (entity.EntryCostLedgerEntryId is null)
            return;

        Result<CurrencyLedgerEntryDto> refund = await _accounts.PostLedgerEntryAsync(
            broadcasterId,
            new PostLedgerEntryCommand(
                entity.RequesterUserId,
                Math.Abs(await OriginalCostAsync(broadcasterId, ct)),
                nameof(CurrencyEntryType.RefundMedia),
                nameof(CurrencyLedgerSourceType.MediaShare),
                SourceId: entity.Id,
                EventId: null,
                Reason: "Media share refund",
                ActorUserId: null,
                IdempotencyKey: $"media-refund:{entity.Id}"
            ),
            ct
        );
        // A refund failure must not block the moderation action; the reference is cleared regardless so it
        // is attempted at most once.
        entity.EntryCostLedgerEntryId = null;
        _ = refund;
    }

    private async Task<long> OriginalCostAsync(Guid broadcasterId, CancellationToken ct)
    {
        long? cost = await _db
            .MediaShareConfigs.AsNoTracking()
            .Where(c => c.BroadcasterId == broadcasterId)
            .Select(c => c.EntryCost)
            .FirstOrDefaultAsync(ct);
        return cost ?? 0;
    }

    /// <summary>
    /// Evaluates the (optional) eligibility gate. The config's REST surface does not set
    /// <c>EligibilityJson</c>, so this is dormant unless seeded; when set, <c>subOnly</c> is enforced
    /// against the viewer's community standing (the one truthfully-verifiable rule today).
    /// </summary>
    private async Task<Result> CheckEligibilityAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        string? eligibilityJson,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(eligibilityJson))
            return Result.Success();

        bool subOnly;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(eligibilityJson);
            subOnly =
                doc.RootElement.TryGetProperty("subOnly", out JsonElement s)
                && s.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return Result.Success(); // malformed rules never block a viewer
        }

        if (!subOnly)
            return Result.Success();

        bool isSub = await _db
            .ChannelCommunityStandings.AsNoTracking()
            .AnyAsync(
                s =>
                    s.BroadcasterId == broadcasterId
                    && s.UserId == viewerUserId
                    && (s.SubTier != null || s.Standing == CommunityStanding.Subscriber),
                ct
            );
        return isSub
            ? Result.Success()
            : Result.Failure("Media submissions are subscriber-only right now.", "NOT_ELIGIBLE");
    }

    private Task PublishPlaybackAsync(
        Guid broadcasterId,
        Guid requestId,
        string status,
        DateTime at,
        CancellationToken ct
    ) =>
        _eventBus.PublishAsync(
            new MediaSharePlaybackChangedEvent
            {
                BroadcasterId = broadcasterId,
                RequestId = requestId,
                Status = status,
                OccurredAt = at,
            },
            ct
        );

    private static string HashViewer(Guid viewerUserId) =>
        Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(viewerUserId.ToByteArray())
        )[..32];

    private static MediaShareRequestDto ToDto(MediaShareRequest r) =>
        new(
            r.Id,
            r.RequesterUserId,
            r.SourceType,
            r.SourceUrl,
            r.MediaRef,
            r.Title,
            r.DurationSeconds,
            r.ThumbnailUrl,
            r.Status,
            r.QueuePosition,
            r.RequestedAt
        );

    private static MediaShareConfigDto ToConfigDto(MediaShareConfig c) =>
        new(
            c.IsEnabled,
            c.RequireApproval,
            c.AllowTwitchClips,
            c.AllowYouTube,
            c.MaxDurationSeconds,
            c.EntryCost,
            c.MaxQueueLength,
            c.PerUserCooldownSeconds
        );

    private static MediaShareConfigDto Defaults() =>
        new(false, true, true, true, 180, null, 20, 60);
}
