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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Stream;
using NomNomzBot.Infrastructure.Stream.Jobs;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Stream;

/// <summary>
/// A Streams row that is still open while the channel is offline (restart or a missed stream.offline) must be
/// closed, and its empty title / category filled from the channel's stored values. Every test reads the row back
/// from the database after the action.
/// </summary>
public sealed class StreamRowReconcilerTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192f000-0000-7000-8000-0000000000e1");
    private static readonly Guid Owner = Guid.Parse("0192f000-0000-7000-8000-0000000000e9");
    private static readonly DateTimeOffset Started = new(2026, 10, 12, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Observed = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private static Channel NewChannel(string? title = "Chill night", string? game = "Celeste") =>
        new()
        {
            Id = Broadcaster,
            OwnerUserId = Owner,
            Provider = AuthEnums.Platform.Twitch,
            ExternalChannelId = "tw-e1",
            TwitchChannelId = "tw-e1",
            Name = "streamer_e1",
            NameNormalized = "streamer_e1",
            IsOnboarded = true,
            IsLive = false,
            Title = title,
            GameName = game,
            DeploymentMode = AuthEnums.DeploymentMode.Saas,
            BillingTierKey = "free",
        };

    private static Domain.Stream.Entities.Stream OpenRow(
        string id,
        DateTimeOffset startedAt,
        string title = "",
        string game = ""
    ) =>
        new()
        {
            Id = id,
            ChannelId = Broadcaster,
            StartedAt = startedAt,
            Title = title,
            GameName = game,
        };

    private static ChatMessage Chat(string id, string? streamId, DateTimeOffset at) =>
        new()
        {
            Id = id,
            BroadcasterId = Broadcaster,
            StreamId = streamId,
            UserId = "u1",
            Username = "u1",
            DisplayName = "U1",
            UserType = "viewer",
            Message = "hi",
            CreatedAt = at.UtcDateTime,
        };

    private static async Task<Domain.Stream.Entities.Stream> Reload(AuthDbContext db, string id) =>
        await db.Streams.AsNoTracking().SingleAsync(s => s.Id == id);

    [Fact]
    public async Task An_open_row_of_an_offline_channel_is_closed_at_the_last_chat_of_that_stream_and_its_empty_text_is_filled()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Channel channel = NewChannel();
        db.Channels.Add(channel);
        db.Streams.Add(OpenRow("s-1", Started));
        db.ChatMessages.Add(Chat("m-1", "s-1", Started.AddHours(1)));
        db.ChatMessages.Add(Chat("m-2", "s-1", Started.AddHours(2)));
        await db.SaveChangesAsync();

        bool changed = await StreamRowReconciler.CloseOpenStreamsAsync(
            db,
            channel,
            Observed,
            CancellationToken.None
        );
        await db.SaveChangesAsync();

        changed.Should().BeTrue();
        Domain.Stream.Entities.Stream row = await Reload(db, "s-1");
        row.EndedAt.Should()
            .Be(Started.AddHours(2), "the latest chat of the stream is the best end");
        (row.EndedAt - row.StartedAt).Should().Be(TimeSpan.FromHours(2));
        row.Title.Should().Be("Chill night");
        row.GameName.Should().Be("Celeste");
    }

    [Fact]
    public async Task Without_any_chat_in_its_window_the_row_closes_at_its_start_so_it_adds_no_live_time()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Channel channel = NewChannel();
        db.Channels.Add(channel);
        db.Streams.Add(OpenRow("s-1", Started));
        db.ChatMessages.Add(Chat("before", null, Started.AddMinutes(-5)));
        await db.SaveChangesAsync();

        await StreamRowReconciler.CloseOpenStreamsAsync(
            db,
            channel,
            Observed,
            CancellationToken.None
        );
        await db.SaveChangesAsync();

        Domain.Stream.Entities.Stream row = await Reload(db, "s-1");
        row.EndedAt.Should()
            .Be(
                Started,
                "a chat from before the stream started proves nothing, so only the start is proven"
            );
        row.Title.Should().Be("Chill night", "the text backfill still applies to an open row");
    }

    [Fact]
    public async Task A_row_with_only_channel_chat_in_its_window_closes_at_the_last_such_message()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Channel channel = NewChannel();
        db.Channels.Add(channel);
        db.Streams.Add(OpenRow("s-1", Started));
        db.ChatMessages.Add(Chat("a", null, Started.AddMinutes(10)));
        db.ChatMessages.Add(Chat("b", null, Started.AddMinutes(90)));
        await db.SaveChangesAsync();

        await StreamRowReconciler.CloseOpenStreamsAsync(
            db,
            channel,
            Observed,
            CancellationToken.None
        );
        await db.SaveChangesAsync();

        (await Reload(db, "s-1")).EndedAt.Should().Be(Started.AddMinutes(90));
    }

    [Fact]
    public async Task Chat_after_the_next_streams_start_is_not_used_for_the_older_row()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Channel channel = NewChannel();
        db.Channels.Add(channel);
        db.Streams.Add(OpenRow("old", Started));
        Domain.Stream.Entities.Stream next = OpenRow("next", Started.AddHours(5));
        next.EndedAt = Started.AddHours(7);
        db.Streams.Add(next);
        db.ChatMessages.Add(Chat("in", null, Started.AddHours(1)));
        db.ChatMessages.Add(Chat("later", null, Started.AddHours(6)));
        await db.SaveChangesAsync();

        await StreamRowReconciler.CloseOpenStreamsAsync(
            db,
            channel,
            Observed,
            CancellationToken.None
        );
        await db.SaveChangesAsync();

        (await Reload(db, "old")).EndedAt.Should().Be(Started.AddHours(1));
    }

    [Fact]
    public async Task Text_the_row_already_has_is_kept_and_empty_channel_text_is_never_written()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Channel channel = NewChannel(title: "Other title", game: null);
        db.Channels.Add(channel);
        db.Streams.Add(OpenRow("s-1", Started, title: "Real title"));
        db.ChatMessages.Add(Chat("m", "s-1", Started.AddHours(1)));
        await db.SaveChangesAsync();

        await StreamRowReconciler.CloseOpenStreamsAsync(
            db,
            channel,
            Observed,
            CancellationToken.None
        );
        await db.SaveChangesAsync();

        Domain.Stream.Entities.Stream row = await Reload(db, "s-1");
        row.Title.Should().Be("Real title");
        row.GameName.Should().BeEmpty("the channel has no stored game, so nothing is invented");
        row.EndedAt.Should().Be(Started.AddHours(1));
    }

    [Fact]
    public async Task Only_the_newest_open_row_is_backfilled_but_every_open_row_is_closed_and_closed_rows_are_untouched()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Channel channel = NewChannel();
        db.Channels.Add(channel);
        db.Streams.Add(OpenRow("old", Started.AddDays(-3)));
        db.Streams.Add(OpenRow("new", Started));
        Domain.Stream.Entities.Stream done = OpenRow("done", Started.AddDays(-5));
        done.EndedAt = Started.AddDays(-5).AddHours(1);
        db.Streams.Add(done);
        db.ChatMessages.Add(Chat("o", "old", Started.AddDays(-3).AddHours(2)));
        db.ChatMessages.Add(Chat("n", "new", Started.AddHours(2)));
        await db.SaveChangesAsync();

        await StreamRowReconciler.CloseOpenStreamsAsync(
            db,
            channel,
            Observed,
            CancellationToken.None
        );
        await db.SaveChangesAsync();

        (await Reload(db, "old")).EndedAt.Should().Be(Started.AddDays(-3).AddHours(2));
        (await Reload(db, "old"))
            .Title.Should()
            .BeEmpty("an older stream is not given today's title");
        (await Reload(db, "new")).Title.Should().Be("Chill night");
        (await Reload(db, "done")).EndedAt.Should().Be(Started.AddDays(-5).AddHours(1));
    }

    [Fact]
    public async Task Another_channels_open_row_is_not_closed()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Channel channel = NewChannel();
        db.Channels.Add(channel);
        Domain.Stream.Entities.Stream other = OpenRow("other", Started);
        other.ChannelId = Guid.NewGuid();
        db.Streams.Add(other);
        await db.SaveChangesAsync();

        bool changed = await StreamRowReconciler.CloseOpenStreamsAsync(
            db,
            channel,
            Observed,
            CancellationToken.None
        );

        changed.Should().BeFalse();
        (await Reload(db, "other")).EndedAt.Should().BeNull();
    }

    [Fact]
    public void Backfill_fills_only_empty_fields()
    {
        Domain.Stream.Entities.Stream row = OpenRow("s-1", Started, title: "", game: "Kept");

        bool changed = StreamRowReconciler.BackfillMetadata(row, NewChannel());

        changed.Should().BeTrue();
        row.Title.Should().Be("Chill night");
        row.GameName.Should().Be("Kept");
    }

    // The 2026-10 shape: bot restarted, the registry context is fresh (not live, no CurrentStreamId), the
    // stream.offline was never seen. The poll sees an empty Get Streams and must still close the row.
    [Fact]
    public async Task A_poll_that_sees_the_channel_offline_closes_an_open_row_without_an_in_memory_live_edge()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(NewChannel());
        db.Streams.Add(OpenRow("s-1", Started));
        db.ChatMessages.Add(Chat("m", null, Started.AddHours(3)));
        await db.SaveChangesAsync();

        IChannelRegistry channels = Substitute.For<IChannelRegistry>();
        channels
            .GetAll()
            .Returns(
                new List<ChannelContext>
                {
                    new()
                    {
                        BroadcasterId = Broadcaster,
                        TwitchChannelId = "tw-e1",
                        ChannelName = "streamer_e1",
                        IsLive = false,
                    },
                }
            );
        IPlatformBotReadinessGate gate = Substitute.For<IPlatformBotReadinessGate>();
        gate.IsPlatformBotConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);
        ITwitchStreamsApi streams = Substitute.For<ITwitchStreamsApi>();
        streams
            .GetStreamAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<TwitchStream>("Twitch returned no data.", TwitchErrorCodes.NotFound)
            );
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddSingleton(gate)
            .AddSingleton(streams)
            .AddSingleton(Substitute.For<IEventBus>())
            .BuildServiceProvider();
        StreamStatusPollingService sut = new(
            channels,
            provider.GetRequiredService<IServiceScopeFactory>(),
            new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Observed),
            NullLogger<StreamStatusPollingService>.Instance
        );

        await sut.PollAllAsync(CancellationToken.None);

        Domain.Stream.Entities.Stream row = await Reload(db, "s-1");
        row.EndedAt.Should().Be(Started.AddHours(3));
        row.Title.Should().Be("Chill night");
    }

    // An inconclusive read (rate limit) is not an offline signal: the open row must stay open.
    [Fact]
    public async Task A_broken_poll_read_leaves_the_open_row_open()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(NewChannel());
        db.Streams.Add(OpenRow("s-1", Started));
        await db.SaveChangesAsync();

        IChannelRegistry channels = Substitute.For<IChannelRegistry>();
        channels
            .GetAll()
            .Returns(
                new List<ChannelContext>
                {
                    new()
                    {
                        BroadcasterId = Broadcaster,
                        TwitchChannelId = "tw-e1",
                        ChannelName = "streamer_e1",
                    },
                }
            );
        IPlatformBotReadinessGate gate = Substitute.For<IPlatformBotReadinessGate>();
        gate.IsPlatformBotConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);
        ITwitchStreamsApi streams = Substitute.For<ITwitchStreamsApi>();
        streams
            .GetStreamAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<TwitchStream>("rate limited", TwitchErrorCodes.RateLimited));
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddSingleton(gate)
            .AddSingleton(streams)
            .AddSingleton(Substitute.For<IEventBus>())
            .BuildServiceProvider();
        StreamStatusPollingService sut = new(
            channels,
            provider.GetRequiredService<IServiceScopeFactory>(),
            new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Observed),
            NullLogger<StreamStatusPollingService>.Instance
        );

        await sut.PollAllAsync(CancellationToken.None);

        (await Reload(db, "s-1")).EndedAt.Should().BeNull();
    }
}
