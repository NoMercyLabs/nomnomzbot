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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Stream;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Stream;

/// <summary>
/// Stored Stream rows whose end equals the next stream's start (the old online handler's stamp) are moved to
/// the last proof the stream was live. Each test reads the rows back and checks the stored end and the
/// resulting duration.
/// </summary>
public sealed class StreamEndRepairSeederTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192f000-0000-7000-8000-0000000000f1");
    private static readonly Guid Owner = Guid.Parse("0192f000-0000-7000-8000-0000000000f9");
    private static readonly DateTimeOffset First = new(2026, 10, 11, 11, 58, 54, TimeSpan.Zero);
    private static readonly DateTimeOffset Second = new(2026, 10, 13, 11, 58, 19, TimeSpan.Zero);

    private static Channel NewChannel() =>
        new()
        {
            Id = Broadcaster,
            OwnerUserId = Owner,
            Provider = AuthEnums.Platform.Twitch,
            ExternalChannelId = "tw-f1",
            TwitchChannelId = "tw-f1",
            Name = "streamer_f1",
            NameNormalized = "streamer_f1",
            IsOnboarded = true,
            DeploymentMode = AuthEnums.DeploymentMode.Saas,
            BillingTierKey = "free",
        };

    private static Domain.Stream.Entities.Stream Row(
        string id,
        DateTimeOffset start,
        DateTimeOffset? end
    ) =>
        new()
        {
            Id = id,
            ChannelId = Broadcaster,
            StartedAt = start,
            EndedAt = end,
        };

    private static ChatMessage Chat(string id, DateTimeOffset at) =>
        new()
        {
            Id = id,
            BroadcasterId = Broadcaster,
            UserId = "u1",
            Username = "u1",
            DisplayName = "U1",
            UserType = "viewer",
            Message = "hi",
            CreatedAt = at.UtcDateTime,
        };

    private static StreamEndRepairSeeder Sut(AuthDbContext db) =>
        new(db, NullLogger<StreamEndRepairSeeder>.Instance);

    private static async Task<Domain.Stream.Entities.Stream> Reload(AuthDbContext db, string id) =>
        await db.Streams.AsNoTracking().SingleAsync(s => s.Id == id);

    [Fact]
    public async Task A_row_ended_at_the_next_start_gets_the_end_of_its_last_chat_and_a_true_duration()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(NewChannel());
        db.Streams.Add(Row("old", First, Second));
        db.Streams.Add(Row("next", Second, Second.AddHours(7)));
        db.ChatMessages.Add(Chat("a", First.AddHours(1)));
        db.ChatMessages.Add(Chat("b", First.AddHours(6)));
        db.ChatMessages.Add(Chat("after", Second.AddHours(1)));
        await db.SaveChangesAsync();

        await Sut(db).SeedAsync();

        Domain.Stream.Entities.Stream old = await Reload(db, "old");
        old.EndedAt.Should().Be(First.AddHours(6));
        (old.EndedAt - old.StartedAt).Should().Be(TimeSpan.FromHours(6));
        Domain.Stream.Entities.Stream next = await Reload(db, "next");
        next.EndedAt.Should().Be(Second.AddHours(7), "a row with a different end is left alone");
    }

    [Fact]
    public async Task A_later_viewer_sample_beats_an_earlier_chat()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(NewChannel());
        db.Streams.Add(Row("old", First, Second));
        db.Streams.Add(Row("next", Second, null));
        db.ChatMessages.Add(Chat("a", First.AddHours(1)));
        db.EventJournals.Add(
            new EventJournal
            {
                EventId = Guid.NewGuid(),
                BroadcasterId = Broadcaster,
                StreamPosition = 1,
                EventType = "StreamViewerCountSampledEvent",
                EventVersion = 1,
                Source = "domain",
                Payload = "{}",
                Metadata = "{}",
                OccurredAt = First.AddHours(8).UtcDateTime,
                RecordedAt = First.AddHours(8).UtcDateTime,
            }
        );
        await db.SaveChangesAsync();

        await Sut(db).SeedAsync();

        (await Reload(db, "old")).EndedAt.Should().Be(First.AddHours(8));
    }

    [Fact]
    public async Task With_no_activity_the_end_is_the_rows_own_start_and_a_second_run_changes_nothing()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(NewChannel());
        db.Streams.Add(Row("old", First, Second));
        db.Streams.Add(Row("next", Second, Second.AddHours(7)));
        await db.SaveChangesAsync();

        await Sut(db).SeedAsync();
        await Sut(db).SeedAsync();

        Domain.Stream.Entities.Stream old = await Reload(db, "old");
        old.EndedAt.Should().Be(First, "no proof of live time, so no invented length");
        (old.EndedAt - old.StartedAt).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task A_real_end_before_the_next_start_is_not_touched()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(NewChannel());
        DateTimeOffset realEnd = First.AddHours(7).AddMilliseconds(412);
        db.Streams.Add(Row("old", First, realEnd));
        db.Streams.Add(Row("next", Second, null));
        await db.SaveChangesAsync();

        await Sut(db).SeedAsync();

        (await Reload(db, "old")).EndedAt.Should().Be(realEnd);
    }
}
