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
using FluentAssertions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Stream;
using NomNomzBot.Infrastructure.Stream.PipelineActions;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Stream.PipelineActions;

/// <summary>
/// The owner's rule: a manual shoutout posts its announcement and speaks it AT ONCE, every time. Only the
/// native Twitch /shoutout waits in the queue for Twitch's cooldown. The seeded target state (game, title,
/// live) comes from the TARGET's own channel, with one streams lookup when the target is live.
/// </summary>
public sealed class ShoutoutAnnounceNowTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000b3c1");
    private const string TargetId = "123456";

    private sealed record Rig(
        ShoutoutAction Action,
        ITwitchChatApi Chat,
        IChatProvider BotChat,
        ITtsDispatchService Tts,
        ITwitchChannelsApi Channels,
        ITwitchStreamsApi Streams,
        ShoutoutQueue Queue,
        ChannelContext ChannelCtx,
        List<IDictionary<string, string>> Seeds
    );

    private static TwitchUser User() =>
        new(
            Id: TargetId,
            Login: "numerictarget",
            DisplayName: "NumericTarget",
            Type: "",
            BroadcasterType: "",
            Description: "",
            ProfileImageUrl: "",
            OfflineImageUrl: "",
            ViewCount: 0,
            CreatedAt: DateTimeOffset.UnixEpoch
        );

    private static ITwitchStreamsApi Live(string game, string title)
    {
        ITwitchStreamsApi streams = Substitute.For<ITwitchStreamsApi>();
        TwitchStream stream = new(
            "s1",
            TargetId,
            "numerictarget",
            "NumericTarget",
            "1",
            game,
            "live",
            title,
            [],
            5,
            DateTimeOffset.UnixEpoch,
            "en",
            "",
            false
        );
        streams
            .GetStreamsAsync(
                Arg.Any<TwitchStreamsFilter>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Task.FromResult(Result.Success(new TwitchPage<TwitchStream>([stream], null, 1)))
            );
        return streams;
    }

    private static ITwitchChannelsApi Info(string game, string title)
    {
        ITwitchChannelsApi channels = Substitute.For<ITwitchChannelsApi>();
        channels
            .GetChannelInformationByTwitchIdsAsync(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Task.FromResult(
                    Result.Success<IReadOnlyList<TwitchChannelInformation>>([
                        new(
                            TargetId,
                            "numerictarget",
                            "NumericTarget",
                            "en",
                            "1",
                            game,
                            title,
                            0,
                            [],
                            [],
                            false
                        ),
                    ])
                )
            );
        return channels;
    }

    private static async Task<Rig> BuildAsync(
        ITwitchStreamsApi? streams = null,
        ITwitchChannelsApi? channels = null
    )
    {
        ITwitchChatApi chat = Substitute.For<ITwitchChatApi>();
        chat.SendShoutoutAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        chat.SendAnnouncementAsync(
                Channel,
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        IChatProvider botChat = Substitute.For<IChatProvider>();
        botChat
            .SendMessageAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        ITwitchUsersApi users = Substitute.For<ITwitchUsersApi>();
        users
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([User()]));

        List<IDictionary<string, string>> seeds = [];
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                IDictionary<string, string> seed = (IDictionary<string, string>)call[1];
                seeds.Add(new Dictionary<string, string>(seed));
                string template = (string)call[0];
                foreach (KeyValuePair<string, string> kv in seed)
                    template = template.Replace("{" + kv.Key + "}", kv.Value);
                return Task.FromResult(template);
            });
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
                ShoutoutTemplate = "{target.name} plays {game} ({title}) {status}",
            }
        );
        await db.SaveChangesAsync();

        ChannelContext channelCtx = new()
        {
            BroadcasterId = Channel,
            TwitchChannelId = "tw-channel",
            ChannelName = "stoney",
        };
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Channel).Returns(channelCtx);
        ShoutoutQueue queue = new();
        ITwitchStreamsApi streamsApi = streams ?? ShoutoutTestFactory.NoStream();
        ITwitchChannelsApi channelsApi = channels ?? ShoutoutTestFactory.NoChannelInfo();
        ShoutoutAction action = ShoutoutTestFactory.Create(
            chat,
            users,
            registry,
            db,
            resolver,
            tts,
            TimeProvider.System,
            queue,
            channelsApi,
            botChat,
            streamsApi
        );
        return new(action, chat, botChat, tts, channelsApi, streamsApi, queue, channelCtx, seeds);
    }

    private static PipelineExecutionContext Manual() =>
        new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "tw-1",
            TriggeredByDisplayName = "Viewer",
            MessageId = "m1",
            RawMessage = "!so numerictarget",
        };

    private static ActionDefinition Step() =>
        new()
        {
            Type = "shoutout",
            Parameters = new()
            {
                ["user_id"] = JsonSerializer.SerializeToElement(TargetId),
                ["tts"] = JsonSerializer.SerializeToElement(true),
            },
        };

    [Fact]
    public async Task A_live_target_fills_game_title_status_and_live_state_from_one_streams_lookup()
    {
        Rig rig = await BuildAsync(Live("Chess", "Rated blitz"), Info("Old category", "Old title"));

        await rig.Action.ExecuteAsync(Manual(), Step());

        IDictionary<string, string> seed = rig.Seeds.Single();
        seed["game"].Should().Be("Chess");
        seed["title"].Should().Be("Rated blitz");
        seed["status"].Should().Be("live");
        seed["target.isLive"].Should().Be("true");
        await rig
            .Channels.DidNotReceiveWithAnyArgs()
            .GetChannelInformationByTwitchIdsAsync(default!, default);
    }

    [Fact]
    public async Task An_offline_target_gets_its_last_category_and_title_and_an_offline_state()
    {
        Rig rig = await BuildAsync(channels: Info("Go", "Late night"));
        rig.ChannelCtx.IsLive = true; // the broadcaster is live; the target is not

        await rig.Action.ExecuteAsync(Manual(), Step());

        IDictionary<string, string> seed = rig.Seeds.Single();
        seed["game"].Should().Be("Go");
        seed["title"].Should().Be("Late night");
        seed["status"].Should().Be("offline");
        seed["target.isLive"].Should().Be("false");
    }

    [Fact]
    public async Task A_target_with_no_category_anywhere_gets_something_awesome()
    {
        Rig rig = await BuildAsync();

        await rig.Action.ExecuteAsync(Manual(), Step());

        rig.Seeds.Single()["game"].Should().Be("something awesome");
    }

    [Fact]
    public async Task During_the_global_cooldown_the_announcement_and_tts_go_out_now_and_only_the_native_call_waits()
    {
        Rig rig = await BuildAsync(Live("Chess", "Rated blitz"));
        rig.ChannelCtx.LastGlobalShoutout = DateTimeOffset.UtcNow;

        ActionResult result = await rig.Action.ExecuteAsync(Manual(), Step());

        result.Succeeded.Should().BeTrue();
        result.Output.Should().Be("queued (global cooldown)");
        const string line = "NumericTarget plays Chess (Rated blitz) live";
        await rig
            .Chat.Received(1)
            .SendAnnouncementAsync(Channel, line, Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await rig
            .Tts.Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r => r.Text == line),
                Arg.Any<CancellationToken>()
            );
        await rig.Chat.DidNotReceiveWithAnyArgs().SendShoutoutAsync(default, default!);
        QueuedShoutout queued = rig.Queue.Peek(Channel)!;
        queued.Target.Id.Should().Be(TargetId);
        await rig
            .BotChat.Received(1)
            .SendMessageAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_queued_native_call_does_not_stamp_the_cooldown_it_is_waiting_for()
    {
        Rig rig = await BuildAsync();
        DateTimeOffset stamp = DateTimeOffset.UtcNow.AddSeconds(-30);
        rig.ChannelCtx.LastGlobalShoutout = stamp;

        await rig.Action.ExecuteAsync(Manual(), Step());

        rig.ChannelCtx.LastGlobalShoutout.Should().Be(stamp);
        rig.ChannelCtx.LastShoutoutPerUser.Should().NotContainKey(TargetId);
    }
}
