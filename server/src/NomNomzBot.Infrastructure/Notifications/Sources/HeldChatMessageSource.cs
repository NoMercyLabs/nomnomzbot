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
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Enums;
using NomNomzBot.Domain.Moderation.Events;

namespace NomNomzBot.Infrastructure.Notifications.Sources;

/// <summary>
/// AutoMod-held chat messages pending review, grouped per sender (N pending holds from one user = ONE item,
/// S-OWN22 T2). Each hold dismisses under its own <c>held:{guid}</c> key, so a grouped item dismisses as all of
/// its holds and a NEW hold from the same user surfaces again.
/// </summary>
public sealed class HeldChatMessageSource(IApplicationDbContext db) : IActionRequiredSource
{
    private const string HeldKeyPrefix = "held:";
    private const string HeldUserKeyPrefix = "held-user:";

    public IReadOnlyCollection<string> KeyPrefixes { get; } = [HeldKeyPrefix, HeldUserKeyPrefix];

    public IReadOnlyCollection<string> InvalidatingEventTypes { get; } =
    [nameof(AutoModMessageHeldEvent), nameof(AutoModMessageUpdatedEvent)];

    public async Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        IReadOnlySet<string> dismissedKeys,
        CancellationToken cancellationToken = default
    )
    {
        List<ModerationQueueItem> pending = await LoadPendingHoldsAsync(
            channelId,
            cancellationToken
        );

        // One item per sender: identityless holds fall back to their own held:{guid} group key so they
        // stay single items instead of collapsing into one anonymous bucket.
        IEnumerable<IGrouping<string, ModerationQueueItem>> perSender = pending
            .Where(i => !dismissedKeys.Contains(HeldKey(i.Id)))
            .GroupBy(i => SourceUserKeyOf(i) ?? HeldKey(i.Id), StringComparer.Ordinal);

        List<ActionRequiredItemDto> items =
        [
            .. perSender.Select(group => ToItem(group.Key, [.. group.OrderBy(i => i.CreatedAt)])),
        ];
        return Result.Success(items);
    }

    public async Task<Result<List<string>>> ResolveDismissalKeysAsync(
        Guid channelId,
        string itemId,
        CancellationToken cancellationToken = default
    )
    {
        if (!itemId.StartsWith(HeldUserKeyPrefix, StringComparison.Ordinal))
            return Result.Success<List<string>>([itemId]);

        string sourceUserKey = itemId[HeldUserKeyPrefix.Length..];
        List<ModerationQueueItem> pending = await LoadPendingHoldsAsync(
            channelId,
            cancellationToken
        );
        return Result.Success<List<string>>([
            .. pending
                .Where(i =>
                    string.Equals(SourceUserKeyOf(i), sourceUserKey, StringComparison.Ordinal)
                )
                .Select(i => HeldKey(i.Id)),
        ]);
    }

    private static ActionRequiredItemDto ToItem(string groupKey, List<ModerationQueueItem> holds)
    {
        ModerationQueueItem newest = holds[^1];
        string? username = holds
            .Select(i => i.TargetUsernameSnapshot)
            .LastOrDefault(name => name is not null);

        Dictionary<string, string> parameters = new() { ["count"] = holds.Count.ToString() };
        if (username is not null)
            parameters["username"] = username;
        if (newest.AutoModCategory is not null)
            parameters["category"] = newest.AutoModCategory;

        return new ActionRequiredItemDto(
            Id: holds.Count == 1 ? HeldKey(holds[0].Id) : $"{HeldUserKeyPrefix}{groupKey}",
            Kind: "held_chat_message",
            Severity: "warning",
            TitleKey: "attention_held_title",
            MessageKey: MessageKeyFor(holds.Count, username),
            Parameters: parameters,
            DetectedAt: newest.CreatedAt,
            DeepLinkRoute: "moderationqueue",
            SourceUserId: SourceUserKeyOf(newest),
            SourceUserName: username,
            Count: holds.Count,
            QueueItemIds: [.. holds.Select(i => i.Id)]
        );
    }

    private static string MessageKeyFor(int count, string? username) =>
        (count == 1, username is not null) switch
        {
            (true, true) => "attention_held_single_from_user_message",
            (true, false) => "attention_held_single_message",
            (false, true) => "attention_held_many_from_user_message",
            (false, false) => "attention_held_many_message",
        };

    private static string HeldKey(Guid queueItemId) => $"{HeldKeyPrefix}{queueItemId}";

    /// <summary>
    /// The stable per-sender key held messages group under — the platform user id when known, else the
    /// resolved internal user id, else the username snapshot. Null when the hold carries no sender identity
    /// at all (such a hold stays its own single item).
    /// </summary>
    private static string? SourceUserKeyOf(ModerationQueueItem item) =>
        item.TargetTwitchUserId ?? item.TargetUserId?.ToString() ?? item.TargetUsernameSnapshot;

    private async Task<List<ModerationQueueItem>> LoadPendingHoldsAsync(
        Guid channelId,
        CancellationToken cancellationToken
    ) =>
        await db
            .ModerationQueueItems.Where(i =>
                i.BroadcasterId == channelId
                && i.Source == ModerationQueueSource.AutoMod
                && i.Status == ModerationQueueStatus.Pending
            )
            .ToListAsync(cancellationToken);
}
