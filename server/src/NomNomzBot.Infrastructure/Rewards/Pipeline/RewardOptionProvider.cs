// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        IQueryable<Reward> query = _db.Rewards.Where(r => r.BroadcasterId == broadcasterId);
        if (request.IsValueLookup)
        {
            Guid valueId = request.ValueAsGuid ?? Guid.Empty;
            query = query.Where(r => r.Id == valueId);
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
            reward.Id.ToString(),
            reward.Title,
            secondaryText,
            ImageUrl: null,
            reward.IsEnabled ? PipelineOptionState.Selectable : PipelineOptionState.Unavailable,
            reward.IsEnabled ? null : "Reward is disabled."
        );
    }
}
