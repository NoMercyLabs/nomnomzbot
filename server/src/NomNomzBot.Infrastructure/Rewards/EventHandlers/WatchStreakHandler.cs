// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Infrastructure.Platform.Eventing;

namespace NomNomzBot.Infrastructure.Rewards.EventHandlers;

/// <summary>
/// Handles Twitch's native watch-streak chat notification (EventSub <c>channel.chat.notification</c>,
/// translated by <c>ChatTranslators</c>). Upserts the WatchStreak entity and executes the
/// event_response:engagement.watch_streak pipeline — the SAME key the preset catalog and dashboard
/// expose, so a streamer's configured response actually reaches this real Twitch event (the parallel
/// <c>engagement.watch_streak</c> handler in Engagement/EventHandlers listens for a bot-computed
/// milestone event that nothing in this codebase ever publishes — dead on its own).
/// Variables exposed: user, user.id, user.login, user.name, viewer.name, engagement.streak (the preset's
/// names) and streak.months, streak.points, streak.message, plus the old bot's record split:
/// streak.record (the highest PREVIOUS streak, 0 when none; also exposed as record) and streak.state
/// (new_record when the streak beats a previous record above 0, rebuilt when it is below the record,
/// otherwise standard). A replayed event has no stored previous record, so it reads as standard.
/// </summary>
public sealed class WatchStreakHandler
    : TwitchAlertHandlerBase<WatchStreakReceivedEvent>,
        IEventHandler<WatchStreakReceivedEvent>
{
    protected override string EventTypeKey => "engagement.watch_streak";

    private readonly TimeProvider _timeProvider;

    // BuildVariables(event) is fixed by the base class and the event is init-only, so the previous max read
    // during the upsert travels beside the event instance instead of on it.
    private readonly ConditionalWeakTable<WatchStreakReceivedEvent, StrongBox<int>> _previousMax =
        new();

    public WatchStreakHandler(
        IServiceScopeFactory s,
        IPipelineEngine p,
        TimeProvider timeProvider,
        ILogger<WatchStreakHandler> l
    )
        : base(s, p, l)
    {
        _timeProvider = timeProvider;
    }

    protected override string? GetUserId(WatchStreakReceivedEvent e) => e.UserId;

    protected override string? GetUserDisplayName(WatchStreakReceivedEvent e) => e.UserDisplayName;

    protected override Dictionary<string, string> BuildVariables(WatchStreakReceivedEvent e)
    {
        int record = _previousMax.TryGetValue(e, out StrongBox<int>? box) ? box.Value : 0;
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = e.UserDisplayName,
            ["user.id"] = e.UserId,
            ["user.login"] = e.UserLogin,
            ["user.name"] = e.UserDisplayName,
            ["viewer.name"] = e.UserDisplayName,
            ["engagement.streak"] = e.StreakMonths.ToString(),
            ["streak.months"] = e.StreakMonths.ToString(),
            ["streak.points"] = e.ChannelPointsEarned.ToString(),
            ["streak.message"] = e.CustomMessage ?? string.Empty,
            ["streak.record"] = record.ToString(),
            ["record"] = record.ToString(),
            ["streak.state"] = StateFor(e.StreakMonths, record),
        };
    }

    /// <summary>The old bot's pick: beat a real record = new_record, below it = rebuilt, else standard.</summary>
    private static string StateFor(int streak, int record) =>
        streak > record && record > 0 ? "new_record"
        : record > streak ? "rebuilt"
        : "standard";

    protected override WatchStreakReceivedEvent SampleEvent(DateTimeOffset now) =>
        new()
        {
            UserId = "100000006",
            UserLogin = "streakysam",
            UserDisplayName = "StreakySam",
            StreakMonths = 5,
            ChannelPointsEarned = 450,
            CustomMessage = "Never missing a stream!",
        };

    public async Task HandleAsync(WatchStreakReceivedEvent @event, CancellationToken ct = default)
    {
        await UpsertStreakAsync(@event, ct);
        await HandleCoreAsync(@event, ct);
    }

    private async Task UpsertStreakAsync(WatchStreakReceivedEvent e, CancellationToken ct)
    {
        try
        {
            using IServiceScope scope = ScopeFactory.CreateScope();
            IApplicationDbContext db =
                scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            DateOnly today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
            Guid broadcasterId = e.BroadcasterId;
            if (broadcasterId == Guid.Empty)
                return;

            WatchStreak? existing = await db.WatchStreaks.FirstOrDefaultAsync(
                w => w.BroadcasterId == broadcasterId && w.UserId == e.UserId,
                ct
            );

            _previousMax.AddOrUpdate(e, new(existing?.MaxStreak ?? 0));

            if (existing is null)
            {
                db.WatchStreaks.Add(
                    new()
                    {
                        Id = Guid.NewGuid(),
                        BroadcasterId = broadcasterId,
                        UserId = e.UserId,
                        UserDisplayName = e.UserDisplayName,
                        CurrentStreak = e.StreakMonths,
                        MaxStreak = e.StreakMonths,
                        LastSeenDate = today,
                    }
                );
            }
            else
            {
                existing.UserDisplayName = e.UserDisplayName;
                existing.CurrentStreak = e.StreakMonths;
                if (e.StreakMonths > existing.MaxStreak)
                    existing.MaxStreak = e.StreakMonths;
                existing.LastSeenDate = today;
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "Failed to upsert WatchStreak for user {UserId} in channel {BroadcasterId}",
                e.UserId,
                e.BroadcasterId
            );
        }
    }
}
