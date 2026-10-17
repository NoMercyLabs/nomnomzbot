// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Infrastructure.Dashboard;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using Record = NomNomzBot.Domain.Platform.Entities.Record;

namespace NomNomzBot.Infrastructure.Tests.Dashboard;

/// <summary>
/// The stream recap counts recorded facts inside the stream window. Every test seeds real rows, some inside and
/// some outside the window, and asserts the exact numbers and shapes the DTO carries.
/// </summary>
public sealed class StreamRecapServiceTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192f000-0000-7000-8000-0000000000f1");
    private static readonly Guid OtherBroadcaster = Guid.Parse(
        "0192f000-0000-7000-8000-0000000000f2"
    );
    private static readonly DateTimeOffset Started = new(2026, 10, 16, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Ended = new(2026, 10, 16, 21, 30, 0, TimeSpan.Zero);

    private static Domain.Stream.Entities.Stream NewStream(
        string id,
        DateTimeOffset startedAt,
        DateTimeOffset? endedAt
    ) =>
        new()
        {
            Id = id,
            ChannelId = Broadcaster,
            StartedAt = startedAt,
            EndedAt = endedAt,
            PeakViewers = 87,
            Title = "Celeste night",
            GameName = "Celeste",
        };

    private static ChannelEvent Fact(Guid channel, string type, DateTimeOffset at, object data)
    {
        JsonObject json = JsonSerializer.SerializeToNode(data)!.AsObject();
        json["occurredAt"] = at.UtcDateTime.ToString("O");
        return new()
        {
            Id = Guid.NewGuid().ToString(),
            ChannelId = channel,
            Type = type,
            CreatedAt = at.UtcDateTime,
            Data = json.ToJsonString(),
        };
    }

    private static StreamRecapService NewService(AuthDbContext db, DateTimeOffset now)
    {
        TimeProvider clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(now);
        return new StreamRecapService(db, clock);
    }

    private static User Viewer(string twitchId, string name) =>
        new()
        {
            TwitchUserId = twitchId,
            Username = name.ToLowerInvariant(),
            UsernameNormalized = name.ToLowerInvariant(),
            DisplayName = name,
        };

    private static Record Song(
        string track,
        string artist,
        string requesterId,
        DateTimeOffset at
    ) =>
        new()
        {
            BroadcasterId = Broadcaster,
            RecordType = SongRequestHistory.RecordType,
            UserId = requesterId,
            Data = JsonSerializer.Serialize(
                new SongRequestHistory("spotify:track:" + track, track, artist, null, "spotify"),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
            ),
            CreatedAt = at.UtcDateTime,
        };

    private static Redemption Redeemed(string id, string title, DateTimeOffset at) =>
        new()
        {
            BroadcasterId = Broadcaster,
            RedemptionId = id,
            RewardId = "r-" + title,
            RewardTitle = title,
            UserId = "u-" + id,
            UserDisplayName = "U" + id,
            Cost = 100,
            Status = "fulfilled",
            RedeemedAt = at.UtcDateTime,
            UpdatedAt = at.UtcDateTime,
        };

    private static ChatMessage Chat(string id, string user, string? streamId) =>
        new()
        {
            Id = id,
            BroadcasterId = Broadcaster,
            StreamId = streamId,
            UserId = user,
            Username = user,
            DisplayName = user,
            UserType = "viewer",
            Message = "hi",
            CreatedAt = Started.UtcDateTime.AddMinutes(10),
        };

    private static CommandUsage Ran(string name, DateTimeOffset at) =>
        new()
        {
            BroadcasterId = Broadcaster,
            CommandNameSnapshot = name,
            ViewerProfileId = Guid.NewGuid(),
            ViewerUserId = Guid.NewGuid(),
            WasSuccessful = true,
            CreatedAt = at.UtcDateTime,
        };

    private static async Task<AuthDbContext> SeededAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        DateTimeOffset inside = Started.AddHours(1);
        DateTimeOffset before = Started.AddMinutes(-30);
        DateTimeOffset after = Ended.AddMinutes(30);

        db.Streams.Add(NewStream("s-1", Started, Ended));

        // Inside the window.
        db.ChannelEvents.AddRange(
            Fact(Broadcaster, "channel.follow", inside, new { userId = "f1" }),
            Fact(Broadcaster, "channel.follow", inside.AddMinutes(1), new { userId = "f2" }),
            Fact(Broadcaster, "channel.follow", inside.AddMinutes(2), new { userId = "f3" }),
            Fact(
                Broadcaster,
                "channel.subscribe",
                inside,
                new
                {
                    userId = "n1",
                    userDisplayName = "NewOne",
                    isGift = false,
                }
            ),
            Fact(
                Broadcaster,
                "channel.subscribe",
                inside.AddMinutes(1),
                new
                {
                    userId = "n2",
                    userDisplayName = "NewTwo",
                    isGift = false,
                }
            ),
            // A gift recipient: counted under the gift event, never as a new sub.
            Fact(
                Broadcaster,
                "channel.subscribe",
                inside.AddMinutes(2),
                new
                {
                    userId = "n3",
                    userDisplayName = "Lucky",
                    isGift = true,
                }
            ),
            Fact(
                Broadcaster,
                "channel.subscription.message",
                inside.AddMinutes(3),
                new { userId = "r1", userDisplayName = "Returning" }
            ),
            Fact(
                Broadcaster,
                "channel.subscription.gift",
                inside,
                new
                {
                    gifterUserId = "g1",
                    gifterDisplayName = "Generous",
                    giftCount = 3,
                    isAnonymous = false,
                }
            ),
            Fact(
                Broadcaster,
                "channel.subscription.gift",
                inside.AddMinutes(5),
                new { giftCount = 1, isAnonymous = true }
            ),
            Fact(
                Broadcaster,
                "channel.cheer",
                inside,
                new
                {
                    userId = "c1",
                    userDisplayName = "Cheerer",
                    bits = 300,
                    isAnonymous = false,
                }
            ),
            Fact(
                Broadcaster,
                "channel.cheer",
                inside.AddMinutes(3),
                new
                {
                    userId = "c1",
                    userDisplayName = "Cheerer",
                    bits = 200,
                    isAnonymous = false,
                }
            ),
            Fact(
                Broadcaster,
                "channel.raid",
                inside,
                new
                {
                    fromUserId = "x1",
                    fromDisplayName = "Raider",
                    viewerCount = 12,
                }
            )
        );

        // Outside the window: before, after, and another channel.
        db.ChannelEvents.AddRange(
            Fact(Broadcaster, "channel.follow", before, new { userId = "old" }),
            Fact(Broadcaster, "channel.follow", after, new { userId = "late" }),
            Fact(
                Broadcaster,
                "channel.subscribe",
                before,
                new
                {
                    userId = "o1",
                    userDisplayName = "Old",
                    isGift = false,
                }
            ),
            Fact(
                Broadcaster,
                "channel.cheer",
                after,
                new
                {
                    userId = "c9",
                    userDisplayName = "Late",
                    bits = 9999,
                    isAnonymous = false,
                }
            ),
            Fact(
                Broadcaster,
                "channel.raid",
                before,
                new
                {
                    fromUserId = "x2",
                    fromDisplayName = "EarlyRaider",
                    viewerCount = 50,
                }
            ),
            Fact(OtherBroadcaster, "channel.follow", inside, new { userId = "elsewhere" })
        );

        db.Redemptions.AddRange(
            Redeemed("1", "Hydrate", inside),
            Redeemed("2", "Hydrate", inside.AddMinutes(1)),
            Redeemed("3", "Hydrate", inside.AddMinutes(2)),
            Redeemed("4", "Stretch", inside.AddMinutes(3)),
            Redeemed("5", "Old", before)
        );

        db.Records.AddRange(
            Song("Song A", "Artist A", "tw-alice", inside),
            Song("Song B", "Artist B", "tw-bob", inside.AddMinutes(4)),
            Song("Song D", "Artist D", "tw-alice", inside.AddMinutes(6)),
            Song("Song C", "Artist C", "tw-bob", after)
        );

        db.Users.AddRange(Viewer("tw-alice", "Alice"), Viewer("tw-bob", "Bob"));

        db.CommandUsages.AddRange(
            Ran("!hug", inside),
            Ran("!hug", inside.AddMinutes(1)),
            Ran("!hug", inside.AddMinutes(2)),
            Ran("!lurk", inside.AddMinutes(3)),
            Ran("!lurk", inside.AddMinutes(4)),
            Ran("!so", inside.AddMinutes(5)),
            Ran("!old", before)
        );

        db.ChatMessages.AddRange(
            Chat("m1", "alice", "s-1"),
            Chat("m2", "alice", "s-1"),
            Chat("m3", "bob", "s-1"),
            Chat("m4", "carol", "s-other")
        );

        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Counts_every_fact_inside_the_window_and_none_outside_it()
    {
        AuthDbContext db = await SeededAsync();
        StreamRecapService service = NewService(db, Ended.AddHours(1));

        Result<StreamRecapDto> result = await service.GetRecapAsync(Broadcaster, "s-1");

        result.IsSuccess.Should().BeTrue();
        StreamRecapDto recap = result.Value;

        recap.Stream.Id.Should().Be("s-1");
        recap.Stream.StartedAt.Should().Be(Started);
        recap.Stream.EndedAt.Should().Be(Ended);
        recap.Stream.DurationSeconds.Should().Be(12600);
        recap.Stream.PeakViewers.Should().Be(87);
        recap.Stream.Title.Should().Be("Celeste night");
        recap.Stream.Game.Should().Be("Celeste");

        recap.Followers.Should().Be(3);

        recap.Subs.New.Should().Be(2, "a gift recipient's own subscribe row is not a new sub");
        recap.Subs.Resub.Should().Be(1);
        recap.Subs.Gift.Should().Be(4, "3 from the named gifter plus 1 anonymous");
        recap.Subs.Total.Should().Be(7);

        recap.GiftSubs.Should().HaveCount(2);
        recap.GiftSubs[0].Name.Should().Be("Generous");
        recap.GiftSubs[0].Count.Should().Be(3);
        recap.GiftSubs[1].Name.Should().Be("Anonymous");
        recap.GiftSubs[1].Count.Should().Be(1);

        recap.Bits.Total.Should().Be(500);
        recap.Bits.Top.Should().ContainSingle();
        recap.Bits.Top[0].Name.Should().Be("Cheerer");
        recap.Bits.Top[0].Bits.Should().Be(500);

        recap.RaidsIn.Should().ContainSingle();
        recap.RaidsIn[0].Name.Should().Be("Raider");
        recap.RaidsIn[0].Viewers.Should().Be(12);

        recap.Redemptions.Count.Should().Be(4);
        recap.Redemptions.Top.Should().HaveCount(2);
        recap.Redemptions.Top[0].Title.Should().Be("Hydrate");
        recap.Redemptions.Top[0].Count.Should().Be(3);
        recap.Redemptions.Top[1].Title.Should().Be("Stretch");
        recap.Redemptions.Top[1].Count.Should().Be(1);

        recap.Songs.Count.Should().Be(3);
        recap.Songs.Recent.Select(s => s.Track).Should().Equal("Song D", "Song B", "Song A");
        recap.Songs.Recent[1].Artist.Should().Be("Artist B");
        recap
            .Songs.TopRequesters.Select(r => (r.Name, r.Count))
            .Should()
            .Equal(("Alice", 2), ("Bob", 1));

        recap.Chat.Messages.Should().Be(3);
        recap.Chat.UniqueChatters.Should().Be(2);
        recap
            .Chat.TopChatters.Select(c => (c.Name, c.Messages))
            .Should()
            .Equal(("alice", 2), ("bob", 1));

        recap.SelfSubscribers.Should().Equal("NewOne", "NewTwo", "Returning");

        recap.CommandsRun.Should().Be(6);
        recap
            .TopCommands.Select(c => (c.Name, c.Count))
            .Should()
            .Equal(("!hug", 3), ("!lurk", 2), ("!so", 1));
    }

    [Fact]
    public async Task A_stream_with_no_events_returns_zeros_and_empty_lists()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Streams.Add(NewStream("s-empty", Started, Ended));
        await db.SaveChangesAsync();
        StreamRecapService service = NewService(db, Ended.AddHours(1));

        Result<StreamRecapDto> result = await service.GetRecapAsync(Broadcaster, null);

        result.IsSuccess.Should().BeTrue();
        StreamRecapDto recap = result.Value;
        recap.Followers.Should().Be(0);
        recap.Subs.Total.Should().Be(0);
        recap.GiftSubs.Should().BeEmpty();
        recap.Bits.Total.Should().Be(0);
        recap.Bits.Top.Should().BeEmpty();
        recap.RaidsIn.Should().BeEmpty();
        recap.Redemptions.Count.Should().Be(0);
        recap.Redemptions.Top.Should().BeEmpty();
        recap.Songs.Count.Should().Be(0);
        recap.Songs.Recent.Should().BeEmpty();
        recap.Songs.TopRequesters.Should().BeEmpty();
        recap.Chat.Messages.Should().Be(0);
        recap.Chat.UniqueChatters.Should().Be(0);
        recap.Chat.TopChatters.Should().BeEmpty();
        recap.SelfSubscribers.Should().BeEmpty();
        recap.CommandsRun.Should().Be(0);
        recap.TopCommands.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_a_stream_id_the_latest_stream_is_used_and_a_live_stream_runs_to_now()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Streams.Add(NewStream("s-old", Started.AddDays(-2), Started.AddDays(-2).AddHours(2)));
        db.Streams.Add(NewStream("s-live", Started, null));
        db.ChannelEvents.Add(
            Fact(Broadcaster, "channel.follow", Started.AddMinutes(30), new { userId = "f1" })
        );
        db.ChannelEvents.Add(
            Fact(
                Broadcaster,
                "channel.follow",
                Started.AddDays(-2).AddHours(1),
                new { userId = "f0" }
            )
        );
        await db.SaveChangesAsync();
        StreamRecapService service = NewService(db, Started.AddHours(2));

        Result<StreamRecapDto> result = await service.GetRecapAsync(Broadcaster, null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Stream.Id.Should().Be("s-live");
        result.Value.Stream.EndedAt.Should().BeNull();
        result.Value.Stream.DurationSeconds.Should().Be(7200);
        result.Value.Followers.Should().Be(1);
    }

    [Fact]
    public async Task An_unknown_stream_fails_with_not_found()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        StreamRecapService service = NewService(db, Ended);

        Result<StreamRecapDto> result = await service.GetRecapAsync(Broadcaster, "missing");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }
}
