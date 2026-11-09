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

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Interfaces;

namespace NomNomzBot.Infrastructure.Music.Playback;

/// <summary>
/// Runs one channel's playback deadlines every second, so a dead Music Player page gives the music back
/// with no poll, report or user input to drive it. A standby instance leaves the deadlines to the active one.
/// </summary>
internal sealed class PlaybackDeadlineTimer : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private readonly Guid _channelId;
    private readonly PlaybackDirector _director;
    private readonly IActiveInstanceGate? _instanceGate;
    private readonly ILogger? _logger;
    private readonly ITimer _timer;
    private int _ticking;

    public PlaybackDeadlineTimer(
        Guid channelId,
        PlaybackDirector director,
        TimeProvider time,
        IActiveInstanceGate? instanceGate,
        ILogger? logger
    )
    {
        _channelId = channelId;
        _director = director;
        _instanceGate = instanceGate;
        _logger = logger;

        // The first input creates this timer; it must not keep that request's context for its whole life.
        using AsyncFlowControl? noFlow = ExecutionContext.IsFlowSuppressed()
            ? null
            : ExecutionContext.SuppressFlow();
        _timer = time.CreateTimer(_ => _ = TickAsync(), null, TickInterval, TickInterval);
    }

    public void Dispose() => _timer.Dispose();

    private async Task TickAsync()
    {
        if (_instanceGate is { IsActiveInstance: false })
            return;
        if (Interlocked.CompareExchange(ref _ticking, 1, 0) != 0)
            return;

        try
        {
            await _director.CheckDeadlinesAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Playback deadline check failed for channel {C}", _channelId);
        }
        finally
        {
            Volatile.Write(ref _ticking, 0);
        }
    }
}
