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
using NomNomzBot.Application.Admin.Diagnostics;
using NomNomzBot.Application.Admin.Dtos;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.Admin.Diagnostics;

/// <summary>The <c>summary</c> section: the channel's name, plan and live state, and how its runs went in the
/// last 24 hours (counted by the channel's own <see cref="IPipelineExecutionQueryService"/>).</summary>
public sealed class ChannelSummaryDiagnosticSection(
    IApplicationDbContext db,
    IPipelineExecutionQueryService runs,
    TimeProvider clock
) : IChannelDiagnosticSection
{
    private static readonly TimeSpan RunWindow = TimeSpan.FromHours(24);

    public string Key => "summary";

    public async Task<Result> ReadIntoAsync(
        ChannelDiagnosticRequest request,
        ChannelDiagnosticsDto diagnostics,
        CancellationToken cancellationToken = default
    )
    {
        ChannelFacts? channel = await db
            .Channels.AsNoTracking()
            .Where(c => c.Id == request.BroadcasterId)
            .Select(c => new ChannelFacts(c.Name, c.BillingTierKey, c.IsLive))
            .FirstOrDefaultAsync(cancellationToken);
        if (channel is null)
            return Result.Failure("Unknown channel.", "NOT_FOUND");

        string? tierName = await db
            .BillingTiers.AsNoTracking()
            .Where(t => t.Key == channel.TierKey)
            .Select(t => t.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);

        DateTime since = clock.GetUtcNow().UtcDateTime - RunWindow;
        Result<PipelineExecutionActivityDto> activity = await runs.GetActivityAsync(
            request.BroadcasterId.ToString(),
            since,
            cancellationToken
        );
        if (activity.IsFailure)
            return activity;

        diagnostics.Summary = new(
            channel.Name,
            channel.TierKey,
            tierName ?? channel.TierKey,
            channel.IsLive,
            activity.Value.Runs,
            activity.Value.FailedRuns,
            activity.Value.LastFailedRunId,
            activity.Value.LastFailedRunAt,
            activity.Value.LastFailedRunError
        );
        return Result.Success();
    }

    private sealed record ChannelFacts(string Name, string TierKey, bool IsLive);
}
