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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Events;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// See <see cref="IEventSubGapBackfillService"/>. Sweeps the two Twitch sources that carry a real
/// timestamp/id a client can page by (redemptions, follows), derives a stable <c>EventId</c> per candidate,
/// drops anything already journaled, and publishes the rest oldest-first through the ordinary
/// <see cref="IEventBus"/> path.
/// </summary>
public sealed class EventSubGapBackfillService : IEventSubGapBackfillService
{
    private readonly ITwitchChannelPointsApi _channelPoints;
    private readonly ITwitchChannelsApi _channels;
    private readonly IEventJournal _journal;
    private readonly IEventBus _eventBus;
    private readonly ILogger<EventSubGapBackfillService> _logger;

    private static readonly string[] RedemptionStatusesToSweep = ["UNFULFILLED", "FULFILLED"];

    public EventSubGapBackfillService(
        ITwitchChannelPointsApi channelPoints,
        ITwitchChannelsApi channels,
        IEventJournal journal,
        IEventBus eventBus,
        ILogger<EventSubGapBackfillService> logger
    )
    {
        _channelPoints = channelPoints;
        _channels = channels;
        _journal = journal;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<Result<int>> BackfillGapAsync(
        Guid broadcasterId,
        DateTimeOffset gapStart,
        DateTimeOffset gapEnd,
        CancellationToken ct = default
    )
    {
        if (gapEnd <= gapStart)
            return Result.Success(0);

        List<(Guid EventId, DateTimeOffset At, Func<RewardRedeemedEvent> Build)> redemptions =
            await CollectRedemptionsAsync(broadcasterId, gapStart, gapEnd, ct);
        List<(Guid EventId, DateTimeOffset At, Func<FollowEvent> Build)> follows =
            await CollectFollowsAsync(broadcasterId, gapStart, gapEnd, ct);

        HashSet<Guid> candidateIds =
        [
            .. redemptions.Select(r => r.EventId),
            .. follows.Select(f => f.EventId),
        ];
        if (candidateIds.Count == 0)
            return Result.Success(0);

        Result<IReadOnlySet<Guid>> existingResult = await _journal.GetExistingEventIdsAsync(
            candidateIds,
            ct
        );
        if (existingResult.IsFailure)
            return existingResult.WithValue(0);
        IReadOnlySet<Guid> existing = existingResult.Value;

        // Merge both sources into one chronological timeline before publishing, so downstream
        // ordering-sensitive consumers (chat feed, dashboard timeline) see the gap replayed in the
        // order it actually happened, not "all redemptions then all follows". DistinctBy(EventId) guards
        // against the SAME redemption surfacing twice within one sweep — e.g. a status transition mid-page
        // between the UNFULFILLED and FULFILLED passes — collapsing to one publish, never two.
        List<(DateTimeOffset At, Func<Task> Publish)> toPublish =
        [
            .. redemptions
                .Where(r => !existing.Contains(r.EventId))
                .DistinctBy(r => r.EventId)
                .Select(r => (r.At, (Func<Task>)(() => _eventBus.PublishAsync(r.Build(), ct)))),
            .. follows
                .Where(f => !existing.Contains(f.EventId))
                .DistinctBy(f => f.EventId)
                .Select(f => (f.At, (Func<Task>)(() => _eventBus.PublishAsync(f.Build(), ct)))),
        ];
        toPublish.Sort((a, b) => a.At.CompareTo(b.At));

        foreach ((DateTimeOffset _, Func<Task> publish) in toPublish)
            await publish();

        _logger.LogInformation(
            "EventSub gap backfill for {BroadcasterId} [{Start:o}, {End:o}]: {Count} missed event(s) replayed",
            broadcasterId,
            gapStart,
            gapEnd,
            toPublish.Count
        );

        return Result.Success(toPublish.Count);
    }

    private async Task<
        List<(Guid EventId, DateTimeOffset At, Func<RewardRedeemedEvent> Build)>
    > CollectRedemptionsAsync(
        Guid broadcasterId,
        DateTimeOffset gapStart,
        DateTimeOffset gapEnd,
        CancellationToken ct
    )
    {
        List<(Guid, DateTimeOffset, Func<RewardRedeemedEvent>)> found = [];

        Result<IReadOnlyList<TwitchCustomReward>> rewardsResult =
            await _channelPoints.GetCustomRewardsAsync(
                broadcasterId,
                onlyManageableRewards: true,
                ct: ct
            );
        if (rewardsResult.IsFailure)
        {
            _logger.LogWarning(
                "Gap backfill: could not list {BroadcasterId}'s manageable rewards ({Error}) — redemption sweep skipped",
                broadcasterId,
                rewardsResult.ErrorMessage
            );
            return found;
        }

        foreach (TwitchCustomReward reward in rewardsResult.Value)
        foreach (string status in RedemptionStatusesToSweep)
        {
            string? cursor = null;
            bool keepPaging = true;
            while (keepPaging)
            {
                Result<TwitchPage<TwitchCustomRewardRedemption>> page =
                    await _channelPoints.GetCustomRewardRedemptionsAsync(
                        broadcasterId,
                        reward.Id,
                        status,
                        redemptionIds: null,
                        sort: "NEWEST",
                        new TwitchPageRequest(cursor),
                        ct
                    );
                if (page.IsFailure)
                    break;

                foreach (TwitchCustomRewardRedemption r in page.Value.Items)
                {
                    if (r.RedeemedAt > gapEnd)
                        continue;
                    if (r.RedeemedAt < gapStart)
                    {
                        keepPaging = false; // NEWEST-sorted: everything after this is even older
                        break;
                    }

                    Guid eventId = DeterministicId("redemption", r.Id);
                    found.Add(
                        (
                            eventId,
                            r.RedeemedAt,
                            () =>
                                new RewardRedeemedEvent
                                {
                                    EventId = eventId,
                                    BroadcasterId = broadcasterId,
                                    OccurredAt = r.RedeemedAt,
                                    RewardId = reward.Id,
                                    RewardTitle = reward.Title,
                                    RedemptionId = r.Id,
                                    UserId = r.UserId,
                                    UserDisplayName = r.UserName,
                                    Cost = reward.Cost,
                                    UserInput = string.IsNullOrEmpty(r.UserInput)
                                        ? null
                                        : r.UserInput,
                                }
                        )
                    );
                }

                cursor = page.Value.NextCursor;
                keepPaging = keepPaging && cursor is not null;
            }
        }

        return found;
    }

    private async Task<
        List<(Guid EventId, DateTimeOffset At, Func<FollowEvent> Build)>
    > CollectFollowsAsync(
        Guid broadcasterId,
        DateTimeOffset gapStart,
        DateTimeOffset gapEnd,
        CancellationToken ct
    )
    {
        List<(Guid, DateTimeOffset, Func<FollowEvent>)> found = [];

        string? cursor = null;
        bool keepPaging = true;
        while (keepPaging)
        {
            Result<TwitchPage<TwitchChannelFollower>> page =
                await _channels.GetChannelFollowersAsync(
                    broadcasterId,
                    new TwitchPageRequest(cursor),
                    ct
                );
            if (page.IsFailure)
            {
                if (found.Count == 0)
                    _logger.LogWarning(
                        "Gap backfill: could not list {BroadcasterId}'s followers ({Error}) — follow sweep skipped",
                        broadcasterId,
                        page.ErrorMessage
                    );
                break;
            }

            foreach (TwitchChannelFollower f in page.Value.Items)
            {
                if (f.FollowedAt > gapEnd)
                    continue;
                if (f.FollowedAt < gapStart)
                {
                    keepPaging = false; // most-recent-first: everything after this is even older
                    break;
                }

                Guid eventId = DeterministicId(
                    "follow",
                    f.UserId,
                    f.FollowedAt.ToUnixTimeSeconds().ToString()
                );
                found.Add(
                    (
                        eventId,
                        f.FollowedAt,
                        () =>
                            new FollowEvent
                            {
                                EventId = eventId,
                                BroadcasterId = broadcasterId,
                                OccurredAt = f.FollowedAt,
                                UserId = f.UserId,
                                UserDisplayName = f.UserName,
                                UserLogin = f.UserLogin,
                                FollowedAt = f.FollowedAt,
                            }
                    )
                );
            }

            cursor = page.Value.NextCursor;
            keepPaging = keepPaging && cursor is not null;
        }

        return found;
    }

    /// <summary>
    /// A stable Guid derived from a natural Twitch identity (never random), so republishing the same
    /// real-world event — from an overlapping backfill window, or a repeated reconnect — always
    /// resolves to the same <c>EventId</c> and is caught by <see cref="IEventJournal.GetExistingEventIdsAsync"/>
    /// before it ever reaches the bus a second time.
    /// </summary>
    private static Guid DeterministicId(string kind, params string[] parts)
    {
        string key = string.Join('|', new[] { "eventsub-backfill", kind }.Concat(parts));
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash);
    }
}
