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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Moderation.EventHandlers;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// The links rule must catch a bare domain (<c>example.com/x</c>), not only <c>http(s)://</c> URLs, and a
/// deletion Twitch refuses must reach the streamer's attention inbox instead of vanishing into a log line.
/// </summary>
public sealed class AutoModerationBareLinkTests
{
    private static readonly Guid Channel = Guid.Parse("0192d000-0000-7000-8000-0000000000b1");
    private static readonly Guid OwnerUserId = Guid.Parse("0192d000-0000-7000-8000-0000000000b2");

    private sealed record Harness(
        AutoModerationHandler Handler,
        IModerationService Moderation,
        ITwitchModerationApi Twitch,
        IEventBus Bus
    );

    private static async Task<Harness> BuildAsync(
        string action,
        string settingsJson = "{}",
        Result? deleteResult = null
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
                  "IsEnabled": true,
                  "Settings": {{settingsJson}}
                }
                """,
            }
        );
        await db.SaveChangesAsync();

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

        ITwitchModerationApi twitch = Substitute.For<ITwitchModerationApi>();
        twitch
            .DeleteChatMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(deleteResult ?? Result.Success());

        IEventBus bus = Substitute.For<IEventBus>();

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton(moderation);
        services.AddSingleton(twitch);
        services.AddSingleton(ViolationEscalationDoubles.NotHandled());
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
            NomNomzBot.Infrastructure.Tests.Platform.Security.TestSanction.Held(),
            NullLogger<AutoModerationHandler>.Instance
        );

        return new Harness(handler, moderation, twitch, bus);
    }

    private static ChatMessageReceivedEvent Message(string text) =>
        new()
        {
            MessageId = "m-1",
            BroadcasterId = Channel,
            TwitchBroadcasterId = "700888",
            UserId = "900888",
            UserDisplayName = "Offender",
            UserLogin = "offender",
            Message = text,
            Fragments = [],
            IsBroadcaster = false,
            IsModerator = false,
            IsVip = false,
            IsSubscriber = false,
            Badges = [],
        };

    [Theory]
    [InlineData("come to example.com")]
    [InlineData("example.com/x")]
    [InlineData("visit sub.example.co.uk")]
    [InlineData("www.example.com")]
    [InlineData("ｅｘａｍｐｌｅ．ｃｏｍ")]
    [InlineData("https://example.com/x")]
    public async Task ALinkRuleDeletesTheMessageForEveryLinkShape(string text)
    {
        Harness h = await BuildAsync("delete");

        await h.Handler.HandleAsync(Message(text), CancellationToken.None);

        await h
            .Twitch.Received(1)
            .DeleteChatMessageAsync(Channel, "m-1", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("end.of sentence")]
    [InlineData("v1.2")]
    [InlineData("it costs 3.5")]
    [InlineData("just a normal message")]
    public async Task ALinkRuleLeavesOrdinaryWordsWithADotAlone(string text)
    {
        Harness h = await BuildAsync("delete");

        await h.Handler.HandleAsync(Message(text), CancellationToken.None);

        await h
            .Twitch.DidNotReceive()
            .DeleteChatMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ABareAllowedDomainPassesButAnotherOneInTheSameMessageStillTrips()
    {
        Harness h = await BuildAsync("delete", "{ \"allowed_domains\": [\"example.com\"] }");

        await h.Handler.HandleAsync(Message("see www.example.com/a"), CancellationToken.None);
        await h
            .Twitch.DidNotReceive()
            .DeleteChatMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );

        await h.Handler.HandleAsync(
            Message("example.com and evil-example.net"),
            CancellationToken.None
        );
        await h
            .Twitch.Received(1)
            .DeleteChatMessageAsync(Channel, "m-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ALookalikeDomainThatMerelyEndsWithAnAllowedNameStillTrips()
    {
        Harness h = await BuildAsync("delete", "{ \"allowed_domains\": [\"example.com\"] }");

        await h.Handler.HandleAsync(Message("notexample.com"), CancellationToken.None);

        await h
            .Twitch.Received(1)
            .DeleteChatMessageAsync(Channel, "m-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailedDeleteIsPublishedSoTheStreamerSeesItInTheInbox()
    {
        Harness h = await BuildAsync(
            "delete",
            deleteResult: Result.Failure("forbidden", "TWITCH_FORBIDDEN")
        );

        await h.Handler.HandleAsync(Message("buy at example.com"), CancellationToken.None);

        await h
            .Bus.Received(1)
            .PublishAsync(
                Arg.Is<AutoModDeleteFailedEvent>(e =>
                    e.BroadcasterId == Channel
                    && e.MessageId == "m-1"
                    && e.UserId == "900888"
                    && e.UserLogin == "offender"
                    && e.RuleName == "no links"
                    && e.Error == "forbidden"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ASuccessfulDeletePublishesNothing()
    {
        Harness h = await BuildAsync("delete");

        await h.Handler.HandleAsync(Message("buy at example.com"), CancellationToken.None);

        await h
            .Bus.DidNotReceive()
            .PublishAsync(Arg.Any<AutoModDeleteFailedEvent>(), Arg.Any<CancellationToken>());
    }
}
