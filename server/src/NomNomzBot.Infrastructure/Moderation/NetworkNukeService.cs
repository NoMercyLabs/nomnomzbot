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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using RecordEntity = NomNomzBot.Domain.Platform.Entities.Record;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// The SuperMod platform nuke (moderation.md §3.4, J.2a). The channel set is resolved from the TENANT DB
/// (every enabled+onboarded+active channel where the actor's effective level is SuperMod(20)+ — re-checked
/// in-process per channel), and each leg bans on THAT channel's own token — the platform power the operator
/// fan-out (<c>IOperatorNetworkBanService</c>) deliberately is not. Legs are best-effort: a failed channel
/// marks the batch <c>partial</c> and never blocks the rest. Every successful leg is one
/// <c>moderation_action</c> record carrying <c>Origin="network_nuke"</c> + <c>NetworkNukeBatchId</c> — the
/// exact set the one-shot revert walks.
/// </summary>
public sealed class NetworkNukeService(
    IApplicationDbContext db,
    IRoleResolver roles,
    ITwitchModerationApi twitchModeration,
    IEventBus eventBus,
    TimeProvider clock,
    ILogger<NetworkNukeService> logger
) : INetworkNukeService
{
    private const string ActionRecordType = "moderation_action";
    private static readonly int SuperModFloor = ManagementRole.LeadModerator.ToLevel();

    public async Task<Result<NetworkNukeBatchDto>> NukeAsync(
        Guid originBroadcasterId,
        Guid actorUserId,
        NetworkNukeRequest request,
        CancellationToken ct = default
    )
    {
        // The single-confirmation guardrail: the client must assert the user confirmed the blast radius.
        if (!request.RequireConfirmation)
            return Result.Failure<NetworkNukeBatchDto>(
                "A network nuke requires explicit confirmation.",
                "VALIDATION_FAILED"
            );
        if (string.IsNullOrWhiteSpace(request.TargetTwitchUserId))
            return Result.Failure<NetworkNukeBatchDto>(
                "A target user is required.",
                "VALIDATION_FAILED"
            );

        Result originFloor = await RequireSuperModAsync(actorUserId, originBroadcasterId, ct);
        if (originFloor.IsFailure)
            return originFloor.WithValue<NetworkNukeBatchDto>(null!);

        // The candidate set: every LIVE tenant channel — each admitted only where the actor holds
        // SuperMod+ there too (per-channel re-check; the origin is included by its own check above).
        List<Guid> candidates = await db
            .Channels.Where(c =>
                c.Enabled && c.IsOnboarded && c.Status == AuthEnums.ChannelStatus.Active
            )
            .Select(c => c.Id)
            .ToListAsync(ct);

        NetworkNukeBatch batch = new()
        {
            OriginBroadcasterId = originBroadcasterId,
            InitiatedByUserId = actorUserId,
            MatchTerm = request.MatchTerm,
            TargetTwitchUserId = request.TargetTwitchUserId,
            Status = NetworkNukeStatus.Active,
        };
        db.NetworkNukeBatches.Add(batch);
        await db.SaveChangesAsync(ct);

        int actioned = 0;
        bool anyFailed = false;
        foreach (Guid channelId in candidates)
        {
            if (channelId != originBroadcasterId)
            {
                Result channelFloor = await RequireSuperModAsync(actorUserId, channelId, ct);
                if (channelFloor.IsFailure)
                    continue; // not the actor's channel to nuke — silently out of scope, not a failure
            }

            Result<TwitchBanResult> banned = await twitchModeration.BanUserAsync(
                channelId,
                request.TargetTwitchUserId,
                request.Reason ?? "Network nuke.",
                ct
            );
            if (banned.IsFailure)
            {
                anyFailed = true;
                logger.LogWarning(
                    "Network nuke {BatchId}: leg failed in {ChannelId}: {Error}",
                    batch.Id,
                    channelId,
                    banned.ErrorMessage
                );
                continue;
            }

            db.Records.Add(
                NukeLegRecord(channelId, actorUserId, batch.Id, request, originBroadcasterId)
            );
            actioned++;
        }

        batch.ChannelCount = actioned;
        batch.Status = anyFailed ? NetworkNukeStatus.Partial : NetworkNukeStatus.Active;
        await db.SaveChangesAsync(ct);

        await eventBus.PublishAsync(
            new NetworkNukeExecutedEvent
            {
                BroadcasterId = originBroadcasterId,
                BatchId = batch.Id,
                OriginBroadcasterId = originBroadcasterId,
                InitiatedByUserId = actorUserId,
                TargetTwitchUserId = request.TargetTwitchUserId,
                ChannelCount = actioned,
            },
            ct
        );
        return Result.Success(ToDto(batch));
    }

    public async Task<Result<NetworkNukeBatchDto>> RevertAsync(
        Guid actorUserId,
        Guid batchId,
        CancellationToken ct = default
    )
    {
        NetworkNukeBatch? batch = await db.NetworkNukeBatches.FirstOrDefaultAsync(
            b => b.Id == batchId,
            ct
        );
        if (batch is null)
            return Result.Failure<NetworkNukeBatchDto>("Unknown nuke batch.", "NOT_FOUND");
        if (batch.Status == NetworkNukeStatus.Reverted)
            return Result.Failure<NetworkNukeBatchDto>(
                "This batch is already reverted.",
                "VALIDATION_FAILED"
            );

        Result floor = await RequireSuperModAsync(actorUserId, batch.OriginBroadcasterId, ct);
        if (floor.IsFailure)
            return floor.WithValue<NetworkNukeBatchDto>(null!);

        // The legs ARE the records carrying this batch id — cross-tenant by nature, so the tenant
        // filter is lifted and the soft-delete guard re-applied explicitly.
        string batchMarker = batch.Id.ToString();
        List<RecordEntity> legs = await db
            .Records.IgnoreQueryFilters()
            .Where(r =>
                r.RecordType == ActionRecordType
                && r.DeletedAt == null
                && r.Data.Contains(batchMarker)
            )
            .ToListAsync(ct);

        foreach (RecordEntity leg in legs)
        {
            Result unbanned = await twitchModeration.UnbanUserAsync(
                leg.BroadcasterId,
                batch.TargetTwitchUserId ?? "",
                ct
            );
            if (unbanned.IsFailure)
                logger.LogWarning(
                    "Network un-nuke {BatchId}: leg failed in {ChannelId}: {Error}",
                    batch.Id,
                    leg.BroadcasterId,
                    unbanned.ErrorMessage
                );
        }

        batch.Status = NetworkNukeStatus.Reverted;
        batch.RevertedByUserId = actorUserId;
        batch.RevertedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(batch));
    }

    public async Task<Result<PagedList<NetworkNukeBatchDto>>> ListBatchesAsync(
        Guid originBroadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<NetworkNukeBatch> query = db.NetworkNukeBatches.Where(b =>
            b.OriginBroadcasterId == originBroadcasterId
        );
        int total = await query.CountAsync(ct);
        List<NetworkNukeBatch> rows = await query
            .OrderByDescending(b => b.CreatedAt)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);
        return Result.Success(
            new PagedList<NetworkNukeBatchDto>(
                [.. rows.Select(ToDto)],
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    private async Task<Result> RequireSuperModAsync(
        Guid actorUserId,
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        Result<int> level = await roles.ResolveEffectiveLevelAsync(actorUserId, broadcasterId, ct);
        if (level.IsFailure)
            return level;
        return level.Value >= SuperModFloor
            ? Result.Success()
            : Result.Failure("A network nuke requires the SuperMod tier or above.", "FORBIDDEN");
    }

    private static RecordEntity NukeLegRecord(
        Guid channelId,
        Guid actorUserId,
        Guid batchId,
        NetworkNukeRequest request,
        Guid originBroadcasterId
    ) =>
        new()
        {
            BroadcasterId = channelId,
            RecordType = ActionRecordType,
            Data = JsonSerializer.Serialize(
                new NukeActionData
                {
                    Action = "nuke",
                    TargetUserId = request.TargetTwitchUserId,
                    Reason = request.Reason,
                    Origin = "network_nuke",
                    OriginChannelId = originBroadcasterId,
                    NetworkNukeBatchId = batchId,
                }
            ),
            UserId = actorUserId.ToString(),
        };

    /// <summary>The recorded leg shape — a superset of ModerationService's action data (same JSON reader).</summary>
    private sealed class NukeActionData
    {
        public string Action { get; set; } = null!;
        public string TargetUserId { get; set; } = null!;
        public string? Reason { get; set; }
        public string? Origin { get; set; }
        public Guid? OriginChannelId { get; set; }
        public Guid? NetworkNukeBatchId { get; set; }
    }

    private static NetworkNukeBatchDto ToDto(NetworkNukeBatch b) =>
        new(
            b.Id,
            b.OriginBroadcasterId,
            b.InitiatedByUserId,
            b.MatchTerm,
            b.TargetUserId,
            b.TargetTwitchUserId,
            b.ChannelCount,
            b.Status,
            b.RevertedByUserId,
            b.RevertedAt,
            b.CreatedAt
        );
}
