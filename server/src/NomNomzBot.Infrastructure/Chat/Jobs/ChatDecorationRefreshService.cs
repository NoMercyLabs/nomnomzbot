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

namespace NomNomzBot.Infrastructure.Chat.Jobs;

/// <summary>
/// Keeps the third-party emote cache warm so the decoration pipeline only ever reads cache on the chat hot path
/// (chat-decoration spec §3.6). Warms every provider's GLOBAL set on startup and then every 6 h. Per-iteration
/// failures are logged and retried next tick — they never tear the worker (or the host) down. Auto-discovered by
/// <c>AddHostedWorkers</c>.
/// </summary>
public sealed class ChatDecorationRefreshService : BackgroundService
{
    private static readonly TimeSpan GlobalRefreshInterval = TimeSpan.FromHours(6);

    private readonly ChatEmoteCacheWarmer _warmer;
    private readonly ILogger<ChatDecorationRefreshService> _logger;

    public ChatDecorationRefreshService(
        ChatEmoteCacheWarmer warmer,
        ILogger<ChatDecorationRefreshService> logger
    )
    {
        _warmer = warmer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial warm right after host start (non-blocking — ExecuteAsync runs post-startup).
        await WarmGlobalsAsync(stoppingToken);

        using PeriodicTimer timer = new(GlobalRefreshInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await WarmGlobalsAsync(stoppingToken);
    }

    private async Task WarmGlobalsAsync(CancellationToken ct)
    {
        try
        {
            int warmed = await _warmer.WarmGlobalAsync(ct);
            _logger.LogDebug("Refreshed {Count} global third-party emote set(s).", warmed);
        }
        catch (OperationCanceledException)
        {
            // host is shutting down — let the loop's WaitForNextTickAsync observe the cancellation and exit.
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Global emote refresh iteration failed; retrying at the next interval."
            );
        }
    }
}
