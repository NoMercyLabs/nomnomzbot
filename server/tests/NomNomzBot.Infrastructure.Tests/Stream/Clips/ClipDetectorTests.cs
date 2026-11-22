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

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.Stream.Clips;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Stream.Clips;

/// <summary>
/// A live channel's clips are read about once a minute. The first read of a stream only learns the clips that
/// exist; each later read tells chat about every clip it has not seen, once, oldest first, through the
/// <c>channel.clip.created</c> event response, and publishes one <see cref="ClipCreatedEvent"/> for it.
/// </summary>
public sealed class ClipDetectorTests
{
    private const string ResponseKey = "channel.clip.created";
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000c11b");
    private static readonly DateTimeOffset StreamStart = new(2026, 11, 22, 18, 0, 0, TimeSpan.Zero);

    private sealed class RecordingBus : IEventBus
    {
        public List<IDomainEvent> Events { get; } = [];

        public Task PublishAsync<TEvent>(
            TEvent @event,
            CancellationToken cancellationToken = default
        )
            where TEvent : class, IDomainEvent
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }

        public void PublishFireAndForget<TEvent>(TEvent @event)
            where TEvent : class, IDomainEvent => Events.Add(@event);
    }

    private sealed class Rig
    {
        public required ClipDetector Sut { get; init; }
        public required FakeTimeProvider Clock { get; init; }
        public required RecordingBus Bus { get; init; }
        public required IEventResponseExecutor Executor { get; init; }
        public required ITwitchClipsApi Clips { get; init; }
        public required ChannelContext Context { get; init; }
        public List<TwitchClip> Existing { get; } = [];
        public bool ReadFails { get; set; }

        public Task PassAsync(int secondsLater = 60)
        {
            Clock.Advance(TimeSpan.FromSeconds(secondsLater));
            return Sut.ProcessAsync(CancellationToken.None);
        }

        public List<ClipCreatedEvent> Published => [.. Bus.Events.OfType<ClipCreatedEvent>()];

        public List<IReadOnlyDictionary<string, string>> Sent =>
            [
                .. Executor
                    .ReceivedCalls()
                    .Where(c =>
                        c.GetMethodInfo().Name == nameof(IEventResponseExecutor.ExecuteAsync)
                        && (Guid)c.GetArguments()[0]! == Channel
                        && (string)c.GetArguments()[1]! == ResponseKey
                    )
                    .Select(c => (IReadOnlyDictionary<string, string>)c.GetArguments()[4]!),
            ];

        public int Reads =>
            Clips
                .ReceivedCalls()
                .Count(c =>
                    c.GetMethodInfo().Name == nameof(ITwitchClipsApi.GetClipsByBroadcasterAsync)
                );
    }

    private static TwitchClip Clip(string id, DateTimeOffset createdAt) =>
        new(
            id,
            $"https://clips.twitch.tv/{id}",
            $"https://clips.twitch.tv/embed?clip={id}",
            "tw-c11b",
            "stoney",
            "9876",
            $"creator-{id}",
            "video-1",
            "509658",
            "en",
            $"title-{id}",
            0,
            createdAt,
            "https://example.test/thumb.jpg",
            20,
            null,
            false
        );

    private static Rig Build()
    {
        FakeTimeProvider clock = new(StreamStart.AddMinutes(10));
        IEventResponseExecutor executor = Substitute.For<IEventResponseExecutor>();
        ITwitchClipsApi clips = Substitute.For<ITwitchClipsApi>();
        ITwitchHelixClient helix = Substitute.For<ITwitchHelixClient>();
        helix.Clips.Returns(clips);
        ServiceCollection services = new();
        services.AddSingleton(executor);
        services.AddSingleton(helix);
        IServiceScopeFactory scopes = services
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        ChannelContext context = new()
        {
            BroadcasterId = Channel,
            TwitchChannelId = "tw-c11b",
            ChannelName = "stoney",
            IsLive = true,
            WentLiveAt = StreamStart,
        };
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.GetLiveChannels().Returns(_ => context.IsLive ? [context] : []);

        RecordingBus bus = new();
        Rig rig = new()
        {
            Sut = new(scopes, registry, bus, clock, NullLogger<ClipDetector>.Instance),
            Clock = clock,
            Bus = bus,
            Executor = executor,
            Clips = clips,
            Context = context,
        };
        clips
            .GetClipsByBroadcasterAsync(
                Channel,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
                rig.ReadFails
                    ? Result.Failure<TwitchPage<TwitchClip>>("down", "TWITCH_DOWN")
                    : Result.Success(new TwitchPage<TwitchClip>([.. rig.Existing], null, 0))
            );
        return rig;
    }

    [Fact]
    public async Task The_first_read_of_a_stream_learns_the_clips_and_tells_chat_nothing()
    {
        Rig rig = Build();
        rig.Existing.Add(Clip("old-1", StreamStart.AddMinutes(3)));
        rig.Existing.Add(Clip("old-2", StreamStart.AddMinutes(5)));

        await rig.PassAsync();

        rig.Sent.Should().BeEmpty();
        rig.Published.Should().BeEmpty();
        rig.Reads.Should().Be(1);
    }

    [Fact]
    public async Task The_read_asks_for_the_clips_created_since_the_stream_started()
    {
        Rig rig = Build();

        await rig.PassAsync();

        await rig
            .Clips.Received(1)
            .GetClipsByBroadcasterAsync(
                Channel,
                Arg.Any<TwitchPageRequest>(),
                StreamStart,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_clip_that_appears_on_a_later_read_goes_to_chat_once_with_its_title_creator_and_link_and_is_published()
    {
        Rig rig = Build();
        rig.Existing.Add(Clip("old-1", StreamStart.AddMinutes(3)));
        await rig.PassAsync();
        DateTimeOffset madeAt = StreamStart.AddMinutes(11);
        rig.Existing.Add(Clip("new-1", madeAt));

        await rig.PassAsync();

        rig.Sent.Should().HaveCount(1);
        rig.Sent[0]["clip.title"].Should().Be("title-new-1");
        rig.Sent[0]["clip.creator"].Should().Be("creator-new-1");
        rig.Sent[0]["clip.url"].Should().Be("https://clips.twitch.tv/new-1");
        rig.Published.Should().HaveCount(1);
        ClipCreatedEvent published = rig.Published[0];
        published.BroadcasterId.Should().Be(Channel);
        published.ClipId.Should().Be("new-1");
        published.Title.Should().Be("title-new-1");
        published.CreatorName.Should().Be("creator-new-1");
        published.Url.Should().Be("https://clips.twitch.tv/new-1");
        published.CreatedAt.Should().Be(madeAt);
    }

    [Fact]
    public async Task The_same_clip_on_a_third_read_sends_nothing_more()
    {
        Rig rig = Build();
        await rig.PassAsync();
        rig.Existing.Add(Clip("new-1", StreamStart.AddMinutes(11)));
        await rig.PassAsync();

        await rig.PassAsync();

        rig.Sent.Should().HaveCount(1);
        rig.Published.Should().HaveCount(1);
        rig.Reads.Should().Be(3);
    }

    [Fact]
    public async Task Two_new_clips_go_out_oldest_first()
    {
        Rig rig = Build();
        await rig.PassAsync();
        rig.Existing.Add(Clip("newer", StreamStart.AddMinutes(12)));
        rig.Existing.Add(Clip("older", StreamStart.AddMinutes(11)));

        await rig.PassAsync();

        rig.Sent.Select(v => v["clip.title"]).Should().Equal("title-older", "title-newer");
        rig.Published.Select(e => e.ClipId).Should().Equal("older", "newer");
    }

    [Fact]
    public async Task A_clip_created_before_the_stream_started_sends_nothing()
    {
        Rig rig = Build();
        await rig.PassAsync();
        rig.Existing.Add(Clip("last-stream", StreamStart.AddMinutes(-30)));

        await rig.PassAsync();

        rig.Sent.Should().BeEmpty();
        rig.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_read_sends_nothing_and_a_later_good_read_still_posts_the_new_clip()
    {
        Rig rig = Build();
        rig.Existing.Add(Clip("old-1", StreamStart.AddMinutes(3)));
        await rig.PassAsync();
        rig.Existing.Add(Clip("new-1", StreamStart.AddMinutes(11)));
        rig.ReadFails = true;

        await rig.PassAsync();

        rig.Sent.Should().BeEmpty();
        rig.Published.Should().BeEmpty();

        rig.ReadFails = false;
        await rig.PassAsync();

        rig.Sent.Should().HaveCount(1);
        rig.Sent[0]["clip.title"].Should().Be("title-new-1");
        rig.Published.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_failed_first_read_does_not_count_as_the_first_read_so_clips_that_existed_stay_quiet()
    {
        Rig rig = Build();
        rig.Existing.Add(Clip("old-1", StreamStart.AddMinutes(3)));
        rig.ReadFails = true;
        await rig.PassAsync();
        rig.ReadFails = false;

        await rig.PassAsync();

        rig.Sent.Should().BeEmpty();
        rig.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task No_read_happens_within_60_seconds_of_the_last_one()
    {
        Rig rig = Build();
        await rig.PassAsync();

        await rig.PassAsync(5);
        await rig.PassAsync(30);
        await rig.PassAsync(24);
        rig.Reads.Should().Be(1);

        await rig.PassAsync(1);
        rig.Reads.Should().Be(2);
    }

    [Fact]
    public async Task A_channel_that_goes_offline_and_live_again_learns_its_clips_again_without_telling_chat()
    {
        Rig rig = Build();
        await rig.PassAsync();
        rig.Context.IsLive = false;
        await rig.PassAsync();
        rig.Reads.Should().Be(1, "an offline channel costs no Helix read");

        DateTimeOffset secondStart = StreamStart.AddHours(3);
        rig.Context.IsLive = true;
        rig.Context.WentLiveAt = secondStart;
        rig.Existing.Add(Clip("early-second", secondStart.AddMinutes(2)));
        await rig.PassAsync();

        rig.Sent.Should().BeEmpty();
        rig.Reads.Should().Be(2);
    }
}
