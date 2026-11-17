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

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Infrastructure.Music;

/// <inheritdoc />
/// <remarks>
/// Held writes live in the <c>MusicHeldWrites</c> table, the source of truth: a row is inserted when a
/// write is held and deleted once the provider accepts it, so a restart cannot drop a held write.
/// </remarks>
public sealed class MusicWriteQueue(
    IServiceScopeFactory scopes,
    IYouTubeQuotaCooldown quota,
    TimeProvider clock,
    ILogger<MusicWriteQueue> logger
) : IMusicWriteQueue
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _flushing = new(1, 1);
    private readonly List<(DateTimeOffset At, int Units)> _youTubeSpend = [];
    private DateTimeOffset _spendDayEnds = DateTimeOffset.MinValue;

    public async Task<Result<MusicWriteOutcome>> SubmitAsync(
        MusicWriteRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (ShouldHold(request))
        {
            Result stored = await StoreHeldAsync(request, cancellationToken);
            if (stored.IsFailure)
                return Result.Failure<MusicWriteOutcome>(
                    stored.ErrorMessage,
                    stored.ErrorCode,
                    stored.ErrorDetail
                );

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
        await _flushing.WaitAsync(cancellationToken);
        try
        {
            List<MusicHeldWrite> rows = await LoadHeldAsync(broadcasterId, cancellationToken);
            int sent = 0;
            foreach (MusicHeldWrite row in rows)
                if (await FlushRowAsync(row, cancellationToken))
                    sent++;

            return sent;
        }
        finally
        {
            _flushing.Release();
        }
    }

    public async Task<int> FlushAllAsync(CancellationToken cancellationToken = default)
    {
        List<Guid> broadcasterIds;
        using (IServiceScope scope = scopes.CreateScope())
        {
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // IgnoreQueryFilters: the startup flush has no ambient tenant and must see every channel.
            broadcasterIds = await db
                .MusicHeldWrites.IgnoreQueryFilters()
                .Select(r => r.BroadcasterId)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        int sent = 0;
        foreach (Guid broadcasterId in broadcasterIds)
            sent += await FlushAsync(broadcasterId, cancellationToken);

        return sent;
    }

    public void Charge(string provider, int units)
    {
        if (provider == MusicWriteBudget.YouTube)
            RecordYouTubeSpend(units);
    }

    private async Task<Result> StoreHeldAsync(MusicWriteRequest request, CancellationToken ct)
    {
        try
        {
            using IServiceScope scope = scopes.CreateScope();
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.MusicHeldWrites.Add(
                new()
                {
                    BroadcasterId = request.BroadcasterId,
                    Provider = request.Provider,
                    Kind = request.Kind.ToString(),
                    TrackUrisJson = JsonSerializer.Serialize(request.TrackUris),
                    PlaylistId = request.PlaylistId,
                    CreatedAt = clock.GetUtcNow().UtcDateTime,
                }
            );
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(
                ex,
                "Could not store a held music write for {BroadcasterId}",
                request.BroadcasterId
            );
            return Result.Failure("The write could not be held.", "SERVICE_UNAVAILABLE");
        }
    }

    private async Task<List<MusicHeldWrite>> LoadHeldAsync(Guid broadcasterId, CancellationToken ct)
    {
        using IServiceScope scope = scopes.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db
            .MusicHeldWrites.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.BroadcasterId == broadcasterId)
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .ToListAsync(ct);
    }

    private async Task<bool> FlushRowAsync(MusicHeldWrite row, CancellationToken ct)
    {
        if (IsCooling(row.Provider))
            return false;

        Result result = await SendAsync(ToRequest(row), ct);
        if (result.IsFailure)
        {
            logger.LogWarning(
                "A held {Kind} write on {Provider} for {BroadcasterId} failed at flush: {Code}",
                row.Kind,
                row.Provider,
                row.BroadcasterId,
                result.ErrorCode
            );
            return false;
        }

        using IServiceScope scope = scopes.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db
            .MusicHeldWrites.IgnoreQueryFilters()
            .Where(r => r.Id == row.Id)
            .ExecuteDeleteAsync(ct);
        return true;
    }

    private static MusicWriteRequest ToRequest(MusicHeldWrite row) =>
        new(
            row.BroadcasterId,
            row.Provider,
            Enum.Parse<MusicWriteKind>(row.Kind),
            JsonSerializer.Deserialize<List<string>>(row.TrackUrisJson) ?? [],
            row.PlaylistId
        );

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
