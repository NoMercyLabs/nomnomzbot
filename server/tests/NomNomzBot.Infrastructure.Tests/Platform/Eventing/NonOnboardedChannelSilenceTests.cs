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

    private static Channel Channel(Guid id, string externalId, bool onboarded) =>
        new()
        {
            Id = id,
            OwnerUserId = Owner,
            Provider = AuthEnums.Platform.Twitch,
            ExternalChannelId = externalId,
            TwitchChannelId = externalId,
            Name = externalId,
            NameNormalized = externalId,
            IsOnboarded = onboarded,
            DeploymentMode = AuthEnums.DeploymentMode.Saas,
            BillingTierKey = "free",
        };

    private static async Task<AuthDbContext> TwoChannelsAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(Channel(ClosedTenant, "closed1", onboarded: false));
        db.Channels.Add(Channel(OpenTenant, "open1", onboarded: true));
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<(ActingProbe Acting, StoringProbe Storing)> IngestChatAsync(
        Guid tenant
    )
    {
        AuthDbContext db = await TwoChannelsAsync();
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
