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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Domain.Rewards.Entities;

namespace NomNomzBot.Infrastructure.Rewards.Pipeline;

/// <summary>
/// Supplies the channel's channel-point rewards for the <c>twitch_reward</c> picker kind. The option value is the
/// reward's TWITCH id, the id a widget's <c>reward.redeemed</c> event carries, where <see cref="RewardOptionProvider"/>
/// stores the bot's own id. A reward not yet synced to Twitch has no such id and is not offered.
/// </summary>
internal sealed class TwitchRewardOptionProvider : IPipelineOptionProvider
{
    private readonly IApplicationDbContext _db;

    public TwitchRewardOptionProvider(IApplicationDbContext db)
    {
        _db = db;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.TwitchReward;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        IQueryable<Reward> query = _db.Rewards.Where(r =>
            r.BroadcasterId == broadcasterId && r.TwitchRewardId != null
        );
        if (request.IsValueLookup)
        {
            string twitchId = request.Value!;
            query = query.Where(r => r.TwitchRewardId == twitchId);
        }
        else if (!string.IsNullOrWhiteSpace(request.Search))
        {
            string term = request.Search.ToLowerInvariant();
            query = query.Where(r => r.Title.ToLower().Contains(term));
        }

        int total = await query.CountAsync(ct);
        List<Reward> page = await query
            .OrderBy(r => r.Title)
            .Skip((request.Pagination.Page - 1) * request.Pagination.PageSize)
            .Take(request.Pagination.PageSize)
            .ToListAsync(ct);

        List<PipelineOption> options = [.. page.Select(ToOption)];
        return Result.Success(PipelineOptionListResult.Of(options, total));
    }

    private static PipelineOption ToOption(Reward reward)
    {
        string cost = reward.Cost is { } c ? $"{c} points" : "cost unknown";
        string secondaryText = $"{cost} · {(reward.IsPaused ? "paused" : "active")}";

        return new PipelineOption(
            reward.TwitchRewardId!,
            reward.Title,
            secondaryText,
            ImageUrl: null,
            reward.IsEnabled ? PipelineOptionState.Selectable : PipelineOptionState.Unavailable,
            reward.IsEnabled ? null : "Reward is disabled."
        );
    }
}
