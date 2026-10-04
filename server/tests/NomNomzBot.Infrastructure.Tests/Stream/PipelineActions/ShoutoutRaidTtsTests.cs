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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Stream.PipelineActions;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Stream.PipelineActions;

/// <summary>
/// Old-bot parity for the raid shoutout: when a <c>channel.raid</c> event runs a shoutout step, the raider
/// gets their own customised line in chat AND spoken with TTS, even when the step never sets <c>tts</c>, and
/// even inside the Twitch cooldowns (only the native Helix call is skipped then — the legacy
/// <c>skipApiCall</c> rule). An automated run that is not a raid stays silent, and an explicit
/// <c>tts: false</c> always wins.
/// </summary>
public sealed class ShoutoutRaidTtsTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000b301");
    private const string RaiderId = "777001";
    private const string RaiderTemplate = "Raid hero {target.name}, go follow {target.link}!";

    private sealed class Rig
    {
        public required ShoutoutAction Sut { get; init; }
        public required ITwitchChatApi Chat { get; init; }
        public required ITtsDispatchService Tts { get; init; }
        public required ChannelContext ChannelCtx { get; init; }
    }

    private static string NaiveResolve(NSubstitute.Core.CallInfo callInfo)
    {
        string template = (string)callInfo[0];
        IDictionary<string, string> vars = (IDictionary<string, string>)callInfo[1];
        foreach (KeyValuePair<string, string> kv in vars)
            template = template.Replace("{" + kv.Key + "}", kv.Value);
        return template;
    }

    private static async Task<Rig> BuildAsync()
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
            .Returns(
                Result.Success<IReadOnlyList<TwitchUser>>([
                    new TwitchUser(
                        Id: RaiderId,
                        Login: "raider",
                        DisplayName: "RaiderName",
                        Type: "",
                        BroadcasterType: "",
                        Description: "",
                        ProfileImageUrl: "",
                        OfflineImageUrl: "",
                        ViewCount: 0,
                        CreatedAt: DateTimeOffset.UnixEpoch
                    ),
                ])
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
            }
        );
        db.ShoutoutOverrides.Add(
            new()
            {
                BroadcasterId = Channel,
                TargetTwitchUserId = RaiderId,
                TargetDisplayName = "RaiderName",
                MessageTemplate = RaiderTemplate,
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

        ShoutoutAction sut = new(
            chat,
            users,
            registry,
            db,
            resolver,
            tts,
            TimeProvider.System,
            NullLogger<ShoutoutAction>.Instance
        );
        return new()
        {
            Sut = sut,
            Chat = chat,
            Tts = tts,
            ChannelCtx = channelCtx,
        };
    }

    /// <summary>A run started by an event response: no chat message id, the event name in the variables.</summary>
    private static PipelineExecutionContext EventCtx(string eventName)
    {
        PipelineExecutionContext ctx = new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = RaiderId,
            TriggeredByDisplayName = "RaiderName",
            MessageId = string.Empty,
            RawMessage = string.Empty,
        };
        ctx.Variables["event.name"] = eventName;
        return ctx;
    }

    private static ActionDefinition Step(bool? tts)
    {
        ActionDefinition action = new()
        {
            Type = "shoutout",
            Parameters = new() { ["user_id"] = JsonSerializer.SerializeToElement(RaiderId) },
        };
        if (tts.HasValue)
            action.Parameters["tts"] = JsonSerializer.SerializeToElement(tts.Value);
        return action;
    }

    private static Task AssertRaiderLineSpokenAsync(Rig rig) =>
        rig
            .Tts.Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r =>
                    r.Text == "Raid hero RaiderName, go follow twitch.tv/raider!"
                    && r.RequestedByTwitchUserId == RaiderId
                ),
                Arg.Any<CancellationToken>()
            );

    private static Task AssertRaiderLineAnnouncedAsync(Rig rig) =>
        rig
            .Chat.Received(1)
            .SendAnnouncementAsync(
                Channel,
                "Raid hero RaiderName, go follow twitch.tv/raider!",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );

    [Fact]
    public async Task A_raid_shoutout_with_tts_unset_posts_the_raiders_own_line_and_speaks_it()
    {
        Rig rig = await BuildAsync();

        ActionResult result = await rig.Sut.ExecuteAsync(EventCtx("channel.raid"), Step(null));

        result.Succeeded.Should().BeTrue();
        await AssertRaiderLineAnnouncedAsync(rig);
        await AssertRaiderLineSpokenAsync(rig);
        await rig
            .Chat.Received(1)
            .SendShoutoutAsync(Channel, RaiderId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_raid_shoutout_inside_the_global_cooldown_still_announces_and_speaks_but_skips_the_native_call()
    {
        Rig rig = await BuildAsync();
        rig.ChannelCtx.LastGlobalShoutout = TimeProvider.System.GetUtcNow();

        ActionResult result = await rig.Sut.ExecuteAsync(EventCtx("channel.raid"), Step(null));

        result.Succeeded.Should().BeTrue();
        await rig.Chat.DidNotReceiveWithAnyArgs().SendShoutoutAsync(default, default!);
        await AssertRaiderLineAnnouncedAsync(rig);
        await AssertRaiderLineSpokenAsync(rig);
    }

    [Fact]
    public async Task A_raid_shoutout_inside_the_per_user_cooldown_still_announces_and_speaks_but_skips_the_native_call()
    {
        Rig rig = await BuildAsync();
        rig.ChannelCtx.LastShoutoutPerUser[RaiderId] = TimeProvider.System.GetUtcNow();

        ActionResult result = await rig.Sut.ExecuteAsync(EventCtx("channel.raid"), Step(null));

        result.Succeeded.Should().BeTrue();
        await rig.Chat.DidNotReceiveWithAnyArgs().SendShoutoutAsync(default, default!);
        await AssertRaiderLineAnnouncedAsync(rig);
        await AssertRaiderLineSpokenAsync(rig);
    }

    [Fact]
    public async Task An_automated_shoutout_that_is_not_a_raid_stays_silent_when_tts_is_unset()
    {
        Rig rig = await BuildAsync();

        ActionResult result = await rig.Sut.ExecuteAsync(EventCtx("channel.follow"), Step(null));

        result.Succeeded.Should().BeTrue();
        await AssertRaiderLineAnnouncedAsync(rig);
        await rig.Tts.DidNotReceiveWithAnyArgs().RequestSpeakAsync(default!);
    }

    [Fact]
    public async Task An_explicit_tts_false_stays_silent_even_on_a_raid()
    {
        Rig rig = await BuildAsync();

        ActionResult result = await rig.Sut.ExecuteAsync(EventCtx("channel.raid"), Step(false));

        result.Succeeded.Should().BeTrue();
        await AssertRaiderLineAnnouncedAsync(rig);
        await rig.Tts.DidNotReceiveWithAnyArgs().RequestSpeakAsync(default!);
    }
}
