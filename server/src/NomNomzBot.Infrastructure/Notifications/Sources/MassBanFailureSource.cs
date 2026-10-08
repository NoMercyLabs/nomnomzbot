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
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Notifications.Services;

namespace NomNomzBot.Infrastructure.Notifications.Sources;

/// <summary>
/// Mass-ban batches that finished with accounts Twitch never banned: a refusal, or a transient failure that kept
/// failing until the last attempt. Shown in the inbox of the channel the batch ran in and in the inbox of the
/// channel owned by the moderator who asked for it; each target's reason is readable through the mass-ban batch
/// endpoints. A batch still waiting on retries is not reported, because it can still succeed.
/// </summary>
public sealed class MassBanFailureSource(IApplicationDbContext db) : IActionRequiredSource
{
    private const string KeyPrefix = "massban-failed:";

    public IReadOnlyCollection<string> KeyPrefixes { get; } = [KeyPrefix];

    // A batch finishing is not a journaled domain event; the inbox is read when the dashboard loads.
    public IReadOnlyCollection<string> InvalidatingEventTypes { get; } = [];

    public async Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        IReadOnlySet<string> dismissedKeys,
        CancellationToken cancellationToken = default
    )
    {
        Guid? ownerUserId = await db
            .Channels.AsNoTracking()
            .Where(c => c.Id == channelId)
            .Select(c => (Guid?)c.OwnerUserId)
            .FirstOrDefaultAsync(cancellationToken);

        List<BatchRow> batches = await db
            .MassBanBatches.AsNoTracking()
            .Where(b =>
                b.CompletedAt != null
                && (b.ChannelId == channelId || b.OperatorUserId == ownerUserId)
                && b.Targets.Any(t => !t.Banned)
            )
            .Select(b => new BatchRow(
                b.Id,
                b.ChannelLogin,
                b.CompletedAt!.Value,
                b.Targets.Count,
                b.Targets.Count(t => !t.Banned)
            ))
            .ToListAsync(cancellationToken);

        List<ActionRequiredItemDto> items =
        [
            .. batches.Select(ToItem).Where(item => !dismissedKeys.Contains(item.Id)),
        ];
        return Result.Success(items);
    }

    public Task<Result<List<string>>> ResolveDismissalKeysAsync(
        Guid channelId,
        string itemId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success<List<string>>([itemId]));

    private static ActionRequiredItemDto ToItem(BatchRow batch) =>
        new(
            Id: $"{KeyPrefix}{batch.Id}",
            Kind: "mass_ban_failed",
            Severity: "warning",
            TitleKey: "attention_mass_ban_failed_title",
            MessageKey: "attention_mass_ban_failed_message",
            Parameters: new()
            {
                ["channelLogin"] = batch.ChannelLogin,
                ["failedCount"] = batch.Failed.ToString(),
                ["totalCount"] = batch.Total.ToString(),
            },
            DetectedAt: batch.CompletedAt,
            DeepLinkRoute: "moderation",
            SourceUserId: null,
            SourceUserName: null,
            Count: batch.Failed,
            QueueItemIds: []
        );

    private sealed record BatchRow(
        Guid Id,
        string ChannelLogin,
        DateTime CompletedAt,
        int Total,
        int Failed
    );
}
