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
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Chat.Jobs;

/// <summary>
/// Keeps the decoration caches warm so the pipeline only ever reads cache on the chat hot path (chat-decoration spec
/// §3.6). Two cadences run concurrently: GLOBAL sets (third-party emotes + Helix badges) on startup then every 6 h, and
/// every live channel's sets on startup then every 5 min (the staleness window for a newly-added emote). Per-iteration
/// failures are logged and retried next tick — they never tear the worker (or the host) down. Auto-discovered by
/// <c>AddHostedWorkers</c>; a channel going live is warmed immediately by <c>StreamWentLiveEmoteWarmer</c>. The badge
/// warmer is scoped (it uses the scoped Helix client), so it is resolved inside a per-iteration scope.
/// </summary>
public sealed class ChatDecorationRefreshService : BackgroundService
{
    private static readonly TimeSpan GlobalRefreshInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan ChannelRefreshInterval = TimeSpan.FromMinutes(5);

    private readonly ChatEmoteCacheWarmer _warmer;
    private readonly IChannelRegistry _channels;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChatDecorationRefreshService> _logger;

    public ChatDecorationRefreshService(
        ChatEmoteCacheWarmer warmer,
        IChannelRegistry channels,
        IServiceScopeFactory scopeFactory,
        ILogger<ChatDecorationRefreshService> logger
    )
    {
        _warmer = warmer;
        _channels = channels;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(
            RunLoopAsync(GlobalRefreshInterval, WarmGlobalsAsync, stoppingToken),
            RunLoopAsync(ChannelRefreshInterval, WarmLiveChannelsAsync, stoppingToken)
        );

    // One self-priming loop: warm immediately, then on every interval tick until the host stops.
    private static async Task RunLoopAsync(
        TimeSpan interval,
        Func<CancellationToken, Task> warm,
        CancellationToken stoppingToken
    )
    {
        try
        {
            await warm(stoppingToken);

            using PeriodicTimer timer = new(interval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await warm(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // host is shutting down — end the loop quietly.
        }
    }

    private async Task WarmGlobalsAsync(CancellationToken ct)
    {
        try
        {
            int warmed = await _warmer.WarmGlobalAsync(ct);
            _logger.LogDebug("Refreshed {Count} global third-party emote set(s).", warmed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Global emote refresh iteration failed; retrying at the next interval."
            );
        }

        await WarmBadgesAsync(badges => badges.WarmGlobalAsync(ct), "global", ct);
    }

    private async Task WarmLiveChannelsAsync(CancellationToken ct)
    {
        IReadOnlyCollection<ChannelContext> live = _channels.GetLiveChannels();

        try
        {
            foreach (ChannelContext channel in live)
                await _warmer.WarmChannelAsync(channel.TwitchChannelId, channel.ChannelName, ct);

            if (live.Count > 0)
                _logger.LogDebug(
                    "Refreshed channel emote sets for {Count} live channel(s).",
                    live.Count
                );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Live-channel emote refresh iteration failed; retrying at the next interval."
            );
        }

        await WarmBadgesAsync(
            async badges =>
            {
                foreach (ChannelContext channel in live)
                    await badges.WarmChannelAsync(channel.BroadcasterId, ct);
            },
            "live-channel",
            ct
        );
    }

    // Resolves the scoped badge warmer inside its own scope (it uses the scoped Helix client) and runs the given work.
    private async Task WarmBadgesAsync(
        Func<ChatBadgeCacheWarmer, Task> work,
        string scope,
        CancellationToken ct
    )
    {
        try
        {
            using IServiceScope serviceScope = _scopeFactory.CreateScope();
            ChatBadgeCacheWarmer badges =
                serviceScope.ServiceProvider.GetRequiredService<ChatBadgeCacheWarmer>();
            await work(badges);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Badge refresh iteration ({Scope}) failed; retrying at the next interval.",
                scope
            );
        }
    }
}
