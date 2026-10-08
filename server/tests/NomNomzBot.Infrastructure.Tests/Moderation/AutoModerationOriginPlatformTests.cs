// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Moderation.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Platform.Security;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// The channel's own auto-moderation rules act on the platform the message arrived on. A Kick or YouTube
/// message is moderated through <see cref="IInboundOriginModerator"/> with the event's provider; Helix and
/// the Twitch-keyed <see cref="IModerationService"/> never see it. Twitch keeps its existing path.
/// </summary>
public sealed class AutoModerationOriginPlatformTests
{
    private static readonly Guid Channel = Guid.Parse("0192d300-0000-7000-8000-0000000000d1");
    private static readonly Guid OwnerUserId = Guid.Parse("0192d300-0000-7000-8000-0000000000d2");
    private const string OffenderId = "kick-user-42";

    private sealed record Harness(
        AutoModerationHandler Handler,
        IInboundOriginModerator Origin,
        IModerationService Moderation,
        ITwitchModerationApi Twitch,
        IViolationEscalationService Escalation,
        IEventBus Bus,
        ILogger<AutoModerationHandler> Logger
    );

    private static async Task<Harness> BuildAsync(
        string action,
        InboundModerationOutcome originOutcome
    )
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                TwitchChannelId = "700888",
                OwnerUserId = OwnerUserId,
                Name = "c",
                NameNormalized = "c",
            }
        );
        db.Records.Add(
            new()
            {
                BroadcasterId = Channel,
                UserId = OwnerUserId.ToString(),
                RecordType = "moderation_rule",
                Data = $$"""
                {
                  "Name": "no links",
                  "Type": "links",
                  "Action": "{{action}}",
                  "Reason": "links are not allowed",
                  "DurationSeconds": 120,
                  "IsEnabled": true,
                  "Settings": {}
                }
                """,
            }
        );
        await db.SaveChangesAsync();

        IInboundOriginModerator origin = Substitute.For<IInboundOriginModerator>();
        origin
            .DeleteMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(originOutcome);
        origin
            .TimeoutUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(originOutcome);
        origin
            .BanUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(originOutcome);

        IModerationService moderation = Substitute.For<IModerationService>();
        moderation
            .TimeoutAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new ModerationActionResult(true, null)));
        moderation
            .BanAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new ModerationActionResult(true, null)));
        ITwitchModerationApi twitch = Substitute.For<ITwitchModerationApi>();
        twitch
            .DeleteChatMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        IViolationEscalationService escalation = ViolationEscalationDoubles.NotHandled();
        IEventBus bus = Substitute.For<IEventBus>();
        ILogger<AutoModerationHandler> logger = Substitute.For<ILogger<AutoModerationHandler>>();

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton(origin);
        services.AddSingleton(moderation);
        services.AddSingleton(twitch);
        services.AddSingleton(escalation);
        services.AddSingleton(bus);
        ServiceProvider provider = services.BuildServiceProvider();

        IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        AutoModerationHandler handler = new(
            scopeFactory,
            new AutoModRuleCache(
                scopeFactory,
                TimeProvider.System,
                NullLogger<AutoModRuleCache>.Instance
            ),
            TestSanction.Held(),
            logger
        );

        return new Harness(handler, origin, moderation, twitch, escalation, bus, logger);
    }

    private static ChatMessageReceivedEvent LinkMessage(string provider) =>
        new()
        {
            MessageId = "m-1",
            Provider = provider,
            BroadcasterId = Channel,
            TwitchBroadcasterId = "700888",
            UserId = OffenderId,
            UserDisplayName = "Offender",
            UserLogin = "offender",
            Message = "buy at example.com",
            Fragments = [],
            IsBroadcaster = false,
            IsModerator = false,
            IsVip = false,
            IsSubscriber = false,
            Badges = [],
        };

    private static async Task AssertNothingTwitchKeyedAsync(Harness h)
    {
        await h
            .Twitch.DidNotReceive()
            .DeleteChatMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        Assert.Empty(h.Moderation.ReceivedCalls());
        Assert.Empty(h.Escalation.ReceivedCalls());
    }

    private static void AssertWarningLogged(Harness h) =>
        h
            .Logger.Received()
            .Log(
                LogLevel.Warning,
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>()
            );

    [Theory]
    [InlineData(AuthEnums.Platform.Kick)]
    [InlineData(AuthEnums.Platform.YouTube)]
    public async Task ANonTwitchDeleteRuleDeletesThroughTheOriginSeamAndNeverTouchesHelix(
        string provider
    )
    {
        Harness h = await BuildAsync("delete", InboundModerationOutcome.Done());

        await h.Handler.HandleAsync(LinkMessage(provider), CancellationToken.None);

        await h
            .Origin.Received(1)
            .DeleteMessageAsync(Channel, provider, "m-1", Arg.Any<CancellationToken>());
        Assert.Single(h.Origin.ReceivedCalls());
        await AssertNothingTwitchKeyedAsync(h);
        await h
            .Bus.DidNotReceive()
            .PublishAsync(Arg.Any<AutoModDeleteFailedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ANotSupportedDeleteIsReportedExactlyLikeAFailedTwitchDelete()
    {
        Harness h = await BuildAsync(
            "delete",
            InboundModerationOutcome.NotSupported("No chat platform is registered for 'kick'.")
        );

        await h.Handler.HandleAsync(LinkMessage(AuthEnums.Platform.Kick), CancellationToken.None);

        await h
            .Bus.Received(1)
            .PublishAsync(
                Arg.Is<AutoModDeleteFailedEvent>(e =>
                    e.BroadcasterId == Channel
                    && e.MessageId == "m-1"
                    && e.UserId == OffenderId
                    && e.UserLogin == "offender"
                    && e.RuleName == "no links"
                    && e.Error == "No chat platform is registered for 'kick'."
                ),
                Arg.Any<CancellationToken>()
            );
        AssertWarningLogged(h);
        await AssertNothingTwitchKeyedAsync(h);
    }

    [Fact]
    public async Task AFailedDeleteOnTheOriginPlatformIsReportedWithThePlatformReason()
    {
        Harness h = await BuildAsync(
            "delete",
            InboundModerationOutcome.Failed(
                "The 'youtube' chat platform refused the delete-message."
            )
        );

        await h.Handler.HandleAsync(
            LinkMessage(AuthEnums.Platform.YouTube),
            CancellationToken.None
        );

        await h
            .Bus.Received(1)
            .PublishAsync(
                Arg.Is<AutoModDeleteFailedEvent>(e =>
                    e.MessageId == "m-1"
                    && e.Error == "The 'youtube' chat platform refused the delete-message."
                ),
                Arg.Any<CancellationToken>()
            );
        await AssertNothingTwitchKeyedAsync(h);
    }

    [Fact]
    public async Task ANonTwitchTimeoutRuleTimesOutThroughTheOriginSeamWithTheRulesDurationAndReason()
    {
        Harness h = await BuildAsync("timeout", InboundModerationOutcome.Done());

        await h.Handler.HandleAsync(LinkMessage(AuthEnums.Platform.Kick), CancellationToken.None);

        await h
            .Origin.Received(1)
            .TimeoutUserAsync(
                Channel,
                AuthEnums.Platform.Kick,
                OffenderId,
                120,
                "links are not allowed",
                Arg.Any<CancellationToken>()
            );
        Assert.Single(h.Origin.ReceivedCalls());
        await AssertNothingTwitchKeyedAsync(h);
    }

    [Fact]
    public async Task ANonTwitchBanRuleBansThroughTheOriginSeam()
    {
        Harness h = await BuildAsync("ban", InboundModerationOutcome.Done());

        await h.Handler.HandleAsync(
            LinkMessage(AuthEnums.Platform.YouTube),
            CancellationToken.None
        );

        await h
            .Origin.Received(1)
            .BanUserAsync(
                Channel,
                AuthEnums.Platform.YouTube,
                OffenderId,
                "links are not allowed",
                Arg.Any<CancellationToken>()
            );
        Assert.Single(h.Origin.ReceivedCalls());
        await AssertNothingTwitchKeyedAsync(h);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("ban")]
    public async Task ARefusedAccountActionOnTheOriginPlatformIsLoggedLikeAFailedTwitchOne(
        string action
    )
    {
        Harness h = await BuildAsync(
            action,
            InboundModerationOutcome.NotSupported("No chat platform is registered for 'kick'.")
        );

        await h.Handler.HandleAsync(LinkMessage(AuthEnums.Platform.Kick), CancellationToken.None);

        AssertWarningLogged(h);
        await AssertNothingTwitchKeyedAsync(h);
        await h
            .Bus.DidNotReceive()
            .PublishAsync(Arg.Any<AutoModDeleteFailedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ATwitchMessageStillGoesToHelixOnlyAndNeverTheOriginSeam()
    {
        Harness h = await BuildAsync("delete", InboundModerationOutcome.Done());

        await h.Handler.HandleAsync(LinkMessage(AuthEnums.Platform.Twitch), CancellationToken.None);

        await h
            .Twitch.Received(1)
            .DeleteChatMessageAsync(Channel, "m-1", Arg.Any<CancellationToken>());
        Assert.Empty(h.Origin.ReceivedCalls());
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("ban")]
    public async Task ATwitchAccountActionStillGoesThroughTheModerationServiceNotTheOriginSeam(
        string action
    )
    {
        Harness h = await BuildAsync(action, InboundModerationOutcome.Done());

        await h.Handler.HandleAsync(LinkMessage(AuthEnums.Platform.Twitch), CancellationToken.None);

        Assert.Empty(h.Origin.ReceivedCalls());
        Assert.Single(h.Moderation.ReceivedCalls());
        string expected =
            action == "ban"
                ? nameof(IModerationService.BanAsync)
                : nameof(IModerationService.TimeoutAsync);
        Assert.Equal(expected, h.Moderation.ReceivedCalls().Single().GetMethodInfo().Name);
    }
}
