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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Turns a request's pending history row into a counted one when the song plays. The row keeps the
/// requester written at request time, and its date becomes the play, so a stream's recap counts the songs
/// that played in that stream. A replay has no pending row left, so it never counts twice.
/// </summary>
public sealed class SongRequestPlayedHistoryWriter : IEventHandler<SongRequestPlayedEvent>
{
    /// <summary>How many of the channel's newest pending rows to look through.</summary>
    private const int PendingScanLimit = 500;

    private static readonly JsonSerializerOptions HistoryJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IApplicationDbContext _db;
    private readonly ILogger<SongRequestPlayedHistoryWriter> _logger;

    public SongRequestPlayedHistoryWriter(
        IApplicationDbContext db,
        ILogger<SongRequestPlayedHistoryWriter> logger
    )
    {
        _db = db;
        _logger = logger;
    }

    public async Task HandleAsync(
        SongRequestPlayedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            await CountAsPlayedAsync(@event, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Could not count played song request {ItemId} on {BroadcasterId}",
                @event.ItemId,
                @event.BroadcasterId
            );
        }
    }

    private async Task CountAsPlayedAsync(
        SongRequestPlayedEvent @event,
        CancellationToken cancellationToken
    )
    {
        List<Domain.Platform.Entities.Record> pending = await _db
            .Records.Where(r =>
                r.BroadcasterId == @event.BroadcasterId
                && r.RecordType == SongRequestHistory.PendingRecordType
            )
            .OrderByDescending(r => r.Id)
            .Take(PendingScanLimit)
            .ToListAsync(cancellationToken);

        foreach (Domain.Platform.Entities.Record row in pending)
        {
            SongRequestHistory? history = JsonSerializer.Deserialize<SongRequestHistory>(
                row.Data,
                HistoryJson
            );
            if (history?.SourceRef != @event.ItemId || history.TrackUri != @event.TrackUri)
                continue;

            row.RecordType = SongRequestHistory.RecordType;
            row.Data = JsonSerializer.Serialize(history with { SourceRef = null }, HistoryJson);
            row.CreatedAt = @event.OccurredAt.UtcDateTime;
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }
    }
}
