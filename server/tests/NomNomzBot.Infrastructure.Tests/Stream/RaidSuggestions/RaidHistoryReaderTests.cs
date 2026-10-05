// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Newtonsoft.Json;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Raids;
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Infrastructure.Stream.RaidSuggestions;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Stream.RaidSuggestions;

/// <summary>
/// The raid history comes from journaled EventSub <c>channel.raid</c> rows, the only place outgoing raids are
/// kept. Each test seeds real-shaped payloads and asserts the counts and times per remote channel and direction.
/// </summary>
public sealed class RaidHistoryReaderTests
{
    private const string Us = "100";
    private const string X = "200";
    private const string Y = "300";
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000abc01");
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-0000000abc02");
    private static readonly DateTime T0 = new(2026, 9, 1, 20, 0, 0, DateTimeKind.Utc);
    private static long _position;

    private static EventJournal Row(
        string from,
        string to,
        DateTime at,
        Guid? channel = null,
        string type = "channel.raid",
        bool encrypted = false
    ) =>
        new()
        {
            EventId = Guid.NewGuid(),
            BroadcasterId = channel ?? Channel,
            StreamPosition = Interlocked.Increment(ref _position),
            EventType = type,
            EventVersion = 1,
            Source = "eventsub",
            Payload = encrypted
                ? "ciphertext"
                : JsonConvert.SerializeObject(
                    new Dictionary<string, object>
                    {
                        ["from_broadcaster_user_id"] = from,
                        ["to_broadcaster_user_id"] = to,
                        ["viewers"] = 12,
                    }
                ),
            PayloadIsEncrypted = encrypted,
            Metadata = "{}",
            OccurredAt = at,
            RecordedAt = at,
        };

    private static async Task<RaidHistory> ReadAsync(params EventJournal[] rows)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.EventJournals.AddRange(rows);
        await db.SaveChangesAsync();
        Result<RaidHistory> result = await new RaidHistoryReader(db).GetHistoryAsync(Channel, Us);
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        return result.Value;
    }

    private static DateTimeOffset At(int days) => new(T0.AddDays(days), TimeSpan.Zero);

    [Fact]
    public async Task Counts_each_direction_per_remote_user()
    {
        RaidHistory history = await ReadAsync(
            Row(Us, X, T0),
            Row(Us, X, T0.AddDays(3)),
            Row(X, Us, T0.AddDays(1)),
            Row(Y, Us, T0.AddDays(5)),
            Row(Y, Us, T0.AddDays(2)),
            Row(Y, Us, T0.AddDays(4))
        );

        history.Outgoing.Keys.Should().BeEquivalentTo(X);
        history.Outgoing[X].Should().Be(new RaidStats(2, At(3)));
        history.Incoming.Keys.Should().BeEquivalentTo(X, Y);
        history.Incoming[X].Should().Be(new RaidStats(1, At(1)));
        history.Incoming[Y].Should().Be(new RaidStats(3, At(5)));
    }

    [Fact]
    public async Task Ignores_other_channels_other_types_and_encrypted_rows()
    {
        RaidHistory history = await ReadAsync(
            Row(Us, X, T0),
            Row(Us, Y, T0, channel: OtherChannel),
            Row(Us, Y, T0, type: "channel.follow"),
            Row(Us, Y, T0, encrypted: true)
        );

        history.Outgoing.Keys.Should().BeEquivalentTo(X);
        history.Incoming.Should().BeEmpty();
    }

    [Fact]
    public async Task Skips_a_row_whose_payload_is_not_a_raid_shape()
    {
        EventJournal broken = Row(Us, X, T0);
        broken.Payload = "{}";

        RaidHistory history = await ReadAsync(broken, Row(X, Us, T0.AddDays(1)));

        history.Outgoing.Should().BeEmpty();
        history.Incoming[X].Count.Should().Be(1);
    }
}
