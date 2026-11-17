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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Infrastructure.Music;

/// <inheritdoc />
/// <remarks>
/// Held writes live in memory only: a restart drops them. Persisting them needs a new table in both
/// migration sets, which is not cheap enough for this slice.
/// </remarks>
public sealed class MusicWriteQueue(
    IServiceScopeFactory scopes,
    IYouTubeQuotaCooldown quota,
    TimeProvider clock,
    ILogger<MusicWriteQueue> logger
) : IMusicWriteQueue
{
    private readonly object _gate = new();
    private readonly List<MusicWriteRequest> _held = [];
    private readonly List<(DateTimeOffset At, int Units)> _youTubeSpend = [];
    private DateTimeOffset _spendDayEnds = DateTimeOffset.MinValue;

    public async Task<Result<MusicWriteOutcome>> SubmitAsync(
        MusicWriteRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (ShouldHold(request))
        {
            lock (_gate)
                _held.Add(request);
            return Result.Success(MusicWriteOutcome.Held);
        }

        Result sent = await SendAsync(request, cancellationToken);
        if (sent.IsFailure)
            return Result.Failure<MusicWriteOutcome>(
                sent.ErrorMessage,
                sent.ErrorCode,
                sent.ErrorDetail
            );

        return Result.Success(MusicWriteOutcome.Sent);
    }

    public async Task<int> FlushAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<MusicWriteRequest> mine;
        lock (_gate)
        {
            mine = _held.Where(r => r.BroadcasterId == broadcasterId).ToList();
            _held.RemoveAll(r => r.BroadcasterId == broadcasterId);
        }

        int sent = 0;
        foreach (MusicWriteRequest request in mine)
        {
            if (IsCooling(request.Provider))
            {
                lock (_gate)
                    _held.Add(request);
                continue;
            }

            Result result = await SendAsync(request, cancellationToken);
            if (result.IsSuccess)
                sent++;
            else
                logger.LogWarning(
                    "A held {Kind} write on {Provider} for {BroadcasterId} failed at flush: {Code}",
                    request.Kind,
                    request.Provider,
                    broadcasterId,
                    result.ErrorCode
                );
        }

        return sent;
    }

    public void Charge(string provider, int units)
    {
        if (provider == MusicWriteBudget.YouTube)
            RecordYouTubeSpend(units);
    }

    private bool ShouldHold(MusicWriteRequest request)
    {
        if (request.Provider != MusicWriteBudget.YouTube)
            return false;

        if (IsCooling(request.Provider))
            return true;

        lock (_gate)
        {
            DateTimeOffset now = clock.GetUtcNow();
            ResetSpendWhenDayEnded(now);
            int usedToday = _youTubeSpend.Sum(s => s.Units);
            int recent = _youTubeSpend
                .Where(s => now - s.At <= MusicWriteBudget.RecentWindow)
                .Sum(s => s.Units);
            double perMinute = recent / MusicWriteBudget.RecentWindow.TotalMinutes;
            double predicted =
                usedToday
                + MusicWriteBudget.CostOf(request)
                + perMinute * MusicWriteBudget.PredictionHorizon.TotalMinutes;
            return predicted > MusicWriteBudget.WriteBudgetUnits;
        }
    }

    private bool IsCooling(string provider) =>
        provider == MusicWriteBudget.YouTube && quota.TryGetCoolingUntil(out _);

    private async Task<Result> SendAsync(MusicWriteRequest request, CancellationToken ct)
    {
        using IServiceScope scope = scopes.CreateScope();
        IMusicProviderManageApi api =
            scope.ServiceProvider.GetRequiredService<IMusicProviderManageApi>();

        Result result = request.Kind switch
        {
            MusicWriteKind.Save => await api.SaveTracksAsync(
                request.BroadcasterId,
                request.Provider,
                request.TrackUris,
                ct
            ),
            MusicWriteKind.Unsave => await api.RemoveSavedTracksAsync(
                request.BroadcasterId,
                request.Provider,
                request.TrackUris,
                ct
            ),
            _ => await api.AddPlaylistTracksAsync(
                request.BroadcasterId,
                request.Provider,
                request.PlaylistId ?? string.Empty,
                request.TrackUris,
                ct
            ),
        };

        if (result.IsSuccess && request.Provider == MusicWriteBudget.YouTube)
            RecordYouTubeSpend(MusicWriteBudget.CostOf(request));

        return result;
    }

    private void RecordYouTubeSpend(int units)
    {
        lock (_gate)
        {
            DateTimeOffset now = clock.GetUtcNow();
            ResetSpendWhenDayEnded(now);
            _youTubeSpend.Add((now, units));
        }
    }

    private void ResetSpendWhenDayEnded(DateTimeOffset now)
    {
        if (now < _spendDayEnds)
            return;

        _youTubeSpend.Clear();
        _spendDayEnds = YouTubeQuotaCooldown.NextPacificMidnight(now);
    }
}

/// <summary>Sends a channel's held favorite and banger writes the moment its stream ends.</summary>
public sealed class MusicWriteQueueStreamOfflineHandler(IMusicWriteQueue queue)
    : IEventHandler<ChannelOfflineEvent>
{
    public async Task HandleAsync(
        ChannelOfflineEvent @event,
        CancellationToken cancellationToken = default
    ) => await queue.FlushAsync(@event.BroadcasterId, cancellationToken);
}
