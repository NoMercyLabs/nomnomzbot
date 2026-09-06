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
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Moderation.Entities;
using RecordEntity = NomNomzBot.Domain.Platform.Entities.Record;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// The network-wide block desk (S-ADMIN-8b) — the most dangerous control in the product, because it acts
/// across EVERY tenant of this deployment at once. Every entry point gates and audits FIRST through
/// <see cref="IPlatformIamService.AuthorizePlatformAsync"/> — the same funnel
/// <see cref="TrustSafetyReviewService"/> uses — and requires a justification. The fan-out itself reuses
/// <see cref="NetworkNukeService"/>'s best-effort, own-token-per-channel shape rather than a second one:
/// each leg is a <c>moderation_action</c> Record carrying <c>Origin="network_block"</c> +
/// <c>NetworkBlockId</c>, so a lift can find and reverse exactly the legs the apply actually touched.
/// </summary>
public sealed class NetworkBlockService(
    IApplicationDbContext db,
    IPlatformIamService iam,
    ITwitchModerationApi twitchModeration,
    TimeProvider clock,
    ILogger<NetworkBlockService> logger
) : INetworkBlockService
{
    private const string ActionRecordType = "moderation_action";

    public async Task<Result<NetworkBlockPreviewDto>> PreviewAsync(
        Guid actingPrincipalId,
        string targetTwitchUserId,
        string justification,
        CancellationToken ct = default
    )
    {
        Result gate = await RequireAsync(
            actingPrincipalId,
            justification,
            $"preview:{targetTwitchUserId}",
            ct
        );
        if (gate.IsFailure)
            return gate.WithValue<NetworkBlockPreviewDto>(null!);

        Result<(User Target, List<NetworkBlockAffectedTenantDto> Tenants)> loaded =
            await LoadTargetAndTenantsAsync(targetTwitchUserId, ct);
        if (loaded.IsFailure)
            return loaded.WithValue<NetworkBlockPreviewDto>(null!);

        (User target, List<NetworkBlockAffectedTenantDto> tenants) = loaded.Value;
        return Result.Success(
            new NetworkBlockPreviewDto(
                target.Id,
                target.TwitchUserId ?? targetTwitchUserId,
                target.DisplayName,
                tenants.Count,
                tenants
            )
        );
    }

    public async Task<Result<NetworkBlockDto>> ApplyAsync(
        Guid actingPrincipalId,
        ApplyNetworkBlockRequest request,
        CancellationToken ct = default
    )
    {
        Result gate = await RequireAsync(
            actingPrincipalId,
            request.Justification,
            $"apply:{request.TargetTwitchUserId}",
            ct
        );
        if (gate.IsFailure)
            return gate.WithValue<NetworkBlockDto>(null!);

        Result<(User Target, List<NetworkBlockAffectedTenantDto> Tenants)> loaded =
            await LoadTargetAndTenantsAsync(request.TargetTwitchUserId, ct);
        if (loaded.IsFailure)
            return loaded.WithValue<NetworkBlockDto>(null!);

        (User target, List<NetworkBlockAffectedTenantDto> tenants) = loaded.Value;

        // The fail-closed guardrail: the operator must be acting on the SAME blast radius they were just
        // shown. If it moved (a new tenant appeared, one disappeared), reject rather than act on stale
        // numbers — the same rule platform-content publish and billing-tier changes use.
        if (tenants.Count != request.ConfirmedTenantCount)
            return Result.Failure<NetworkBlockDto>(
                "The affected-tenant count changed since the last preview. Run preview again.",
                "PREVIEW_STALE"
            );

        NetworkBlock block = new()
        {
            TargetUserId = target.Id,
            TargetTwitchUserId = target.TwitchUserId ?? request.TargetTwitchUserId,
            TargetDisplayName = target.DisplayName,
            Reason = request.Reason,
            Justification = request.Justification,
            AppliedByPrincipalId = actingPrincipalId,
            AppliedAt = clock.GetUtcNow().UtcDateTime,
            TenantCount = tenants.Count,
        };
        db.NetworkBlocks.Add(block);
        await db.SaveChangesAsync(ct);

        int actioned = 0;
        bool anyFailed = false;
        foreach (NetworkBlockAffectedTenantDto tenant in tenants)
        {
            Result<TwitchBanResult> banned = await twitchModeration.BanUserAsync(
                tenant.BroadcasterId,
                block.TargetTwitchUserId,
                request.Reason ?? "Network-wide block.",
                ct
            );
            if (banned.IsFailure)
            {
                anyFailed = true;
                logger.LogWarning(
                    "Network block {BlockId}: leg failed in {ChannelId}: {Error}",
                    block.Id,
                    tenant.BroadcasterId,
                    banned.ErrorMessage
                );
                continue;
            }

            db.Records.Add(
                BlockLegRecord(tenant.BroadcasterId, actingPrincipalId, block.Id, request)
            );
            actioned++;
        }

        block.ChannelCount = actioned;
        block.Status = anyFailed ? NetworkBlockStatus.Partial : NetworkBlockStatus.Active;
        await db.SaveChangesAsync(ct);

        return Result.Success(ToDto(block));
    }

    public async Task<Result<NetworkBlockDto>> LiftAsync(
        Guid actingPrincipalId,
        Guid blockId,
        string justification,
        CancellationToken ct = default
    )
    {
        Result gate = await RequireAsync(actingPrincipalId, justification, $"lift:{blockId}", ct);
        if (gate.IsFailure)
            return gate.WithValue<NetworkBlockDto>(null!);

        NetworkBlock? block = await db.NetworkBlocks.FirstOrDefaultAsync(b => b.Id == blockId, ct);
        if (block is null)
            return Result.Failure<NetworkBlockDto>("Unknown network block.", "NOT_FOUND");
        if (block.Status == NetworkBlockStatus.Lifted)
            return Result.Failure<NetworkBlockDto>(
                "This network block is already lifted.",
                "VALIDATION_FAILED"
            );

        // The legs ARE the records carrying this block id — cross-tenant by nature, so the tenant filter
        // is lifted and the soft-delete guard re-applied explicitly.
        string blockMarker = block.Id.ToString();
        List<RecordEntity> legs = await db
            .Records.IgnoreQueryFilters()
            .Where(r =>
                r.RecordType == ActionRecordType
                && r.DeletedAt == null
                && r.Data.Contains(blockMarker)
            )
            .ToListAsync(ct);

        // The reversal law (as established for the spam-campaign restore): the REAL undo is attempted
        // BEFORE anything is stamped, and the row is only marked fully lifted when every leg actually
        // restores — a partial outcome is recorded honestly, never silently claimed clean.
        List<Guid> restored = [];
        List<Guid> failed = [];
        foreach (RecordEntity leg in legs)
        {
            Result unbanned = await twitchModeration.UnbanUserAsync(
                leg.BroadcasterId,
                block.TargetTwitchUserId,
                ct
            );
            if (unbanned.IsSuccess)
            {
                restored.Add(leg.BroadcasterId);
            }
            else
            {
                failed.Add(leg.BroadcasterId);
                logger.LogWarning(
                    "Network un-block {BlockId}: leg failed in {ChannelId}: {Error}",
                    block.Id,
                    leg.BroadcasterId,
                    unbanned.ErrorMessage
                );
            }
        }

        block.LiftedByPrincipalId = actingPrincipalId;
        block.LiftJustification = justification;
        block.LiftAttemptedAt = clock.GetUtcNow().UtcDateTime;
        block.RestoredChannelCount = restored.Count;
        block.LiftFailedChannelIds = string.Join(',', failed);

        // Stamped ONLY when every leg actually restored — a partial lift stays enforced, because this is
        // the network-wide deny gate: it must never say "cleared" while some tenant still shows the
        // actor as actioned and the platform cannot back that claim up.
        if (failed.Count == 0)
        {
            block.LiftedAt = block.LiftAttemptedAt;
            block.Status = NetworkBlockStatus.Lifted;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(block));
    }

    public async Task<Result<IReadOnlyList<NetworkBlockDto>>> ListAsync(
        Guid actingPrincipalId,
        string justification,
        CancellationToken ct = default
    )
    {
        Result gate = await RequireAsync(actingPrincipalId, justification, "list", ct);
        if (gate.IsFailure)
            return gate.WithValue<IReadOnlyList<NetworkBlockDto>>(null!);

        List<NetworkBlock> rows = await db
            .NetworkBlocks.OrderByDescending(b => b.AppliedAt)
            .ToListAsync(ct);
        return Result.Success<IReadOnlyList<NetworkBlockDto>>([.. rows.Select(ToDto)]);
    }

    /// <summary>
    /// The real, freshly-computed blast radius: every tenant where the target has a recorded community
    /// standing, or that they own — the exact presence computation the cross-tenant support desk already
    /// uses, never a heuristic guess.
    /// </summary>
    private async Task<
        Result<(User Target, List<NetworkBlockAffectedTenantDto> Tenants)>
    > LoadTargetAndTenantsAsync(string targetTwitchUserId, CancellationToken ct)
    {
        User? target = await db
            .Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.TwitchUserId == targetTwitchUserId, ct);
        if (target is null)
            return Result.Failure<(User, List<NetworkBlockAffectedTenantDto>)>(
                "Unknown target user.",
                "NOT_FOUND"
            );

        HashSet<Guid> broadcasterIds = [];
        List<Guid> standingBroadcasterIds = await db
            .ChannelCommunityStandings.IgnoreQueryFilters()
            .Where(s => s.UserId == target.Id)
            .Select(s => s.BroadcasterId)
            .Distinct()
            .ToListAsync(ct);
        foreach (Guid id in standingBroadcasterIds)
            broadcasterIds.Add(id);
        List<Guid> ownedBroadcasterIds = await db
            .Channels.Where(c => c.OwnerUserId == target.Id)
            .Select(c => c.Id)
            .ToListAsync(ct);
        foreach (Guid id in ownedBroadcasterIds)
            broadcasterIds.Add(id);

        Dictionary<Guid, string> channelNames = await db
            .Channels.IgnoreQueryFilters()
            .Where(c => broadcasterIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        List<NetworkBlockAffectedTenantDto> tenants = broadcasterIds
            .Select(id => new NetworkBlockAffectedTenantDto(
                id,
                channelNames.GetValueOrDefault(id, "unknown channel")
            ))
            .OrderBy(t => t.ChannelName)
            .ToList();

        return Result.Success((target, tenants));
    }

    private static RecordEntity BlockLegRecord(
        Guid channelId,
        Guid actorPrincipalId,
        Guid blockId,
        ApplyNetworkBlockRequest request
    ) =>
        new()
        {
            BroadcasterId = channelId,
            RecordType = ActionRecordType,
            Data = JsonSerializer.Serialize(
                new BlockActionData
                {
                    Action = "block",
                    TargetUserId = request.TargetTwitchUserId,
                    Reason = request.Reason,
                    Origin = "network_block",
                    NetworkBlockId = blockId,
                }
            ),
            UserId = actorPrincipalId.ToString(),
        };

    /// <summary>The recorded leg shape — a superset of ModerationService's action data (same JSON reader).</summary>
    private sealed class BlockActionData
    {
        public string Action { get; set; } = null!;
        public string TargetUserId { get; set; } = null!;
        public string? Reason { get; set; }
        public string? Origin { get; set; }
        public Guid? NetworkBlockId { get; set; }
    }

    private async Task<Result> RequireAsync(
        Guid actingPrincipalId,
        string justification,
        string targetResource,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(justification))
            return Result.Failure(
                "A justification is required for a network-wide block action.",
                "VALIDATION_FAILED"
            );

        Result<bool> allowed = await iam.AuthorizePlatformAsync(
            actingPrincipalId,
            IamPermissionKeys.NetworkBlockManage,
            null,
            false,
            justification,
            ct,
            targetResource
        );
        if (allowed.IsFailure)
            return allowed;
        return allowed.Value
            ? Result.Success()
            : Result.Failure($"Requires {IamPermissionKeys.NetworkBlockManage}.", "FORBIDDEN");
    }

    private static NetworkBlockDto ToDto(NetworkBlock b) =>
        new(
            b.Id,
            b.TargetUserId,
            b.TargetTwitchUserId,
            b.TargetDisplayName,
            b.Reason,
            b.Justification,
            b.AppliedByPrincipalId,
            b.AppliedAt,
            b.TenantCount,
            b.ChannelCount,
            b.Status,
            b.LiftedByPrincipalId,
            b.LiftJustification,
            b.LiftAttemptedAt,
            b.LiftedAt,
            b.RestoredChannelCount,
            string.IsNullOrEmpty(b.LiftFailedChannelIds)
                ? []
                : b.LiftFailedChannelIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
        );
}
