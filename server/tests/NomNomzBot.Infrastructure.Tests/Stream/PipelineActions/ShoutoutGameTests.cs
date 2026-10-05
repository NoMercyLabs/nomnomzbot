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
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Stream;
using NomNomzBot.Infrastructure.Stream.PipelineActions;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Stream.PipelineActions;

/// <summary>
/// Proves a shoutout fills <c>{target.game}</c> with the target channel's current Twitch category (old-bot
/// parity): the category when there is one, "something awesome" when it is empty or the lookup fails, and the
/// same value for a shoutout that waits in the queue.
/// </summary>
public sealed class ShoutoutGameTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000b301");
    private const string TargetId = "123456";
    private const string Template = "Playing {target.game} with {target.name}";

    private sealed record Rig(
        ShoutoutAction Action,
        ITwitchChatApi Chat,
        ITwitchChannelsApi Channels,
        ShoutoutQueue Queue,
        ChannelContext ChannelCtx
    );

    private static TwitchUser User() =>
        new(
            Id: TargetId,
            Login: "numerictarget",
            DisplayName: "numerictarget",
            Type: "",
            BroadcasterType: "",
            Description: "",
            ProfileImageUrl: "",
            OfflineImageUrl: "",
            ViewCount: 0,
            CreatedAt: DateTimeOffset.UnixEpoch
        );

    private static TwitchChannelInformation Info(string gameName) =>
        new(
            TargetId,
            "numerictarget",
            "numerictarget",
            "en",
            "509658",
            gameName,
            "title",
            0,
            [],
            [],
            false
        );

    private static ITwitchChannelsApi ChannelsReturning(
        Result<IReadOnlyList<TwitchChannelInformation>> result
    )
    {
        ITwitchChannelsApi channels = Substitute.For<ITwitchChannelsApi>();
        channels
            .GetChannelInformationByTwitchIdsAsync(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(result));
        return channels;
    }

    private static Result<IReadOnlyList<TwitchChannelInformation>> Found(string gameName) =>
        Result.Success<IReadOnlyList<TwitchChannelInformation>>([Info(gameName)]);

    private static async Task<Rig> BuildAsync(ITwitchChannelsApi channels)
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
        ITwitchUsersApi users = Substitute.For<ITwitchUsersApi>();
        users
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([User()]));
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
                string template = (string)call[0];
                foreach (KeyValuePair<string, string> kv in (IDictionary<string, string>)call[1])
                    template = template.Replace("{" + kv.Key + "}", kv.Value);
                return Task.FromResult(template);
            });

        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                Name = "stoney",
                NameNormalized = "stoney",
                OwnerUserId = Guid.NewGuid(),
                ShoutoutTemplate = Template,
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

        ShoutoutAction action = ShoutoutTestFactory.Create(
            chat,
            users,
            registry,
            db,
            resolver,
            Substitute.For<ITtsDispatchService>(),
            TimeProvider.System,
            queue,
            channels
        );
        return new(action, chat, channels, queue, channelCtx);
    }

    private static PipelineExecutionContext Ctx() =>
        new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "tw-1",
            TriggeredByDisplayName = "Viewer",
            MessageId = "m1",
            RawMessage = "!so target",
        };

    private static ActionDefinition Step() =>
        new()
        {
            Type = "shoutout",
            Parameters = new() { ["user_id"] = JsonSerializer.SerializeToElement(TargetId) },
        };

    private static string Announced(Rig rig) =>
        (string)
            rig
                .Chat.ReceivedCalls()
                .Single(c => c.GetMethodInfo().Name == nameof(ITwitchChatApi.SendAnnouncementAsync))
                .GetArguments()[1]!;

    [Fact]
    public async Task A_target_with_a_category_gets_it_in_the_announcement()
    {
        Rig rig = await BuildAsync(ChannelsReturning(Found("Software and Game Development")));

        ActionResult result = await rig.Action.ExecuteAsync(Ctx(), Step());

        result.Succeeded.Should().BeTrue();
        Announced(rig).Should().Be("Playing Software and Game Development with numerictarget");
        await rig
            .Channels.Received(1)
            .GetChannelInformationByTwitchIdsAsync(
                Arg.Is<IReadOnlyList<string>>(ids => ids.Single() == TargetId),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_target_with_an_empty_category_gets_something_awesome()
    {
        Rig rig = await BuildAsync(ChannelsReturning(Found("")));

        await rig.Action.ExecuteAsync(Ctx(), Step());

        Announced(rig).Should().Be("Playing something awesome with numerictarget");
    }

    [Fact]
    public async Task A_failed_helix_lookup_still_shouts_out_with_something_awesome()
    {
        Rig rig = await BuildAsync(
            ChannelsReturning(
                Result.Failure<IReadOnlyList<TwitchChannelInformation>>(
                    "boom",
                    TwitchErrorCodes.TwitchError
                )
            )
        );

        ActionResult result = await rig.Action.ExecuteAsync(Ctx(), Step());

        result.Succeeded.Should().BeTrue();
        Announced(rig).Should().Be("Playing something awesome with numerictarget");
        await rig
            .Chat.Received(1)
            .SendShoutoutAsync(Channel, TargetId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_queued_shoutout_already_holds_the_game_in_its_announcement()
    {
        Rig rig = await BuildAsync(ChannelsReturning(Found("Just Chatting")));
        rig.ChannelCtx.LastGlobalShoutout = DateTimeOffset.UtcNow;

        ActionResult result = await rig.Action.ExecuteAsync(Ctx(), Step());

        result.Output.Should().Be("queued (global cooldown)");
        rig.Queue.Peek(Channel)!
            .Announcement.Should()
            .Be("Playing Just Chatting with numerictarget");
    }
}
