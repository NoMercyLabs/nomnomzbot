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
using NomNomzBot.Application.Dashboard.Stats;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Infrastructure.Dashboard.Stats;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using Record = NomNomzBot.Domain.Platform.Entities.Record;

namespace NomNomzBot.Infrastructure.Tests.Dashboard;

/// <summary>
/// The stream recap counts recorded facts inside the stream window. Every test seeds real rows, some inside and
/// some outside the window, and asserts the exact numbers and shapes the DTO carries.
/// </summary>
public sealed class StreamStatsServiceTests
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

    private static StreamStatsService NewService(AuthDbContext db, DateTimeOffset now)
    {
        TimeProvider clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(now);
        StreamBanFilter bans = new(db);
        StreamFactReader facts = new(db, bans);
        IStreamMetric[] metrics =
        [
            new DurationMetric(),
            new PeakViewersMetric(db),
            new FollowersMetric(facts),
            new NewFollowersMetric(facts),
            new SubsMetric(facts),
            new GiftersMetric(facts),
            new SelfSubsMetric(facts),
            new BitsMetric(facts),
            new RaidsInMetric(facts),
            new RedemptionsMetric(db, bans),
            new SongRequestersMetric(db, bans),
            new ChatMetric(db, bans),
            new TopChattersMetric(db, bans),
            new CommandsRunMetric(db, bans),
        ];
        return new StreamStatsService(db, clock, metrics);
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

    // The live writer never sets StreamId (0 of 58685 rows on the dev box), so a chat row belongs to a stream by time.
    private static ChatMessage Chat(
        string id,
        string user,
        DateTimeOffset at,
        Guid? broadcaster = null
    ) =>
        new()
        {
            Id = id,
            BroadcasterId = broadcaster ?? Broadcaster,
            UserId = user,
            Username = user,
            DisplayName = user,
            UserType = "viewer",
            Message = "hi",
            CreatedAt = at.UtcDateTime,
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
            Fact(
                Broadcaster,
                "channel.follow",
                inside,
                new { userId = "f1", userDisplayName = "FirstFollower" }
            ),
            Fact(
                Broadcaster,
                "channel.follow",
                inside.AddMinutes(2),
                new { userId = "f3", userDisplayName = "ThirdFollower" }
            ),
            Fact(
                Broadcaster,
                "channel.follow",
                inside.AddMinutes(1),
                new { userId = "f2", userDisplayName = "SecondFollower" }
            ),
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
            Fact(
                Broadcaster,
                "channel.follow",
                before,
                new { userId = "old", userDisplayName = "EarlyFollower" }
            ),
            Fact(
                Broadcaster,
                "channel.follow",
                after,
                new { userId = "late", userDisplayName = "LateFollower" }
            ),
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
            Chat("m1", "alice", inside),
            Chat("m2", "alice", inside.AddMinutes(1)),
            Chat("m3", "bob", inside.AddMinutes(2)),
            Chat("m4", "carol", before),
            Chat("m5", "dave", after),
            Chat("m6", "erin", inside, OtherBroadcaster)
        );

        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<StreamStatsDto> AllAsync()
    {
        AuthDbContext db = await SeededAsync();
        Result<StreamStatsDto> result = await NewService(db, Ended.AddHours(1))
            .GetAsync(Broadcaster, "s-1", null);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task Window_and_scalar_metrics_count_only_inside_the_window()
    {
        StreamStatsDto stats = await AllAsync();

        stats.Stream.Id.Should().Be("s-1");
        stats.Stream.StartedAt.Should().Be(Started);
        stats.Stream.EndedAt.Should().Be(Ended);
        stats.Stream.Title.Should().Be("Celeste night");
        stats.Stream.Game.Should().Be("Celeste");
        stats.Duration.Should().Be(12600);
        stats.PeakViewers.Should().Be(87);
        stats
            .Followers.Should()
            .Be(3, "the follows before, after and on another channel are outside");
    }

    private static EventJournal ViewerSample(long position, DateTimeOffset at, int viewers) =>
        new()
        {
            EventId = Guid.NewGuid(),
            BroadcasterId = Broadcaster,
            StreamPosition = position,
            EventType = "StreamViewerCountSampledEvent",
            EventVersion = 1,
            Source = "domain",
            Payload = $"{{\"ViewerCount\":{viewers},\"StreamId\":\"tw-1\"}}",
            Metadata = "{}",
            OccurredAt = at.UtcDateTime,
            RecordedAt = at.UtcDateTime,
        };

    [Fact]
    public async Task The_peak_is_the_largest_viewer_sample_inside_the_window_when_it_beats_the_stored_peak()
    {
        AuthDbContext db = await SeededAsync();
        db.EventJournals.AddRange(
            ViewerSample(1, Started.AddHours(1), 30),
            ViewerSample(2, Started.AddHours(2), 112),
            ViewerSample(3, Ended.AddHours(1), 900)
        );
        await db.SaveChangesAsync();

        Result<StreamStatsDto> result = await NewService(db, Ended.AddHours(2))
            .GetAsync(Broadcaster, "s-1", ["peakViewers"]);

        result
            .Value.PeakViewers.Should()
            .Be(
                112,
                "the stored 87 is lower than a sample inside the window; the sample after the end is outside"
            );
    }

    [Fact]
    public async Task The_stored_peak_stands_when_no_sample_beats_it()
    {
        AuthDbContext db = await SeededAsync();
        db.EventJournals.Add(ViewerSample(1, Started.AddHours(1), 30));
        await db.SaveChangesAsync();

        Result<StreamStatsDto> result = await NewService(db, Ended.AddHours(2))
            .GetAsync(Broadcaster, "s-1", ["peakViewers"]);

        result.Value.PeakViewers.Should().Be(87);
    }

    [Fact]
    public async Task Sub_metrics_split_new_resub_gift_and_self_subscribers()
    {
        StreamStatsDto stats = await AllAsync();

        stats.Subs!.New.Should().Be(2, "a gift recipient's own subscribe row is not a new sub");
        stats.Subs.Resub.Should().Be(1);
        stats.Subs.Gift.Should().Be(4, "3 from the named gifter plus 1 anonymous");
        stats.Subs.Total.Should().Be(7);
        stats
            .Gifters!.Select(g => (g.Name, g.Count))
            .Should()
            .Equal(("Generous", 3), ("Anonymous", 1));
        stats.SelfSubs.Should().Equal("NewOne", "NewTwo", "Returning");
    }

    [Fact]
    public async Task New_followers_lists_the_names_inside_the_window_oldest_first()
    {
        StreamStatsDto stats = await AllAsync();

        stats
            .NewFollowers.Should()
            .Equal(
                ["FirstFollower", "SecondFollower", "ThirdFollower"],
                "the follows before and after the window and on another channel are out"
            );
    }

    [Fact]
    public async Task Bits_and_raids_count_only_inside_the_window()
    {
        StreamStatsDto stats = await AllAsync();

        stats.Bits!.Total.Should().Be(500);
        stats.Bits.Top.Select(c => (c.Name, c.Count)).Should().Equal(("Cheerer", 500));
        stats.RaidsIn!.Select(r => (r.Name, r.Viewers)).Should().Equal(("Raider", 12));
    }

    [Fact]
    public async Task Table_metrics_rank_redemptions_requesters_chatters_and_commands()
    {
        StreamStatsDto stats = await AllAsync();

        stats.Redemptions!.Total.Should().Be(4);
        stats
            .Redemptions.Top.Select(t => (t.Name, t.Count))
            .Should()
            .Equal(("Hydrate", 3), ("Stretch", 1));
        stats.SongRequesters!.Total.Should().Be(3);
        stats
            .SongRequesters.Top.Select(r => (r.Name, r.Count))
            .Should()
            .Equal(("Alice", 2), ("Bob", 1));
        stats.Chat!.Messages.Should().Be(3);
        stats.Chat.UniqueChatters.Should().Be(2);
        stats.TopChatters!.Select(c => (c.Name, c.Count)).Should().Equal(("alice", 2), ("bob", 1));
        stats.CommandsRun!.Total.Should().Be(6);
        stats
            .CommandsRun.Top.Select(c => (c.Name, c.Count))
            .Should()
            .Equal(("!hug", 3), ("!lurk", 2), ("!so", 1));
    }

    [Fact]
    public async Task Only_the_requested_metrics_are_filled()
    {
        AuthDbContext db = await SeededAsync();

        Result<StreamStatsDto> result = await NewService(db, Ended.AddHours(1))
            .GetAsync(Broadcaster, "s-1", ["followers", "bits"]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Followers.Should().Be(3);
        result.Value.Bits!.Total.Should().Be(500);
        result.Value.Subs.Should().BeNull();
        result.Value.Gifters.Should().BeNull();
        result.Value.Chat.Should().BeNull();
        result.Value.CommandsRun.Should().BeNull();
        result.Value.Duration.Should().BeNull();
    }

    [Fact]
    public async Task An_unknown_metric_key_fails_validation_naming_the_key()
    {
        AuthDbContext db = await SeededAsync();

        Result<StreamStatsDto> result = await NewService(db, Ended.AddHours(1))
            .GetAsync(Broadcaster, "s-1", ["followers", "nonsense"]);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.ErrorMessage.Should().Contain("'nonsense'");
    }

    [Fact]
    public async Task A_stream_with_no_events_returns_zeros_and_empty_lists()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Streams.Add(NewStream("s-empty", Started, Ended));
        await db.SaveChangesAsync();

        Result<StreamStatsDto> result = await NewService(db, Ended.AddHours(1))
            .GetAsync(Broadcaster, null, null);

        result.IsSuccess.Should().BeTrue();
        StreamStatsDto stats = result.Value;
        stats.Followers.Should().Be(0);
        stats.Subs!.Total.Should().Be(0);
        stats.Gifters.Should().BeEmpty();
        stats.SelfSubs.Should().BeEmpty();
        stats.Bits!.Total.Should().Be(0);
        stats.Bits.Top.Should().BeEmpty();
        stats.RaidsIn.Should().BeEmpty();
        stats.Redemptions!.Top.Should().BeEmpty();
        stats.SongRequesters!.Top.Should().BeEmpty();
        stats.Chat!.Messages.Should().Be(0);
        stats.TopChatters.Should().BeEmpty();
        stats.CommandsRun!.Total.Should().Be(0);
    }

    [Fact]
    public async Task A_raid_sent_by_the_channel_owner_is_not_counted_as_a_raid_in()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new Channel
            {
                Id = Broadcaster,
                OwnerUserId = Guid.NewGuid(),
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "39863651",
                Name = "stoney_eagle",
                NameNormalized = "stoney_eagle",
            }
        );
        db.Streams.Add(NewStream("s-self", Started, Ended));
        DateTimeOffset inside = Started.AddHours(1);
        db.ChannelEvents.AddRange(
            Fact(
                Broadcaster,
                "channel.raid",
                inside,
                new
                {
                    fromUserId = "39863651",
                    fromDisplayName = "Stoney_Eagle",
                    viewerCount = 11,
                }
            ),
            Fact(
                Broadcaster,
                "channel.raid",
                inside.AddMinutes(1),
                new
                {
                    fromUserId = "80695883",
                    fromDisplayName = "ApexPixelated",
                    viewerCount = 15,
                }
            )
        );
        await db.SaveChangesAsync();

        Result<StreamStatsDto> result = await NewService(db, Ended.AddHours(1))
            .GetAsync(Broadcaster, "s-self", ["raidsIn"]);

        result.IsSuccess.Should().BeTrue();
        result
            .Value.RaidsIn!.Select(r => (r.Name, r.Viewers))
            .Should()
            .Equal(("ApexPixelated", 15));
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

        Result<StreamStatsDto> result = await NewService(db, Started.AddHours(2))
            .GetAsync(Broadcaster, null, ["followers", "duration"]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Stream.Id.Should().Be("s-live");
        result.Value.Stream.EndedAt.Should().BeNull();
        result.Value.Duration.Should().Be(7200);
        result.Value.Followers.Should().Be(1);
    }

    /// <summary>The raid recap asks at the moment of the raid command: everything from this stream's start up
    /// to that moment is in, the earlier stream and anything after the command is out.</summary>
    [Fact]
    public async Task A_live_stream_recap_runs_from_the_stream_start_to_the_raid_command_moment()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        DateTimeOffset raidCommand = Started.AddHours(2);
        db.Streams.Add(NewStream("s-old", Started.AddDays(-1), Started.AddDays(-1).AddHours(3)));
        db.Streams.Add(NewStream("s-live", Started, null));
        db.ChannelEvents.Add(
            Fact(Broadcaster, "channel.follow", Started.AddSeconds(-1), new { userId = "before" })
        );
        db.ChannelEvents.Add(
            Fact(Broadcaster, "channel.follow", Started.AddSeconds(1), new { userId = "first" })
        );
        db.ChannelEvents.Add(
            Fact(Broadcaster, "channel.follow", raidCommand.AddSeconds(-1), new { userId = "last" })
        );
        db.ChannelEvents.Add(
            Fact(Broadcaster, "channel.follow", raidCommand.AddSeconds(1), new { userId = "after" })
        );
        await db.SaveChangesAsync();

        Result<StreamStatsDto> result = await NewService(db, raidCommand)
            .GetAsync(Broadcaster, null, ["followers"]);

        result
            .Value.Followers.Should()
            .Be(2, "only 'first' and 'last' lie between start and the raid command");
    }

    private static ModerationHistoryEntry Ledger(string twitchId, string kind, DateTimeOffset at) =>
        new()
        {
            BroadcasterId = Broadcaster,
            SubjectUserId = Guid.NewGuid(),
            SubjectTwitchUserId = twitchId,
            ActionType = kind,
            OccurredAt = at.UtcDateTime,
        };

    private static async Task<StreamStatsDto> BanRecapAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        DateTimeOffset inside = Started.AddHours(1);
        db.Streams.Add(NewStream("s-1", Started, Ended));
        db.ModerationHistoryEntries.AddRange(
            Ledger("banned", ModerationHistoryEntryKinds.Ban, Started.AddHours(-2)),
            Ledger("timedout", ModerationHistoryEntryKinds.Timeout, Started.AddHours(-2)),
            Ledger("unbanned", ModerationHistoryEntryKinds.Ban, Started.AddHours(-3)),
            Ledger("unbanned", ModerationHistoryEntryKinds.Unban, Started.AddHours(-2))
        );
        db.Configurations.Add(
            new()
            {
                BroadcasterId = Broadcaster,
                Key = "ban:listed",
                Value = "{}",
            }
        );
        db.ChatMessages.AddRange(
            Chat("c1", "good", inside),
            Chat("c2", "banned", inside.AddMinutes(1)),
            Chat("c3", "banned", inside.AddMinutes(2)),
            Chat("c4", "timedout", inside.AddMinutes(3)),
            Chat("c5", "unbanned", inside.AddMinutes(4)),
            Chat("c6", "listed", inside.AddMinutes(5))
        );
        db.Records.AddRange(
            Song("Song A", "Artist A", "good", inside),
            Song("Song B", "Artist B", "banned", inside.AddMinutes(1))
        );
        await db.SaveChangesAsync();

        Result<StreamStatsDto> result = await NewService(db, Ended.AddHours(1))
            .GetAsync(Broadcaster, "s-1", null);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task Banned_people_are_out_of_messages_unique_chatters_and_top_chatters()
    {
        StreamStatsDto stats = await BanRecapAsync();

        stats.Chat!.Messages.Should().Be(3);
        stats.Chat.UniqueChatters.Should().Be(3);
        stats
            .TopChatters!.Select(c => c.Name)
            .Should()
            .BeEquivalentTo("good", "timedout", "unbanned");
    }

    [Fact]
    public async Task A_timed_out_chatter_and_an_unbanned_chatter_still_count()
    {
        StreamStatsDto stats = await BanRecapAsync();

        stats.TopChatters!.Select(c => c.Name).Should().Contain(["timedout", "unbanned"]);
        stats.TopChatters!.Select(c => c.Name).Should().NotContain(["banned", "listed"]);
    }

    [Fact]
    public async Task A_banned_requester_is_out_of_song_requesters()
    {
        StreamStatsDto stats = await BanRecapAsync();

        stats.SongRequesters!.Total.Should().Be(1);
    }

    [Fact]
    public async Task A_song_request_that_was_deleted_is_not_counted()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        DateTimeOffset inside = Started.AddHours(1);
        db.Streams.Add(NewStream("s-1", Started, Ended));
        Record removed = Song("Song B", "Artist B", "alice", inside.AddMinutes(1));
        removed.DeletedAt = inside.AddMinutes(2).UtcDateTime;
        db.Records.AddRange(Song("Song A", "Artist A", "alice", inside), removed);
        await db.SaveChangesAsync();

        Result<StreamStatsDto> result = await NewService(db, Ended.AddHours(1))
            .GetAsync(Broadcaster, "s-1", ["songRequesters"]);

        result.IsSuccess.Should().BeTrue();
        result
            .Value.SongRequesters!.Total.Should()
            .Be(1, "the removed request is gone from the history");
        result.Value.SongRequesters.Top.Select(r => (r.Name, r.Count)).Should().Equal(("alice", 1));
    }

    [Fact]
    public async Task An_unknown_stream_fails_with_not_found()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();

        Result<StreamStatsDto> result = await NewService(db, Ended)
            .GetAsync(Broadcaster, "missing", null);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }
}
