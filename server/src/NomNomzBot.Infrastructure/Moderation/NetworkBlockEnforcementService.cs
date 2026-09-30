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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Moderation.Entities;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// Adds the ban legs a network block owes in tenants the apply fan-out never saw (see
/// <see cref="INetworkBlockEnforcementService"/>). A block is enforced while it is not lifted AND no lift has
/// been attempted: once an operator asked to lift it, new bans would work against that decision, even while a
/// partial lift keeps the Gate-2 deny in force.
/// </summary>
public sealed class NetworkBlockEnforcementService(
    IApplicationDbContext db,
    ITwitchModerationApi twitchModeration,
    ILogger<NetworkBlockEnforcementService> logger
) : INetworkBlockEnforcementService
{
    public async Task<Result<int>> EnforceForChatterAsync(
        Guid broadcasterId,
        string twitchUserId,
        CancellationToken ct = default
    )
    {
        NetworkBlock? block = await EnforcedBlocks()
            .FirstOrDefaultAsync(b => b.TargetTwitchUserId == twitchUserId, ct);
        if (block is null)
            return Result.Success(0);

        return Result.Success(await AddLegsAsync(broadcasterId, [block], ct));
    }

    public async Task<Result<int>> EnforceForOnboardedChannelAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        DateTime? channelCreatedAt = await db
            .Channels.IgnoreQueryFilters()
            .Where(c => c.Id == broadcasterId)
            .Select(c => (DateTime?)c.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (channelCreatedAt is null)
            return Result.Failure<int>("Unknown channel.", "NOT_FOUND");

        List<NetworkBlock> blocks = await EnforcedBlocks()
            .Where(b => b.AppliedAt <= channelCreatedAt.Value)
            .ToListAsync(ct);

        return Result.Success(await AddLegsAsync(broadcasterId, blocks, ct));
    }

    private IQueryable<NetworkBlock> EnforcedBlocks() =>
        db.NetworkBlocks.Where(b =>
            b.Status != NetworkBlockStatus.Lifted && b.LiftAttemptedAt == null
        );

    private async Task<int> AddLegsAsync(
        Guid broadcasterId,
        List<NetworkBlock> blocks,
        CancellationToken ct
    )
    {
        int added = 0;
        foreach (NetworkBlock block in blocks)
        {
            if (await HasLegAsync(broadcasterId, block, ct))
                continue;

            Result<TwitchBanResult> banned = await twitchModeration.BanUserAsync(
                broadcasterId,
                block.TargetTwitchUserId,
                NetworkBlockLeg.BanReason(block),
                ct
            );
            if (banned.IsFailure)
            {
                // Recorded as partial, never as a clean block: this tenant still lets the actor chat.
                block.Status = NetworkBlockStatus.Partial;
                logger.LogWarning(
                    "Network block {BlockId}: late leg failed in {ChannelId}: {Error}",
                    block.Id,
                    broadcasterId,
                    banned.ErrorMessage
                );
                continue;
            }

            db.Records.Add(
                NetworkBlockLeg.Create(broadcasterId, block.AppliedByPrincipalId, block)
            );
            block.ChannelCount++;
            added++;
        }

        await db.SaveChangesAsync(ct);
        return added;
    }

    private Task<bool> HasLegAsync(Guid broadcasterId, NetworkBlock block, CancellationToken ct)
    {
        string blockMarker = block.Id.ToString();
        return db
            .Records.IgnoreQueryFilters()
            .AnyAsync(
                r =>
                    r.BroadcasterId == broadcasterId
                    && r.RecordType == NetworkBlockLeg.RecordType
                    && r.DeletedAt == null
                    && r.Data.Contains(blockMarker),
                ct
            );
    }
}
