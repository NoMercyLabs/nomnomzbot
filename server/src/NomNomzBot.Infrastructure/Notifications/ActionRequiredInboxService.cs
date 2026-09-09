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
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Enums;
using NomNomzBot.Domain.Notifications.Entities;
using NomNomzBot.Domain.Rewards.Entities;

namespace NomNomzBot.Infrastructure.Notifications;

/// <summary>
/// Aggregates the action-required inbox (S071a) from the existing signals that genuinely back it today:
/// dead/expired <see cref="IntegrationConnection"/> rows, pending AutoMod-held <see cref="ModerationQueueItem"/>
/// rows, and unmanaged (externally-created) <see cref="Reward"/> rows once imported. See
/// <see cref="IActionRequiredInboxService"/> for why the other candidate categories (missing scopes, failed
/// timers, unban requests) are deliberately excluded rather than faked.
/// <para>
/// S-OWN22 T2: every item carries a stable <see cref="ActionRequiredItemDto.Id"/>, held messages are grouped
/// per sender (N pending holds from one user = ONE item), and persisted
/// <see cref="ActionRequiredDismissal"/> rows filter items out. A dead-token key embeds its invalidation
/// instant, so re-invalidation after a fix mints a NEW key an old dismissal cannot hide.
/// </para>
/// </summary>
public sealed class ActionRequiredInboxService(IApplicationDbContext db, TimeProvider clock)
    : IActionRequiredInboxService
{
    private const string HeldKeyPrefix = "held:";
    private const string HeldUserKeyPrefix = "held-user:";
    private const string TokenKeyPrefix = "token:";
    private const string UnmanagedRewardsKeyPrefix = "unmanaged-rewards:";

    private static readonly HashSet<string> DeadConnectionStatuses = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        AuthEnums.IntegrationStatus.NeedsReauth,
        AuthEnums.IntegrationStatus.Expired,
    };

    public async Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        CancellationToken cancellationToken = default
    )
    {
        HashSet<string> dismissedKeys = await LoadDismissedKeysAsync(channelId, cancellationToken);
        List<ActionRequiredItemDto> items = [];
        items.AddRange(
            await BuildDeadConnectionItemsAsync(channelId, dismissedKeys, cancellationToken)
        );
        items.AddRange(
            await BuildHeldMessageItemsAsync(channelId, dismissedKeys, cancellationToken)
        );
        items.AddRange(
            await BuildUnmanagedRewardItemsAsync(channelId, dismissedKeys, cancellationToken)
        );
        return Result.Success(items.OrderByDescending(i => i.DetectedAt).ToList());
    }

    public async Task<Result<int>> DismissAsync(
        Guid channelId,
        Guid dismissedByUserId,
        List<string> ids,
        CancellationToken cancellationToken = default
    )
    {
        List<string> requestedIds =
        [
            .. ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal),
        ];
        if (requestedIds.Count == 0)
            return Result.Failure<int>("No item ids given.", "VALIDATION_FAILED");

        List<string> itemKeys = [];
        foreach (string id in requestedIds)
        {
            if (id.StartsWith(HeldUserKeyPrefix, StringComparison.Ordinal))
            {
                // A grouped per-user item is dismissed as its contained held:{guid} keys, so a NEW hold from
                // the same user after the dismissal surfaces again.
                string sourceUserKey = id[HeldUserKeyPrefix.Length..];
                List<ModerationQueueItem> pendingHeldMessages = await LoadPendingHeldMessagesAsync(
                    channelId,
                    cancellationToken
                );
                itemKeys.AddRange(
                    pendingHeldMessages
                        .Where(i =>
                            string.Equals(
                                SourceUserKeyOf(i),
                                sourceUserKey,
                                StringComparison.Ordinal
                            )
                        )
                        .Select(i => HeldKey(i.Id))
                );
            }
            else if (
                id.StartsWith(HeldKeyPrefix, StringComparison.Ordinal)
                || id.StartsWith(TokenKeyPrefix, StringComparison.Ordinal)
                || id.StartsWith(UnmanagedRewardsKeyPrefix, StringComparison.Ordinal)
            )
            {
                itemKeys.Add(id);
            }
            else
            {
                return Result.Failure<int>(
                    $"Unknown action-required item id '{id}'.",
                    "VALIDATION_FAILED"
                );
            }
        }

        List<string> newKeys = [.. itemKeys.Distinct(StringComparer.Ordinal)];

        // Cross-tenant-safe uniqueness check: bypass the ambient tenant filter and re-apply the
        // soft-delete predicate explicitly, matching the filtered unique index (ChannelId, ItemKey).
        List<string> alreadyDismissed = await db
            .ActionRequiredDismissals.IgnoreQueryFilters()
            .Where(d => d.ChannelId == channelId && d.DeletedAt == null)
            .Where(d => newKeys.Contains(d.ItemKey))
            .Select(d => d.ItemKey)
            .ToListAsync(cancellationToken);
        HashSet<string> existingKeys = new(alreadyDismissed, StringComparer.Ordinal);

        DateTime dismissedAt = clock.GetUtcNow().UtcDateTime;
        List<ActionRequiredDismissal> rows =
        [
            .. newKeys
                .Where(key => !existingKeys.Contains(key))
                .Select(key => new ActionRequiredDismissal
                {
                    ChannelId = channelId,
                    ItemKey = key,
                    DismissedByUserId = dismissedByUserId,
                    DismissedAt = dismissedAt,
                }),
        ];
        if (rows.Count == 0)
            return Result.Success(0);

        db.ActionRequiredDismissals.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(rows.Count);
    }

    private static string HeldKey(Guid queueItemId) => $"{HeldKeyPrefix}{queueItemId}";

    private static string HeldUserKey(string sourceUserKey) =>
        $"{HeldUserKeyPrefix}{sourceUserKey}";

    private static string TokenKey(Guid connectionId, DateTime invalidatedAtUtc) =>
        $"{TokenKeyPrefix}{connectionId}:{invalidatedAtUtc.Ticks}";

    /// <summary>
    /// The stable per-sender key held messages group under — the platform user id when known, else the
    /// resolved internal user id, else the username snapshot. Null when the hold carries no sender identity
    /// at all (such a hold stays its own single item).
    /// </summary>
    private static string? SourceUserKeyOf(ModerationQueueItem item) =>
        item.TargetTwitchUserId ?? item.TargetUserId?.ToString() ?? item.TargetUsernameSnapshot;

    private async Task<HashSet<string>> LoadDismissedKeysAsync(
        Guid channelId,
        CancellationToken cancellationToken
    )
    {
        List<string> keys = await db
            .ActionRequiredDismissals.IgnoreQueryFilters()
            .Where(d => d.ChannelId == channelId && d.DeletedAt == null)
            .Select(d => d.ItemKey)
            .ToListAsync(cancellationToken);
        return new HashSet<string>(keys, StringComparer.Ordinal);
    }

    private async Task<List<ModerationQueueItem>> LoadPendingHeldMessagesAsync(
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

    private async Task<List<ActionRequiredItemDto>> BuildDeadConnectionItemsAsync(
        Guid channelId,
        HashSet<string> dismissedKeys,
        CancellationToken cancellationToken
    )
    {
        List<IntegrationConnection> deadConnections = await db
            .IntegrationConnections.IgnoreQueryFilters()
            .Where(c => c.BroadcasterId == channelId)
            .ToListAsync(cancellationToken);

        return
        [
            .. deadConnections
                .Where(c => DeadConnectionStatuses.Contains(c.Status))
                .Select(c => new
                {
                    Connection = c,
                    InvalidatedAt = c.LastErrorAt ?? c.ConnectedAt ?? c.CreatedAt,
                })
                .Where(x => !dismissedKeys.Contains(TokenKey(x.Connection.Id, x.InvalidatedAt)))
                .Select(x => new ActionRequiredItemDto(
                    Id: TokenKey(x.Connection.Id, x.InvalidatedAt),
                    Kind: "integration_token_dead",
                    Severity: "critical",
                    Title: $"{x.Connection.Provider} connection needs re-authorization",
                    Message: x.Connection.Status == AuthEnums.IntegrationStatus.Expired
                        ? $"The {x.Connection.Provider} connection's token has expired. Reconnect it to restore the integration."
                        : $"The {x.Connection.Provider} connection failed to refresh {x.Connection.ConsecutiveFailureCount} time(s) in a row and needs re-authorization.",
                    DetectedAt: x.InvalidatedAt,
                    DeepLinkRoute: $"/settings/integrations/{x.Connection.Provider}",
                    SourceUserId: null,
                    SourceUserName: null,
                    Count: 1,
                    QueueItemIds: []
                )),
        ];
    }

    private async Task<List<ActionRequiredItemDto>> BuildHeldMessageItemsAsync(
        Guid channelId,
        HashSet<string> dismissedKeys,
        CancellationToken cancellationToken
    )
    {
        List<ModerationQueueItem> pendingHeldMessages = await LoadPendingHeldMessagesAsync(
            channelId,
            cancellationToken
        );

        // One item per sender: identityless holds fall back to their own held:{guid} group key so they
        // stay single items instead of collapsing into one anonymous bucket.
        IEnumerable<IGrouping<string, ModerationQueueItem>> perSender = pendingHeldMessages
            .Where(i => !dismissedKeys.Contains(HeldKey(i.Id)))
            .GroupBy(i => SourceUserKeyOf(i) ?? HeldKey(i.Id), StringComparer.Ordinal);

        List<ActionRequiredItemDto> items = [];
        foreach (IGrouping<string, ModerationQueueItem> group in perSender)
        {
            List<ModerationQueueItem> holds = [.. group.OrderBy(i => i.CreatedAt)];
            ModerationQueueItem newest = holds[^1];
            string? sourceUserId = SourceUserKeyOf(newest);
            string? username = holds
                .Select(i => i.TargetUsernameSnapshot)
                .LastOrDefault(name => name is not null);

            items.Add(
                new ActionRequiredItemDto(
                    Id: holds.Count == 1 ? HeldKey(holds[0].Id) : HeldUserKey(group.Key),
                    Kind: "held_chat_message",
                    Severity: "warning",
                    Title: holds.Count == 1
                        ? "A chat message is held for review"
                        : $"{holds.Count} chat messages are held for review",
                    Message: BuildHeldMessageText(holds.Count, username, newest.AutoModCategory),
                    DetectedAt: newest.CreatedAt,
                    DeepLinkRoute: "/moderation/queue",
                    SourceUserId: sourceUserId,
                    SourceUserName: username,
                    Count: holds.Count,
                    QueueItemIds: [.. holds.Select(i => i.Id)]
                )
            );
        }

        return items;
    }

    /// <summary>
    /// Rewards imported from Twitch with <c>IsManageable = false</c> (created in the Twitch dashboard or by
    /// another app — rewards.md) are read-only until "taken control of" from the Rewards page. Grouped as ONE
    /// item per channel rather than one per reward, since the fix (visit Rewards, take control) is the same
    /// action regardless of count.
    /// </summary>
    private async Task<List<ActionRequiredItemDto>> BuildUnmanagedRewardItemsAsync(
        Guid channelId,
        HashSet<string> dismissedKeys,
        CancellationToken cancellationToken
    )
    {
        List<Reward> unmanaged = await db
            .Rewards.Where(r =>
                r.BroadcasterId == channelId && r.TwitchRewardId != null && !r.IsManageable
            )
            .OrderBy(r => r.Id)
            .ToListAsync(cancellationToken);
        if (unmanaged.Count == 0)
            return [];

        string key = UnmanagedRewardsKey(channelId, unmanaged.Select(r => r.Id));
        if (dismissedKeys.Contains(key))
            return [];

        int pendingFinalization = unmanaged.Count(r => r.PendingMigrationRequestedAt is not null);

        return
        [
            new ActionRequiredItemDto(
                Id: key,
                Kind: "unmanaged_rewards",
                Severity: "info",
                Title: unmanaged.Count == 1
                    ? "1 channel-point reward isn't managed by NomNomzBot yet"
                    : $"{unmanaged.Count} channel-point rewards aren't managed by NomNomzBot yet",
                Message: BuildUnmanagedRewardText(unmanaged.Count, pendingFinalization),
                DetectedAt: unmanaged.Max(r => r.CreatedAt),
                DeepLinkRoute: "/rewards",
                SourceUserId: null,
                SourceUserName: null,
                Count: unmanaged.Count,
                QueueItemIds: []
            ),
        ];
    }

    private static string BuildUnmanagedRewardText(int count, int pendingFinalization)
    {
        string baseText =
            count == 1
                ? "Created in the Twitch dashboard or by another app, it can't be edited, paused, or deleted from here — take control of it on the Rewards page."
                : "Created in the Twitch dashboard or by another app, they can't be edited, paused, or deleted from here — take control of them on the Rewards page.";

        return pendingFinalization > 0
            ? baseText
                + $" {pendingFinalization} of them are waiting on you to free up their title on Twitch before the migration can finish."
            : baseText;
    }

    /// <summary>
    /// The dismissal key embeds the SET of unmanaged reward ids (not just the channel), the same "the key
    /// encodes what would make the old dismissal stale" pattern <see cref="TokenKey"/> uses for a re-invalidated
    /// connection — a streamer who dismisses "these 3 unmanaged rewards" must see the item again the moment a
    /// 4th one appears, not have it hidden forever by one old dismissal. Hashed (not joined raw) since the
    /// dismissal's persisted <c>ItemKey</c> column has a bounded length and a channel can have many rewards.
    /// </summary>
    private static string UnmanagedRewardsKey(Guid channelId, IEnumerable<Guid> rewardIds)
    {
        string joined = string.Join(',', rewardIds.OrderBy(id => id));
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)))[..16];
        return $"{UnmanagedRewardsKeyPrefix}{channelId}:{hash}";
    }

    private static string BuildHeldMessageText(int count, string? username, string? category)
    {
        if (count == 1)
        {
            return username is not null
                ? $"AutoMod held a message from {username} ({category ?? "flagged"}) pending your review."
                : $"AutoMod held a message ({category ?? "flagged"}) pending your review.";
        }

        return username is not null
            ? $"AutoMod held {count} messages from {username} pending your review."
            : $"AutoMod held {count} messages pending your review.";
    }
}
