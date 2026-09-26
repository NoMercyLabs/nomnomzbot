// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Domain.Rewards.Events;

namespace NomNomzBot.Infrastructure.Notifications.Sources;

/// <summary>
/// Rewards imported from Twitch with <c>IsManageable = false</c> (created in the Twitch dashboard or by another
/// app, see rewards.md) stay read-only until the streamer takes control of them on the Rewards page. Grouped as
/// ONE item per channel, since the fix is the same action regardless of count.
/// </summary>
public sealed class UnmanagedRewardSource(IApplicationDbContext db) : IActionRequiredSource
{
    private const string KeyPrefix = "unmanaged-rewards:";

    public IReadOnlyCollection<string> KeyPrefixes { get; } = [KeyPrefix];

    public IReadOnlyCollection<string> InvalidatingEventTypes { get; } =
    [nameof(RewardCreatedEvent), nameof(RewardUpdatedEvent), nameof(RewardRemovedEvent)];

    public async Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        IReadOnlySet<string> dismissedKeys,
        CancellationToken cancellationToken = default
    )
    {
        List<Reward> unmanaged = await db
            .Rewards.Where(r =>
                r.BroadcasterId == channelId && r.TwitchRewardId != null && !r.IsManageable
            )
            .OrderBy(r => r.Id)
            .ToListAsync(cancellationToken);
        if (unmanaged.Count == 0)
            return Result.Success<List<ActionRequiredItemDto>>([]);

        string key = SetKey(channelId, unmanaged.Select(r => r.Id));
        if (dismissedKeys.Contains(key))
            return Result.Success<List<ActionRequiredItemDto>>([]);

        int pendingFinalization = unmanaged.Count(r => r.PendingMigrationRequestedAt is not null);
        ActionRequiredItemDto item = new(
            Id: key,
            Kind: "unmanaged_rewards",
            Severity: "info",
            TitleKey: "attention_unmanaged_rewards_title",
            MessageKey: pendingFinalization > 0
                ? "attention_unmanaged_rewards_pending_message"
                : "attention_unmanaged_rewards_message",
            Parameters: new()
            {
                ["count"] = unmanaged.Count.ToString(),
                ["pendingCount"] = pendingFinalization.ToString(),
            },
            DetectedAt: unmanaged.Max(r => r.CreatedAt),
            DeepLinkRoute: "rewards",
            SourceUserId: null,
            SourceUserName: null,
            Count: unmanaged.Count,
            QueueItemIds: []
        );
        return Result.Success<List<ActionRequiredItemDto>>([item]);
    }

    public Task<Result<List<string>>> ResolveDismissalKeysAsync(
        Guid channelId,
        string itemId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success<List<string>>([itemId]));

    /// <summary>
    /// The key embeds the SET of unmanaged reward ids, so dismissing "these 3" does not hide a 4th that appears
    /// later. Hashed, since the dismissal's <c>ItemKey</c> column is bounded and a channel can have many rewards.
    /// </summary>
    private static string SetKey(Guid channelId, IEnumerable<Guid> rewardIds)
    {
        string joined = string.Join(',', rewardIds.OrderBy(id => id));
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)))[..16];
        return $"{KeyPrefix}{channelId}:{hash}";
    }
}
