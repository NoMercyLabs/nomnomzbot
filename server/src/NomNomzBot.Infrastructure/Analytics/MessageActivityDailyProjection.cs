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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Analytics.Entities;

namespace NomNomzBot.Infrastructure.Analytics;

/// <summary>
/// Folds chat into the per-viewer daily message-count aggregate (analytics.md §3.1, schema M.4 — counts only, no
/// content). Resolves the viewer via the shared <see cref="ViewerResolver"/>, then upserts the
/// <c>(broadcaster, viewer, channel-local date)</c> row. Plain rollup — a rebuild hard-removes then re-folds.
/// </summary>
public sealed class MessageActivityDailyProjection(
    IApplicationDbContext db,
    ViewerResolver resolver
) : IProjection
{
    private static readonly HashSet<string> Subscribed = new(StringComparer.Ordinal)
    {
        "ChatMessageReceivedEvent",
    };

    public string Name => "message-activity-daily";
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
        MessageActivityDaily row = await GetOrCreateAsync(
            broadcasterId,
            profile.ViewerUserId,
            profile.Id,
            date,
            cancellationToken
        );
        row.MessageCount++;
        row.FirstMessageAt ??= @event.OccurredAt;
        row.LastMessageAt = @event.OccurredAt;

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ResetAsync(
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<MessageActivityDaily> rows = await (
            broadcasterId is Guid id
                ? db.MessageActivityDailies.Where(r => r.BroadcasterId == id)
                : db.MessageActivityDailies
        ).ToListAsync(cancellationToken);
        db.MessageActivityDailies.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<MessageActivityDaily> GetOrCreateAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        Guid viewerProfileId,
        DateOnly date,
        CancellationToken ct
    )
    {
        // IgnoreQueryFilters: tenant-less projection-driver / rebuild scope — the ITenantScoped filter would hide
        // the row just committed for this (broadcaster, viewer, day), causing a re-insert + unique-index 23505.
        MessageActivityDaily? row = await db
            .MessageActivityDailies.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                r =>
                    r.BroadcasterId == broadcasterId
                    && r.ViewerUserId == viewerUserId
                    && r.ActivityDate == date,
                ct
            );
        if (row is null)
        {
            row = new MessageActivityDaily
            {
                BroadcasterId = broadcasterId,
                ViewerUserId = viewerUserId,
                ViewerProfileId = viewerProfileId,
                ActivityDate = date,
            };
            db.MessageActivityDailies.Add(row);
        }
        return row;
    }
}
