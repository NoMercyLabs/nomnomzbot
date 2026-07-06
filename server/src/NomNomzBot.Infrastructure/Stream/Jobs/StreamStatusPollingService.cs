// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Stream.Jobs;

/// <summary>
/// Reconciles every registered channel's live status from Helix Get Streams on a fixed cadence, so the dashboard
/// shows LIVE / offline correctly even when the EventSub <c>stream.online</c> transition never arrives. On the
/// WebSocket transport <c>stream.online</c> is a cost-1 public topic that Twitch drops once the per-token cost cap
/// is hit (twitch-eventsub.md §3.3 / the deferred conduit item), so a channel that goes live AFTER the bot
/// subscribed would otherwise stay "offline" in the registry until the process restarts — the reported bug
/// (a live channel showing offline on the dashboard). This poll is the backstop: it keeps
/// <see cref="ChannelContext.IsLive"/> + <see cref="Channel.IsLive"/> (and title / game) fresh from the
/// authoritative Helix read.
/// <para>
/// It is a status RECONCILER only — it never creates <c>Stream</c> records or runs <c>stream_online</c>
/// event-response pipelines (those stay the EventSub path's job in <c>ChannelOnlineHandler</c>), so a poll-detected
/// transition can never double-fire a "we're live!" announcement. Helix Get Streams runs on the platform bot (app)
/// token, so the poll stays dormant until onboarding completes — gated on <see cref="IPlatformBotReadinessGate"/>,
/// re-checked each tick so it activates without a restart. Auto-discovered by <c>AddHostedWorkers</c>.
/// </para>
/// </summary>
public sealed class StreamStatusPollingService : BackgroundService
{
    // Fresh enough for a "live now" indicator without hammering Helix; Get Streams is a cheap app-token read and
    // the cost cap that motivates this poll only bites the EventSub public topics, never this REST call.
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);

    private readonly IChannelRegistry _channels;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StreamStatusPollingService> _logger;

    // Latches the "waiting for onboarding" log so the dormant path logs once, not on every tick.
    private int _waitingLogged;

    public StreamStatusPollingService(
        IChannelRegistry channels,
        IServiceScopeFactory scopeFactory,
        ILogger<StreamStatusPollingService> logger
    )
    {
        _channels = channels;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Self-priming: reconcile immediately on startup (covers streams already live before we subscribed),
            // then on every interval tick until the host stops.
            await PollAllAsync(stoppingToken);

            using PeriodicTimer timer = new(PollInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await PollAllAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down — end the loop quietly.
        }
    }

    private async Task PollAllAsync(CancellationToken ct)
    {
        IReadOnlyCollection<ChannelContext> channels = _channels.GetAll();
        if (channels.Count == 0)
            return;

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();

            IPlatformBotReadinessGate gate =
                scope.ServiceProvider.GetRequiredService<IPlatformBotReadinessGate>();
            if (!await gate.IsPlatformBotConfiguredAsync(ct))
            {
                if (Interlocked.Exchange(ref _waitingLogged, 1) == 0)
                    _logger.LogInformation(
                        "Stream status poll: waiting for onboarding before polling Helix."
                    );
                return;
            }
            Interlocked.Exchange(ref _waitingLogged, 0);

            ITwitchStreamsApi streams =
                scope.ServiceProvider.GetRequiredService<ITwitchStreamsApi>();
            IApplicationDbContext db =
                scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            int changed = 0;
            foreach (ChannelContext ctx in channels)
            {
                try
                {
                    Channel? dbChannel = await db.Channels.FindAsync([ctx.BroadcasterId], ct);
                    if (dbChannel is null)
                        continue;

                    Result<TwitchStream> result = await streams.GetStreamAsync(
                        ctx.BroadcasterId,
                        ct
                    );
                    if (ApplyStreamState(ctx, dbChannel, result))
                        changed++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(
                        ex,
                        "Stream status poll failed for channel {BroadcasterId}; retrying next tick.",
                        ctx.BroadcasterId
                    );
                }
            }

            if (changed > 0)
            {
                await db.SaveChangesAsync(ct);
                _logger.LogDebug("Stream status poll reconciled {Count} channel(s).", changed);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Stream status poll iteration failed; retrying at the next interval."
            );
        }
    }

    /// <summary>
    /// Reconciles one channel's live state from a Helix Get Streams result into both the in-memory
    /// <paramref name="ctx"/> (what the dashboard reads) and the persisted <paramref name="dbChannel"/>. An empty
    /// Helix result (<c>IsFailure</c>) means offline — a stream appears in <c>data[]</c> only while live. Returns
    /// true when a persisted field (IsLive / Title / GameName) changed, so the caller saves once per cycle.
    /// </summary>
    internal static bool ApplyStreamState(
        ChannelContext ctx,
        Channel dbChannel,
        Result<TwitchStream> result
    )
    {
        bool wasLive = ctx.IsLive;
        bool isLive = result.IsSuccess;
        bool changed = dbChannel.IsLive != isLive;

        ctx.IsLive = isLive;
        dbChannel.IsLive = isLive;

        if (isLive)
        {
            TwitchStream stream = result.Value;
            if (!string.IsNullOrEmpty(stream.Title) && dbChannel.Title != stream.Title)
            {
                ctx.CurrentTitle = stream.Title;
                dbChannel.Title = stream.Title;
                changed = true;
            }
            if (!string.IsNullOrEmpty(stream.GameName) && dbChannel.GameName != stream.GameName)
            {
                ctx.CurrentGame = stream.GameName;
                dbChannel.GameName = stream.GameName;
                changed = true;
            }
            // Rising edge (offline → live): anchor the uptime clock the dashboard reads.
            if (!wasLive)
                ctx.WentLiveAt = stream.StartedAt;
        }
        else if (wasLive)
        {
            // Falling edge (live → offline): the uptime anchor is no longer meaningful.
            ctx.WentLiveAt = null;
        }

        return changed;
    }
}
