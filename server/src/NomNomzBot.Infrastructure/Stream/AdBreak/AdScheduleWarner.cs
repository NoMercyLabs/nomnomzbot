// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Infrastructure.Stream.AdBreak;

/// <summary>
/// Old-bot parity (AdScheduleService): while a channel is live and its broadcaster token has the ads scope, the
/// Helix ad schedule is read once a minute (the old bot's own cadence, because the schedule rarely moves and Twitch
/// is touchy about polling). Between reads the cached next-ad time drives the warnings, so the 10 second threshold
/// is still met on the worker's 5 second tick. Per next-ad slot, each warn threshold (5 minutes, 2 minutes, 60, 30
/// and 10 seconds) goes to the overlays once, and chat hears one warning at about 3 minutes through the
/// <c>channel.ad_break.upcoming</c> event response, so it follows the channel's tone and switch. A new slot (the next
/// ad moved by a snooze or an ad that ran) or a new stream re-arms them all. A channel that is offline, or has no
/// ads scope, is left alone and costs no Helix call.
/// </summary>
public sealed class AdScheduleWarner : IAdScheduleWarner
{
    internal const string UpcomingResponseKey = "channel.ad_break.upcoming";

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ChatWarnThreshold = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan[] WarnThresholds =
    [
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(10),
    ];

    // Only the worker's single loop calls ProcessAsync, so the state needs no lock.
    private readonly Dictionary<Guid, ChannelState> _states = [];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IChannelRegistry _registry;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AdScheduleWarner> _logger;

    public AdScheduleWarner(
        IServiceScopeFactory scopeFactory,
        IChannelRegistry registry,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<AdScheduleWarner> logger
    )
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        List<ChannelContext> live = [.. _registry.GetLiveChannels()];
        HashSet<Guid> liveIds = [.. live.Select(c => c.BroadcasterId)];
        foreach (Guid gone in _states.Keys.Where(id => !liveIds.Contains(id)).ToList())
            _states.Remove(gone);

        if (live.Count == 0)
            return;

        using IServiceScope scope = _scopeFactory.CreateScope();
        ITwitchTokenResolver tokens =
            scope.ServiceProvider.GetRequiredService<ITwitchTokenResolver>();
        ITwitchAdsApi ads = scope.ServiceProvider.GetRequiredService<ITwitchHelixClient>().Ads;
        IEventResponseExecutor executor =
            scope.ServiceProvider.GetRequiredService<IEventResponseExecutor>();

        foreach (ChannelContext channel in live)
        {
            try
            {
                await ProcessChannelAsync(channel, tokens, ads, executor, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Ad schedule warning for channel {Channel} failed",
                    channel.BroadcasterId
                );
            }
        }
    }

    private async Task ProcessChannelAsync(
        ChannelContext channel,
        ITwitchTokenResolver tokens,
        ITwitchAdsApi ads,
        IEventResponseExecutor executor,
        CancellationToken cancellationToken
    )
    {
        if (channel.WentLiveAt is not DateTimeOffset streamStart)
            return;

        if (
            !_states.TryGetValue(channel.BroadcasterId, out ChannelState? state)
            || state.StreamStart != streamStart
        )
        {
            state = new(streamStart);
            _states[channel.BroadcasterId] = state;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (state.NextPollAt <= now)
        {
            state.NextPollAt = now + PollInterval;
            await PollAsync(channel.BroadcasterId, state, now, tokens, ads, cancellationToken);
        }

        await WarnAsync(channel.BroadcasterId, state, now, executor, cancellationToken);
    }

    private async Task PollAsync(
        Guid broadcasterId,
        ChannelState state,
        DateTimeOffset now,
        ITwitchTokenResolver tokens,
        ITwitchAdsApi ads,
        CancellationToken cancellationToken
    )
    {
        state.Schedule = null;
        if (
            !await tokens.HasScopeAsync(
                broadcasterId,
                TwitchScopes.ChannelReadAds,
                cancellationToken
            )
        )
            return;

        Result<TwitchAdSchedule> result = await ads.GetAdScheduleAsync(
            broadcasterId,
            cancellationToken
        );
        if (result.IsFailure)
        {
            _logger.LogDebug(
                "Ad schedule read for channel {Channel} failed: {Error}",
                broadcasterId,
                result.ErrorCode
            );
            return;
        }

        TwitchAdSchedule schedule = result.Value;
        state.Schedule = schedule;
        DateTimeOffset? nextAd = FromUnix(schedule.NextAdAt);
        if (nextAd != state.TrackedNextAd)
        {
            state.TrackedNextAd = nextAd;
            state.FiredThresholds.Clear();
            state.ChatWarned = false;
        }

        await _eventBus.PublishAsync(
            new AdScheduleUpdatedEvent
            {
                BroadcasterId = broadcasterId,
                NextAdAt = nextAd,
                LastAdAt = FromUnix(schedule.LastAdAt),
                DurationSeconds = schedule.Duration,
                PrerollFreeTimeSeconds = schedule.PrerollFreeTime,
                SnoozeCount = schedule.SnoozeCount,
                SnoozeRefreshAt = FromUnix(schedule.SnoozeRefreshAt),
                TimeUntilNextAdSeconds =
                    nextAd > now ? (int)(nextAd.Value - now).TotalSeconds : null,
            },
            cancellationToken
        );
    }

    private async Task WarnAsync(
        Guid broadcasterId,
        ChannelState state,
        DateTimeOffset now,
        IEventResponseExecutor executor,
        CancellationToken cancellationToken
    )
    {
        if (state.Schedule is not { } schedule || state.TrackedNextAd is not DateTimeOffset nextAd)
            return;

        TimeSpan left = nextAd - now;
        if (left <= TimeSpan.Zero)
            return;

        foreach (TimeSpan threshold in WarnThresholds)
        {
            if (left > threshold || !state.FiredThresholds.Add(threshold))
                continue;

            await _eventBus.PublishAsync(
                new AdBreakUpcomingEvent
                {
                    BroadcasterId = broadcasterId,
                    SecondsUntilAd = (int)left.TotalSeconds,
                    ThresholdSeconds = (int)threshold.TotalSeconds,
                    DurationSeconds = schedule.Duration,
                    NextAdAt = nextAd,
                },
                cancellationToken
            );
        }

        if (state.ChatWarned || left > ChatWarnThreshold)
            return;

        // Armed before the send, like the old bot: a failed send is not retried, so chat is never told twice.
        state.ChatWarned = true;
        await executor.ExecuteAsync(
            broadcasterId,
            UpcomingResponseKey,
            null,
            null,
            new()
            {
                ["ad.when"] = WhenText(left),
                ["ad.seconds"] = schedule.Duration.ToString(CultureInfo.InvariantCulture),
            },
            cancellationToken
        );
    }

    /// <summary>The old bot's wording: "in ~3 minutes" from one minute up, else "in ~29 seconds". Minutes round to even, as before.</summary>
    private static string WhenText(TimeSpan left)
    {
        int minutes = (int)Math.Round(left.TotalMinutes);
        return minutes >= 1
            ? $"in ~{minutes} minute{(minutes == 1 ? "" : "s")}"
            : $"in ~{(int)left.TotalSeconds} seconds";
    }

    private static DateTimeOffset? FromUnix(int seconds) =>
        seconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;

    private sealed class ChannelState(DateTimeOffset streamStart)
    {
        public DateTimeOffset StreamStart { get; } = streamStart;
        public DateTimeOffset NextPollAt { get; set; } = DateTimeOffset.MinValue;
        public TwitchAdSchedule? Schedule { get; set; }
        public DateTimeOffset? TrackedNextAd { get; set; }
        public HashSet<TimeSpan> FiredThresholds { get; } = [];
        public bool ChatWarned { get; set; }
    }
}
