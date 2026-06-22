// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Chat.Jobs;

/// <summary>
/// Keeps the third-party emote cache warm so the decoration pipeline only ever reads cache on the chat hot path
/// (chat-decoration spec §3.6). Two cadences run concurrently: every provider's GLOBAL set on startup then every 6 h,
/// and every live channel's CHANNEL set on startup then every 5 min (the staleness window for a newly-added emote).
/// Per-iteration failures are logged and retried next tick — they never tear the worker (or the host) down.
/// Auto-discovered by <c>AddHostedWorkers</c>; a channel going live is warmed immediately by <c>StreamWentLiveEmoteWarmer</c>.
/// </summary>
public sealed class ChatDecorationRefreshService : BackgroundService
{
    private static readonly TimeSpan GlobalRefreshInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan ChannelRefreshInterval = TimeSpan.FromMinutes(5);

    private readonly ChatEmoteCacheWarmer _warmer;
    private readonly IChannelRegistry _channels;
    private readonly ILogger<ChatDecorationRefreshService> _logger;

    public ChatDecorationRefreshService(
        ChatEmoteCacheWarmer warmer,
        IChannelRegistry channels,
        ILogger<ChatDecorationRefreshService> logger
    )
    {
        _warmer = warmer;
        _channels = channels;
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
    }

    private async Task WarmLiveChannelsAsync(CancellationToken ct)
    {
        try
        {
            int channels = 0;
            foreach (ChannelContext channel in _channels.GetLiveChannels())
            {
                await _warmer.WarmChannelAsync(channel.TwitchChannelId, channel.ChannelName, ct);
                channels++;
            }

            if (channels > 0)
                _logger.LogDebug(
                    "Refreshed channel emote sets for {Count} live channel(s).",
                    channels
                );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Live-channel emote refresh iteration failed; retrying at the next interval."
            );
        }
    }
}
