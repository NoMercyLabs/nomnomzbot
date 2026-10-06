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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Chat;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Stream;
using NomNomzBot.Infrastructure.Stream.AutoShoutout;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tests.Stream.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Stream.AutoShoutout;

/// <summary>
/// Old-bot parity for the automatic shoutout (ShoutoutQueueService): a known streamer's first chat of a stream
/// is shouted out only on a channel with the setting on, only after 10 minutes of stream, 5 minutes after the
/// chat, once the 8 minute global floor passed, with nothing queued ahead, silent, and never inside the 1 hour
/// per-user cooldown. The shoutout itself runs through the real <c>ShoutoutAction</c> and sender.
/// </summary>
public sealed class AutoShoutoutSchedulerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000c101");
    private static readonly DateTimeOffset StreamStart = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private const string BroadcasterTwitchId = "tw-channel";
    private const string BotTwitchId = "tw-bot";
    private const string StreamerA = "1001";
    private const string StreamerB = "1002";
    private const string Stranger = "1003";
    private const string DisabledStreamer = "1004";

    private sealed class Rig
    {
        public required AutoShoutoutScheduler Sut { get; init; }
        public required FakeTimeProvider Clock { get; init; }
        public required ITwitchChatApi Chat { get; init; }
        public required ITtsDispatchService Tts { get; init; }
        public required ChannelContext ChannelCtx { get; init; }
        public required IShoutoutQueue Queue { get; init; }
        public required List<OutboundSanction?> SeenSanctions { get; init; }

        public Task ChatAsync(string userId) =>
            Sut.TryEnqueueAsync(Channel, userId, userId, CancellationToken.None);

        public Task TickAtAsync(int minute)
        {
            Clock.SetUtcNow(StreamStart.AddMinutes(minute));
            return Sut.ProcessDueAsync(CancellationToken.None);
        }

        public int Shoutouts =>
            Chat.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "SendShoutoutAsync");

        public int Announcements =>
            Chat.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "SendAnnouncementAsync");
    }

    private static TwitchUser User(string id) =>
        new(
            Id: id,
            Login: id,
            DisplayName: id,
            Type: "",
            BroadcasterType: "",
            Description: "",
            ProfileImageUrl: "",
            OfflineImageUrl: "",
            ViewCount: 0,
            CreatedAt: DateTimeOffset.UnixEpoch
        );

    private static string NaiveResolve(NSubstitute.Core.CallInfo callInfo)
    {
        string template = (string)callInfo[0];
        IDictionary<string, string> vars = (IDictionary<string, string>)callInfo[1];
        foreach (KeyValuePair<string, string> kv in vars)
            template = template.Replace("{" + kv.Key + "}", kv.Value);
        return template;
    }

    private static NomNomzBot.Domain.Identity.Entities.Channel StreamerRow(
        string twitchId,
        bool enabled
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = twitchId,
            NameNormalized = twitchId,
            OwnerUserId = Guid.NewGuid(),
            TwitchChannelId = twitchId,
            ExternalChannelId = twitchId,
            Enabled = enabled,
        };

    private static async Task<Rig> BuildAsync(bool settingOn = true)
    {
        FakeTimeProvider clock = new(StreamStart);

        OutboundSanctionAccessor sanctions = new();
        List<OutboundSanction?> seenSanctions = [];
        ITwitchChatApi chat = Substitute.For<ITwitchChatApi>();
        chat.SendShoutoutAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        chat.SendAnnouncementAsync(
                Channel,
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                seenSanctions.Add(sanctions.Current);
                return Result.Success();
            });

        ITwitchUsersApi users = Substitute.For<ITwitchUsersApi>();
        users
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
                Result.Success<IReadOnlyList<TwitchUser>>(
                    ((IReadOnlyList<string>)callInfo[0]).Select(User).ToList()
                )
            );

        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo => Task.FromResult(NaiveResolve(callInfo)));

        ITtsDispatchService tts = Substitute.For<ITtsDispatchService>();
        tts.RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TtsDispatchOutcome(TtsDispatchDisposition.Dispatched, "v", "p", 0, 0, null)
                )
            );

        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                Name = "stoney",
                NameNormalized = "stoney",
                OwnerUserId = Guid.NewGuid(),
                TwitchChannelId = BroadcasterTwitchId,
                ExternalChannelId = BroadcasterTwitchId,
                AutoShoutoutEnabled = settingOn,
            }
        );
        foreach (string id in new[] { StreamerA, StreamerB, BotTwitchId })
            db.Channels.Add(StreamerRow(id, enabled: true));
        db.Channels.Add(StreamerRow(DisabledStreamer, enabled: false));
        await db.SaveChangesAsync();

        ChannelContext channelCtx = new()
        {
            BroadcasterId = Channel,
            TwitchChannelId = BroadcasterTwitchId,
            ChannelName = "stoney",
            IsLive = true,
            WentLiveAt = StreamStart,
        };
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Channel).Returns(channelCtx);

        IShoutoutQueue queue = new ShoutoutQueue();
        ICommandAction action = ShoutoutTestFactory.Create(
            chat,
            users,
            registry,
            db,
            resolver,
            tts,
            clock,
            queue
        );

        IBotSelfEchoGuard guard = Substitute.For<IBotSelfEchoGuard>();
        guard
            .ShouldSuppressAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                BotTwitchId,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);

        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddSingleton(action)
            .BuildServiceProvider();

        AutoShoutoutScheduler sut = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            registry,
            queue,
            sanctions,
            guard,
            clock,
            NullLogger<AutoShoutoutScheduler>.Instance
        );

        return new()
        {
            SeenSanctions = seenSanctions,
            Sut = sut,
            Clock = clock,
            Chat = chat,
            Tts = tts,
            ChannelCtx = channelCtx,
            Queue = queue,
        };
    }

    [Fact]
    public async Task With_the_setting_off_nothing_ever_fires()
    {
        Rig rig = await BuildAsync(settingOn: false);

        await rig.TickAtAsync(12);
        await rig.ChatAsync(StreamerA);
        await rig.TickAtAsync(17);
        await rig.TickAtAsync(40);

        rig.Shoutouts.Should().Be(0);
        rig.Announcements.Should().Be(0);
    }

    [Fact]
    public async Task A_chat_at_minute_12_fires_one_native_shoutout_and_one_announcement_at_minute_17()
    {
        Rig rig = await BuildAsync();

        await rig.TickAtAsync(12);
        await rig.ChatAsync(StreamerA);
        await rig.TickAtAsync(16);
        rig.Shoutouts.Should().Be(0);
        rig.Announcements.Should().Be(0);

        await rig.TickAtAsync(17);

        await rig
            .Chat.Received(1)
            .SendShoutoutAsync(Channel, StreamerA, Arg.Any<CancellationToken>());
        rig.Announcements.Should().Be(1);
        rig.ChannelCtx.LastShoutoutPerUser.Should().ContainKey(StreamerA);

        await rig.TickAtAsync(30);
        rig.Shoutouts.Should().Be(1);
        rig.Announcements.Should().Be(1);
    }

    [Fact]
    public async Task An_auto_shoutout_posts_under_the_channel_configuration_sanction()
    {
        Rig rig = await BuildAsync();
        await rig.TickAtAsync(2);
        await rig.ChatAsync(StreamerA);

        await rig.TickAtAsync(17);

        OutboundSanction? sanction = rig.SeenSanctions.Should().ContainSingle().Subject;
        sanction!.Basis.Should().Be(OutboundSanctionBasis.ChannelConfiguration);
        sanction.Detail.Should().Be("channel:auto-shoutout");
    }

    [Fact]
    public async Task The_stream_must_be_10_minutes_old_before_an_auto_shoutout_fires()
    {
        Rig rig = await BuildAsync();

        await rig.TickAtAsync(2);
        await rig.ChatAsync(StreamerA);
        await rig.TickAtAsync(8);
        await rig.TickAtAsync(9);
        rig.Shoutouts.Should().Be(0);

        await rig.TickAtAsync(10);

        rig.Shoutouts.Should().Be(1);
    }

    [Fact]
    public async Task A_chatter_who_is_no_streamer_or_has_a_disabled_channel_is_never_shouted_out()
    {
        Rig rig = await BuildAsync();

        await rig.TickAtAsync(12);
        await rig.ChatAsync(Stranger);
        await rig.ChatAsync(DisabledStreamer);
        await rig.TickAtAsync(17);
        await rig.TickAtAsync(40);

        rig.Shoutouts.Should().Be(0);
        rig.Announcements.Should().Be(0);
    }

    [Fact]
    public async Task An_auto_shoutout_is_silent_and_requests_no_tts()
    {
        Rig rig = await BuildAsync();

        await rig.TickAtAsync(12);
        await rig.ChatAsync(StreamerA);
        await rig.TickAtAsync(17);

        rig.Shoutouts.Should().Be(1);
        await rig.Tts.DidNotReceiveWithAnyArgs().RequestSpeakAsync(default!);
    }

    [Fact]
    public async Task Two_streamers_eligible_at_one_tick_fire_one_at_a_time_8_minutes_apart()
    {
        Rig rig = await BuildAsync();

        await rig.TickAtAsync(12);
        await rig.ChatAsync(StreamerA);
        await rig.ChatAsync(StreamerB);
        await rig.TickAtAsync(17);
        rig.Shoutouts.Should().Be(1);
        await rig
            .Chat.Received(1)
            .SendShoutoutAsync(Channel, StreamerA, Arg.Any<CancellationToken>());

        await rig.TickAtAsync(24);
        rig.Shoutouts.Should().Be(1);

        await rig.TickAtAsync(25);
        rig.Shoutouts.Should().Be(2);
        await rig
            .Chat.Received(1)
            .SendShoutoutAsync(Channel, StreamerB, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_manual_shoutout_3_minutes_earlier_holds_the_auto_until_the_8_minute_floor_passed()
    {
        Rig rig = await BuildAsync();

        await rig.TickAtAsync(12);
        await rig.ChatAsync(StreamerA);
        rig.ChannelCtx.LastGlobalShoutout = StreamStart.AddMinutes(14);

        await rig.TickAtAsync(17);
        await rig.TickAtAsync(21);
        rig.Shoutouts.Should().Be(0);

        await rig.TickAtAsync(22);

        rig.Shoutouts.Should().Be(1);
    }

    [Fact]
    public async Task A_manual_or_raid_shoutout_waiting_in_the_queue_holds_the_auto()
    {
        Rig rig = await BuildAsync();
        await rig.TickAtAsync(12);
        await rig.ChatAsync(StreamerA);
        rig.Queue.Enqueue(
            new QueuedShoutout(
                Channel,
                User("tw-raider"),
                "tw-mod",
                true,
                TimeSpan.FromMinutes(2),
                TimeSpan.FromMinutes(60),
                StreamStart
            )
        );

        await rig.TickAtAsync(17);
        await rig.TickAtAsync(30);
        rig.Shoutouts.Should().Be(0);

        rig.Queue.Remove(Channel, "tw-raider");
        await rig.TickAtAsync(31);

        rig.Shoutouts.Should().Be(1);
    }

    [Fact]
    public async Task A_streamer_shouted_out_30_minutes_ago_is_dropped_without_an_announcement()
    {
        Rig rig = await BuildAsync();
        await rig.TickAtAsync(12);
        await rig.ChatAsync(StreamerA);
        rig.ChannelCtx.LastShoutoutPerUser[StreamerA] = StreamStart.AddMinutes(-13);

        await rig.TickAtAsync(17);
        rig.Shoutouts.Should().Be(0);
        rig.Announcements.Should().Be(0);

        rig.ChannelCtx.LastShoutoutPerUser.Clear();
        await rig.TickAtAsync(40);
        rig.Shoutouts.Should().Be(0);
        rig.Announcements.Should().Be(0);
    }

    [Fact]
    public async Task One_streamer_chatting_twice_gets_one_shoutout_for_the_whole_stream()
    {
        Rig rig = await BuildAsync();

        await rig.TickAtAsync(12);
        await rig.ChatAsync(StreamerA);
        await rig.ChatAsync(StreamerA);
        await rig.TickAtAsync(17);
        await rig.ChatAsync(StreamerA);
        rig.ChannelCtx.LastShoutoutPerUser.Clear();
        rig.ChannelCtx.LastGlobalShoutout = null;
        await rig.TickAtAsync(40);

        rig.Shoutouts.Should().Be(1);
        rig.Announcements.Should().Be(1);
    }

    [Fact]
    public async Task Going_offline_clears_the_pending_list_and_a_new_stream_allows_a_new_shoutout()
    {
        Rig rig = await BuildAsync();
        await rig.TickAtAsync(12);
        await rig.ChatAsync(StreamerA);

        rig.ChannelCtx.IsLive = false;
        rig.ChannelCtx.WentLiveAt = null;
        await rig.TickAtAsync(13);

        rig.ChannelCtx.IsLive = true;
        rig.ChannelCtx.WentLiveAt = StreamStart.AddMinutes(14);
        await rig.TickAtAsync(40);
        rig.Shoutouts.Should().Be(0);

        await rig.ChatAsync(StreamerA);
        await rig.TickAtAsync(45);
        rig.Shoutouts.Should().Be(1);
    }

    [Fact]
    public async Task The_broadcaster_and_the_bot_never_queue()
    {
        Rig rig = await BuildAsync();

        await rig.TickAtAsync(12);
        await rig.ChatAsync(BroadcasterTwitchId);
        await rig.ChatAsync(BotTwitchId);
        await rig.TickAtAsync(17);
        await rig.TickAtAsync(40);

        rig.Shoutouts.Should().Be(0);
        rig.Announcements.Should().Be(0);
    }
}
