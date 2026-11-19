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

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Infrastructure.Notifications.Sources;

/// <summary>
/// Spam the bot saw in a channel that did not onboard, and left in chat because no moderator said it may act in
/// their name there. One item per channel, counting the last <see cref="Window"/>, so the moderator sees that
/// nothing was removed and where to turn removal on. It goes away once a moderator turns their switch on.
/// </summary>
public sealed class SpamNotRemovedSource(IApplicationDbContext db, TimeProvider clock)
    : IActionRequiredSource
{
    private const string KeyPrefix = "spam-not-removed:";

    /// <summary>How long seen spam stays in the inbox — long enough to cover one stream.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    public string SourceKey => "spam_not_removed";

    public bool ConcernsModeration => true;

    public IReadOnlyCollection<string> KeyPrefixes { get; } = [KeyPrefix];

    public IReadOnlyCollection<string> InvalidatingEventTypes { get; } = [];

    public async Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        IReadOnlySet<string> dismissedKeys,
        CancellationToken cancellationToken = default
    )
    {
        if (await SomeoneActsAsync(channelId, cancellationToken))
            return Result.Success<List<ActionRequiredItemDto>>([]);

        DateTime since = clock.GetUtcNow().UtcDateTime - Window;
        IQueryable<SpamDetection> left = db
            .SpamDetections.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(d =>
                d.BroadcasterId == channelId
                && d.DeletedAt == null
                && !d.WasDryRun
                && (
                    d.Outcome == SpamOutcome.DeleteAndQueue
                    || d.Outcome == SpamOutcome.DeleteAndEscalate
                )
                && d.DetectedAt >= since
            );

        SpamDetection? latest = await left.OrderByDescending(d => d.DetectedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is null || dismissedKeys.Contains(KeyPrefix + latest.Id))
            return Result.Success<List<ActionRequiredItemDto>>([]);

        int count = await left.CountAsync(cancellationToken);
        return Result.Success<List<ActionRequiredItemDto>>([ToItem(latest, count)]);
    }

    // True when removal already has an identity here: the channel onboarded, a moderator turned their own
    // switch on, or a moderator recorded the streamer's yes. Then the spam is acted on and there is nothing to say.
    private async Task<bool> SomeoneActsAsync(Guid channelId, CancellationToken ct)
    {
        Channel? channel = await db
            .Channels.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == channelId, ct);
        if (channel is null || channel.IsOnboarded)
            return true;

        bool ownYes = await db
            .ChannelMemberships.IgnoreQueryFilters()
            .AnyAsync(
                m => m.BroadcasterId == channelId && m.DeletedAt == null && m.RemovesSpamInOwnName,
                ct
            );
        if (ownYes)
            return true;

        if (channel.TwitchChannelId is not { } twitchChannelId)
            return false;

        return await db.ModeratorMassBanOptIns.AnyAsync(
            o => o.BroadcasterTwitchId == twitchChannelId,
            ct
        );
    }

    private static ActionRequiredItemDto ToItem(SpamDetection latest, int count) =>
        new(
            Id: KeyPrefix + latest.Id,
            Kind: "spam_not_removed",
            Severity: "warning",
            TitleKey: "attention_spam_not_removed_title",
            MessageKey: "attention_spam_not_removed_message",
            Parameters: new Dictionary<string, string>
            {
                ["username"] = latest.SubjectDisplayName,
                ["count"] = count.ToString(CultureInfo.InvariantCulture),
            },
            DetectedAt: latest.DetectedAt,
            DeepLinkRoute: "chat/one-channel",
            SourceUserId: latest.SubjectPlatformUserId,
            SourceUserName: latest.SubjectDisplayName,
            Count: count,
            QueueItemIds: []
        );

    public Task<Result<List<string>>> ResolveDismissalKeysAsync(
        Guid channelId,
        string itemId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success<List<string>>([itemId]));
}
