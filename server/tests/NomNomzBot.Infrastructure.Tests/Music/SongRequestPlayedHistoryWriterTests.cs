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
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music;
using RequestRecord = NomNomzBot.Domain.Platform.Entities.Record;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>Stoney, 2026-11-08: a song counts for the leaderboard only when it has actually played.</summary>
public sealed class SongRequestPlayedHistoryWriterTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000bb02");
    private static readonly DateTimeOffset PlayedAt = new(2026, 11, 8, 20, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static RequestRecord Pending(string userId, string itemId, string uri) =>
        new()
        {
            BroadcasterId = Channel,
            UserId = userId,
            RecordType = SongRequestHistory.PendingRecordType,
            Data = JsonSerializer.Serialize(
                new SongRequestHistory(uri, "Song", "Artist", null, "spotify", itemId),
                Json
            ),
        };

    private static SongRequestPlayedEvent Played(string itemId, string uri) =>
        new()
        {
            BroadcasterId = Channel,
            OccurredAt = PlayedAt,
            ItemId = itemId,
            TrackUri = uri,
        };

    private static SongRequestPlayedHistoryWriter Writer(MusicTestDbContext db) =>
        new(db, NullLogger<SongRequestPlayedHistoryWriter>.Instance);

    [Fact]
    public async Task The_played_request_becomes_a_counted_row_dated_at_the_play()
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        db.Records.Add(Pending("42", "K7QM", "spotify:track:a"));
        await db.SaveChangesAsync();

        await Writer(db).HandleAsync(Played("K7QM", "spotify:track:a"));

        RequestRecord row = await db.Records.SingleAsync();
        row.RecordType.Should().Be(SongRequestHistory.RecordType);
        row.UserId.Should().Be("42");
        row.CreatedAt.Should().Be(PlayedAt.UtcDateTime);
        SongRequestHistory history = JsonSerializer.Deserialize<SongRequestHistory>(
            row.Data,
            Json
        )!;
        history.TrackUri.Should().Be("spotify:track:a");
        history.SourceRef.Should().BeNull();
    }

    [Fact]
    public async Task A_request_that_did_not_play_stays_uncounted()
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        db.Records.Add(Pending("42", "K7QM", "spotify:track:a"));
        db.Records.Add(Pending("99", "ZZ11", "spotify:track:b"));
        await db.SaveChangesAsync();

        await Writer(db).HandleAsync(Played("K7QM", "spotify:track:a"));

        RequestRecord waiting = await db.Records.SingleAsync(r => r.UserId == "99");
        waiting.RecordType.Should().Be(SongRequestHistory.PendingRecordType);
    }

    [Fact]
    public async Task A_second_play_of_the_same_row_counts_once()
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        db.Records.Add(Pending("42", "K7QM", "spotify:track:a"));
        await db.SaveChangesAsync();

        await Writer(db).HandleAsync(Played("K7QM", "spotify:track:a"));
        await Writer(db).HandleAsync(Played("K7QM", "spotify:track:a"));

        (await db.Records.CountAsync(r => r.RecordType == SongRequestHistory.RecordType))
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task A_reused_code_on_another_track_does_not_count_the_old_row()
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        db.Records.Add(Pending("42", "K7QM", "spotify:track:removed"));
        await db.SaveChangesAsync();

        await Writer(db).HandleAsync(Played("K7QM", "spotify:track:other"));

        (await db.Records.SingleAsync())
            .RecordType.Should()
            .Be(SongRequestHistory.PendingRecordType);
    }
}
