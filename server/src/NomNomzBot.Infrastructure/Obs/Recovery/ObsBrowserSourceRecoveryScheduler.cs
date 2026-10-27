// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Services;

namespace NomNomzBot.Infrastructure.Obs.Recovery;

/// <summary>
/// Debounces recovery passes per channel (see <see cref="IObsBrowserSourceRecoveryScheduler"/>). A singleton:
/// the pending timers must outlive the scoped event handler that asked for them.
/// </summary>
public sealed class ObsBrowserSourceRecoveryScheduler : IObsBrowserSourceRecoveryScheduler
{
    /// <summary>How long a page gets to reconnect on its own before the bot reloads it.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(8);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<ObsBrowserSourceRecoveryScheduler> _logger;

    public ObsBrowserSourceRecoveryScheduler(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<ObsBrowserSourceRecoveryScheduler> logger
    )
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _pending = new();

    public void Schedule(Guid broadcasterId)
    {
        CancellationTokenSource source = new();
        _pending.AddOrUpdate(
            broadcasterId,
            source,
            (_, previous) =>
            {
                previous.Cancel();
                return source;
            }
        );

        // Not Task.Run: the delay timer must exist before this call returns, so the window starts now.
        _ = RunAsync(broadcasterId, source);
    }

    private async Task RunAsync(Guid broadcasterId, CancellationTokenSource source)
    {
        try
        {
            await Task.Delay(Delay, _clock, source.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _pending.TryRemove(new KeyValuePair<Guid, CancellationTokenSource>(broadcasterId, source));

        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            IObsBrowserSourceRecoveryService recovery =
                scope.ServiceProvider.GetRequiredService<IObsBrowserSourceRecoveryService>();
            Result<IReadOnlyList<string>> pass = await recovery.RecoverAsync(
                broadcasterId,
                CancellationToken.None
            );
            if (pass.IsFailure)
                _logger.LogWarning(
                    "OBS browser source recovery could not run for channel {ChannelId}: {Error}. A widget that has no connection stays dead until OBS reloads it or the source is refreshed by hand.",
                    broadcasterId,
                    pass.ErrorMessage
                );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "OBS browser source recovery failed for channel {ChannelId}",
                broadcasterId
            );
        }
    }
}
