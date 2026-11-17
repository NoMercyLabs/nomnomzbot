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

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.DTOs.Twitch.EventSub;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Platform.Eventing.Translators;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tests.Platform.Eventing.Translators;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing;

/// <summary>
/// A channel whose broadcaster did not onboard (<c>Channels.IsOnboarded</c> false) may have its chat read and
/// stored, but the bot must never act there: no command, automation or reply. Drives a real chat notification
/// through the real translator and the real event bus, and a real send through the real chat router.
/// </summary>
public sealed class NonOnboardedChannelSilenceTests
{
    private static readonly Guid ClosedTenant = Guid.Parse("0192a000-0000-7000-8000-0000000000d1");
    private static readonly Guid OpenTenant = Guid.Parse("0192a000-0000-7000-8000-0000000000d2");
    private static readonly Guid Owner = Guid.Parse("0192a000-0000-7000-8000-0000000000d9");
    private static readonly FakeTimeProvider Clock = new(
        new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero)
    );

    private sealed class ActingProbe : IEventHandler<ChatMessageReceivedEvent>
    {
        public List<string> Commands { get; } = [];

        public Task HandleAsync(ChatMessageReceivedEvent @event, CancellationToken ct = default)
        {
            Commands.Add(@event.Message);
            return Task.CompletedTask;
        }
    }

    private sealed class StoringProbe
        : IEventHandler<ChatMessageReceivedEvent>,
            IOnboardingExemptHandler
    {
        public List<string> Stored { get; } = [];

        public Task HandleAsync(ChatMessageReceivedEvent @event, CancellationToken ct = default)
        {
            Stored.Add(@event.MessageId);
            return Task.CompletedTask;
        }
    }

    private static readonly Guid ModUser = Guid.Parse("0192a000-0000-7000-8000-0000000000e1");
    private static readonly Guid ModChannel = Guid.Parse("0192a000-0000-7000-8000-0000000000e2");

    // A user who owns an onboarded channel of their own (the "onboarded user" of the open rule).
    private static Channel OnboardedModeratorHome() =>
        Channel(ModChannel, "modhome1", onboarded: true, ModUser);

    private static ChannelMembership Membership(Guid channel, Guid user) =>
        new()
        {
            BroadcasterId = channel,
            UserId = user,
            ManagementRole = ManagementRole.Moderator,
            LevelValue = PermissionLevel.Moderator.ToLevelValue(),
            Source = MembershipSource.TwitchBadge,
            GrantedAt = Clock.GetUtcNow().UtcDateTime,
        };

    private static Channel Channel(
        Guid id,
        string externalId,
        bool onboarded,
        Guid? ownerUserId = null
    ) =>
        new()
        {
            Id = id,
            OwnerUserId = ownerUserId ?? Owner,
            Provider = AuthEnums.Platform.Twitch,
            ExternalChannelId = externalId,
            TwitchChannelId = externalId,
            Name = externalId,
            NameNormalized = externalId,
            IsOnboarded = onboarded,
            DeploymentMode = AuthEnums.DeploymentMode.Saas,
            BillingTierKey = "free",
        };

    private static async Task<AuthDbContext> TwoChannelsAsync(params object[] extra)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(Channel(ClosedTenant, "closed1", onboarded: false));
        db.Channels.Add(Channel(OpenTenant, "open1", onboarded: true));
        db.AddRange(extra);
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<(ActingProbe Acting, StoringProbe Storing)> IngestChatAsync(
        Guid tenant,
        params object[] extra
    )
    {
        AuthDbContext db = await TwoChannelsAsync(extra);
        ActingProbe acting = new();
        StoringProbe storing = new();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<EventLogger>();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton<IChannelOnboardingGate, ChannelOnboardingGate>();
        services.AddSingleton<EventBus>();
        services.AddSingleton<IEventHandler<ChatMessageReceivedEvent>>(acting);
        services.AddSingleton<IEventHandler<ChatMessageReceivedEvent>>(storing);
        ServiceProvider provider = services.BuildServiceProvider();

        IBotSelfEchoGuardStub guard = new();
        ChannelChatMessageTranslator translator = new(
            provider.GetRequiredService<EventBus>(),
            Clock,
            Substitute.For<IChannelRegistry>(),
            guard.Create(),
            PassThroughChatColors.Create()
        );

        using JsonDocument doc = JsonDocument.Parse(
            """
            {
                "broadcaster_user_id": "b-1",
                "chatter_user_id": "555",
                "chatter_user_login": "cool_user",
                "chatter_user_name": "Cool_User",
                "message_id": "m-1",
                "color": "#FF0000",
                "message_type": "text",
                "message": { "text": "!hug", "fragments": [ { "type": "text", "text": "!hug" } ] },
                "badges": []
            }
            """
        );
        await translator.TranslateAsync(
            new EventSubNotification
            {
                MessageId = "n-1",
                MessageTimestamp = Clock.GetUtcNow(),
                SubscriptionType = "channel.chat.message",
                SubscriptionVersion = "1",
                BroadcasterId = tenant,
                TwitchBroadcasterUserId = "b-1",
                Event = doc.RootElement.Clone(),
            }
        );
        return (acting, storing);
    }

    private sealed class IBotSelfEchoGuardStub
    {
        public NomNomzBot.Application.Contracts.Chat.IBotSelfEchoGuard Create()
        {
            NomNomzBot.Application.Contracts.Chat.IBotSelfEchoGuard guard =
                Substitute.For<NomNomzBot.Application.Contracts.Chat.IBotSelfEchoGuard>();
            guard
                .ShouldSuppressAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(false);
            return guard;
        }
    }

    [Fact]
    public async Task A_chat_message_in_a_non_onboarded_channel_is_stored_but_no_command_or_automation_runs()
    {
        (ActingProbe acting, StoringProbe storing) = await IngestChatAsync(ClosedTenant);

        storing
            .Stored.Should()
            .BeEquivalentTo(["m-1"], "the line is still stored for the moderators");
        acting.Commands.Should().BeEmpty("the bot never acts in a channel that did not onboard");
    }

    [Fact]
    public async Task A_chat_message_in_an_onboarded_channel_still_reaches_every_handler()
    {
        (ActingProbe acting, StoringProbe storing) = await IngestChatAsync(OpenTenant);

        storing.Stored.Should().Equal("m-1");
        acting.Commands.Should().Equal("!hug");
    }

    [Fact]
    public async Task A_non_onboarded_channel_moderated_by_an_onboarded_user_reaches_every_handler_and_the_bot_sends()
    {
        object[] seed = [OnboardedModeratorHome(), Membership(ClosedTenant, ModUser)];

        (ActingProbe acting, StoringProbe storing) = await IngestChatAsync(ClosedTenant, seed);
        (bool message, bool reply, bool asBroadcaster, IChatPlatform twitch) = await SendAllAsync(
            ClosedTenant,
            seed
        );

        storing.Stored.Should().Equal("m-1");
        acting.Commands.Should().Equal("!hug");
        message.Should().BeTrue();
        reply.Should().BeTrue();
        asBroadcaster.Should().BeTrue();
        await twitch
            .Received(1)
            .SendMessageAsync(ClosedTenant, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_non_onboarded_channel_that_no_onboarded_user_moderates_stays_closed()
    {
        // The onboarded user moderates a different channel, not this one.
        object[] seed = [OnboardedModeratorHome(), Membership(OpenTenant, ModUser)];

        (ActingProbe acting, StoringProbe storing) = await IngestChatAsync(ClosedTenant, seed);
        (bool message, bool reply, bool asBroadcaster, IChatPlatform twitch) = await SendAllAsync(
            ClosedTenant,
            seed
        );

        storing.Stored.Should().Equal("m-1");
        acting.Commands.Should().BeEmpty();
        (message || reply || asBroadcaster).Should().BeFalse();
        await twitch
            .DidNotReceiveWithAnyArgs()
            .SendMessageAsync(default, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_moderator_who_is_not_onboarded_does_not_open_the_channel()
    {
        // The moderator's own channel row exists but is not onboarded.
        Channel home = Channel(ModChannel, "modhome1", onboarded: false, ModUser);
        object[] seed = [home, Membership(ClosedTenant, ModUser)];

        (ActingProbe acting, _) = await IngestChatAsync(ClosedTenant, seed);
        (bool message, _, _, IChatPlatform twitch) = await SendAllAsync(ClosedTenant, seed);

        acting.Commands.Should().BeEmpty();
        message.Should().BeFalse();
        await twitch
            .DidNotReceiveWithAnyArgs()
            .SendMessageAsync(default, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_onboarded_user_with_only_a_viewer_standing_does_not_open_the_channel()
    {
        ChannelCommunityStanding standing = new()
        {
            BroadcasterId = ClosedTenant,
            UserId = ModUser,
            Standing = CommunityStanding.Vip,
            LevelValue = PermissionLevel.Vip.ToLevelValue(),
            Source = StandingSource.ChatTags,
        };
        object[] seed = [OnboardedModeratorHome(), standing];

        (ActingProbe acting, _) = await IngestChatAsync(ClosedTenant, seed);
        (bool message, _, _, _) = await SendAllAsync(ClosedTenant, seed);

        acting.Commands.Should().BeEmpty();
        message.Should().BeFalse();
    }

    private static async Task<(
        bool Message,
        bool Reply,
        bool AsBroadcaster,
        IChatPlatform Twitch
    )> SendAllAsync(Guid tenant, params object[] extra)
    {
        AuthDbContext db = await TwoChannelsAsync(extra);
        IChatPlatform twitch = Substitute.For<IChatPlatform>();
        twitch.Provider.Returns(AuthEnums.Platform.Twitch);
        twitch
            .SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        twitch
            .SendReplyAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);
        twitch
            .SendMessageAsBroadcasterAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);
        ChatPlatformRouter router = new(
            [twitch],
            db,
            new OutboundChatShaper(),
            new TokenBucketChatSendQueue(),
            NullLogger<ChatPlatformRouter>.Instance
        );

        return (
            await router.SendMessageAsync(tenant, "hello"),
            await router.SendReplyAsync(tenant, "m-1", "hi"),
            await router.SendMessageAsBroadcasterAsync(tenant, "yo"),
            twitch
        );
    }

    [Fact]
    public async Task The_bot_refuses_every_send_to_a_non_onboarded_channel()
    {
        AuthDbContext db = await TwoChannelsAsync();
        IChatPlatform twitch = Substitute.For<IChatPlatform>();
        twitch.Provider.Returns(AuthEnums.Platform.Twitch);
        twitch
            .SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        twitch
            .SendReplyAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);
        ChatPlatformRouter router = new(
            [twitch],
            db,
            new OutboundChatShaper(),
            new TokenBucketChatSendQueue(),
            NullLogger<ChatPlatformRouter>.Instance
        );

        bool message = await router.SendMessageAsync(ClosedTenant, "hello");
        bool reply = await router.SendReplyAsync(ClosedTenant, "m-1", "hi");
        bool asBroadcaster = await router.SendMessageAsBroadcasterAsync(ClosedTenant, "yo");

        message.Should().BeFalse();
        reply.Should().BeFalse();
        asBroadcaster.Should().BeFalse();
        await twitch
            .DidNotReceiveWithAnyArgs()
            .SendMessageAsync(default, default!, Arg.Any<CancellationToken>());
        await twitch
            .DidNotReceiveWithAnyArgs()
            .SendReplyAsync(default, default!, default!, Arg.Any<CancellationToken>());

        (await router.SendMessageAsync(OpenTenant, "hello"))
            .Should()
            .BeTrue("an onboarded channel is unaffected");
    }
}
