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
/// Supplies the channel's channel-point rewards for the <c>reward</c> resource-picker kind (S-RICH-PICKERS).
/// <see cref="PipelineOption.SecondaryText"/> is the reward's cost and paused/active state — what an operator
/// actually needs to tell two rewards apart.
/// </summary>
internal sealed class RewardOptionProvider : IPipelineOptionProvider
{
    private readonly IApplicationDbContext _db;

    public RewardOptionProvider(IApplicationDbContext db)
    {
        _db = db;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.Reward;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        string? search,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<Reward> query = _db.Rewards.Where(r => r.BroadcasterId == broadcasterId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.ToLowerInvariant();
            query = query.Where(r => r.Title.ToLower().Contains(term));
        }

        int total = await query.CountAsync(ct);
        List<Reward> page = await query
            .OrderBy(r => r.Title)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        List<PipelineOption> options = [.. page.Select(ToOption)];
        return Result.Success(PipelineOptionListResult.Of(options, total));
    }

    private static PipelineOption ToOption(Reward reward)
    {
        string cost = reward.Cost is { } c ? $"{c} points" : "cost unknown";
        string secondaryText = $"{cost} · {(reward.IsPaused ? "paused" : "active")}";

        return new PipelineOption(
            reward.Id.ToString(),
            reward.Title,
            secondaryText,
            ImageUrl: null,
            reward.IsEnabled ? PipelineOptionState.Selectable : PipelineOptionState.Unavailable,
            reward.IsEnabled ? null : "Reward is disabled."
        );
    }
}
