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
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Analytics.Entities;

namespace NomNomzBot.Infrastructure.Analytics;

/// <summary>
/// Folds activity into the per-viewer daily engagement roll-up (analytics.md §3.1, schema M.7) — the time-series
/// behind the viewer charts. Resolves the viewer via the shared <see cref="ViewerResolver"/>, then upserts the
/// <c>(broadcaster, viewer, channel-local date)</c> row. Folds chat/command/reward today; watch-seconds (from M.2),
/// song requests, currency, and games extend it as those events are wired. Plain rollup — rebuild = remove + refold.
/// </summary>
public sealed class ViewerEngagementDailyProjection(
    IApplicationDbContext db,
    ViewerResolver resolver
) : IProjection
{
    private static readonly HashSet<string> Subscribed = new(StringComparer.Ordinal)
    {
        "ChatMessageReceivedEvent",
        "CommandExecutedEvent",
        "RewardRedeemedEvent",
    };

    public string Name => "viewer-engagement-daily";
    public bool IsGlobal => false;
    public IReadOnlySet<string> SubscribedEventTypes => Subscribed;

    public async Task<Result> ApplyAsync(
        EventRecord @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId is not Guid broadcasterId)
            return Result.Success();
        (string Provider, string ExternalUserId, string Login, string Display)? identity =
            ViewerResolver.ParseIdentity(@event.PayloadJson);
        if (identity is null)
            return Result.Success();

        ViewerProfile? profile = await resolver.ResolveAsync(
            broadcasterId,
            identity.Value.Provider,
            identity.Value.ExternalUserId,
            identity.Value.Login,
            identity.Value.Display,
            cancellationToken
        );
        if (profile is null)
            return Result.Success();

        DateOnly date = DateOnly.FromDateTime(@event.OccurredAt);
        ViewerEngagementDaily row = await GetOrCreateAsync(
            broadcasterId,
            profile.ViewerUserId,
            profile.Id,
            date,
            cancellationToken
        );

        switch (@event.EventType)
        {
            case "ChatMessageReceivedEvent":
                row.MessageCount++;
                break;
            case "CommandExecutedEvent":
                // Only a run that actually did its work counts as "executed".
                if (
                    ViewerResolver.TryParse(@event.PayloadJson)?["Succeeded"]?.Value<bool?>()
                    == true
                )
                    row.CommandCount++;
                break;
            case "RewardRedeemedEvent":
                row.RedemptionCount++;
                break;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ResetAsync(
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<ViewerEngagementDaily> rows = await (
            broadcasterId is Guid id
                ? db.ViewerEngagementDailies.Where(r => r.BroadcasterId == id)
                : db.ViewerEngagementDailies
        ).ToListAsync(cancellationToken);
        db.ViewerEngagementDailies.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<ViewerEngagementDaily> GetOrCreateAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        Guid viewerProfileId,
        DateOnly date,
        CancellationToken ct
    )
    {
        ViewerEngagementDaily? row = await db.ViewerEngagementDailies.FirstOrDefaultAsync(
            r =>
                r.BroadcasterId == broadcasterId
                && r.ViewerUserId == viewerUserId
                && r.ActivityDate == date,
            ct
        );
        if (row is null)
        {
            row = new ViewerEngagementDaily
            {
                BroadcasterId = broadcasterId,
                ViewerUserId = viewerUserId,
                ViewerProfileId = viewerProfileId,
                ActivityDate = date,
            };
            db.ViewerEngagementDailies.Add(row);
        }
        return row;
    }
}
