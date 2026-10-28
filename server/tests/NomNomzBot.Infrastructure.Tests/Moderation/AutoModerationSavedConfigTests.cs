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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Moderation.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Platform.Security;
using NSubstitute;
using ModerationActionResult = NomNomzBot.Application.Moderation.Dtos.ModerationActionResult;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// The AutoMod page saves the four built-in filters through <c>SaveAutomodConfigAsync</c>; the chat handler
/// enforces them. These tests save a config exactly as the page does and then send chat through the handler, so a
/// filter whose stored name or setting key the handler does not know can never again look saved and enforce nothing.
/// </summary>
public sealed class AutoModerationSavedConfigTests
{
    private static readonly Guid Channel = Guid.Parse("0192d000-0000-7000-8000-0000000000c1");
    private static readonly Guid OwnerUserId = Guid.Parse("0192d000-0000-7000-8000-0000000000c2");

    private sealed record Harness(
        AutoModerationHandler Handler,
        ModerationService Service,
        ITwitchModerationApi Twitch
    );

    private static AutomodConfigDto Off() =>
        new(new(false, []), new(false, 70), new(false, []), new(false, 10));

    private static async Task<Harness> BuildAsync()
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                TwitchChannelId = "700999",
                OwnerUserId = OwnerUserId,
                Name = "c",
                NameNormalized = "c",
            }
        );
        await db.SaveChangesAsync();

        ITwitchModerationApi twitch = Substitute.For<ITwitchModerationApi>();
        twitch
            .DeleteChatMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

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

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton(moderation);
        services.AddSingleton(twitch);
        services.AddSingleton(ViolationEscalationDoubles.NotHandled());
        services.AddSingleton(Substitute.For<IEventBus>());
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
            NullLogger<AutoModerationHandler>.Instance
        );

        ModerationService service = new(
            db,
            Substitute.For<ITwitchModerationApi>(),
            Substitute.For<ITwitchModeratorsApi>(),
            Substitute.For<IChannelRegistry>(),
            TimeProvider.System,
            NullLogger<ModerationService>.Instance,
            Substitute.For<IEventBus>()
        );

        return new Harness(handler, service, twitch);
    }

    private static ChatMessageReceivedEvent Message(
        string text,
        IReadOnlyList<ChatMessageFragment>? fragments = null
    ) =>
        new()
        {
            MessageId = "m-1",
            BroadcasterId = Channel,
            TwitchBroadcasterId = "700999",
            UserId = "900999",
            UserDisplayName = "Offender",
            UserLogin = "offender",
            Message = text,
            Fragments = fragments ?? [],
            IsBroadcaster = false,
            IsModerator = false,
            IsVip = false,
            IsSubscriber = false,
            Badges = [],
        };

    private static async Task<int> DeletesAfterAsync(Harness h, ChatMessageReceivedEvent chat)
    {
        h.Twitch.ClearReceivedCalls();
        await h.Handler.HandleAsync(chat, CancellationToken.None);

        return h
            .Twitch.ReceivedCalls()
            .Count(call => call.GetMethodInfo().Name == "DeleteChatMessageAsync");
    }

    [Fact]
    public async Task ASavedLinkFilterDeletesALinkAndLetsAnAllowedDomainThrough()
    {
        Harness h = await BuildAsync();
        await h.Service.SaveAutomodConfigAsync(
            Channel.ToString(),
            Off() with
            {
                LinkFilter = new(true, ["example.com"]),
            }
        );

        Assert.Equal(1, await DeletesAfterAsync(h, Message("buy at evil.net")));
        Assert.Equal(0, await DeletesAfterAsync(h, Message("see www.example.com/a")));
    }

    [Fact]
    public async Task ASavedCapsFilterReadsItsThresholdAsAPercent()
    {
        Harness h = await BuildAsync();
        await h.Service.SaveAutomodConfigAsync(
            Channel.ToString(),
            Off() with
            {
                CapsFilter = new(true, 70),
            }
        );

        Assert.Equal(1, await DeletesAfterAsync(h, Message("STOP SHOUTING IN MY CHAT NOW")));
        Assert.Equal(0, await DeletesAfterAsync(h, Message("Stop shouting in my chat now")));
    }

    [Fact]
    public async Task ASavedEmoteSpamFilterUsesItsSavedLimit()
    {
        Harness h = await BuildAsync();
        await h.Service.SaveAutomodConfigAsync(
            Channel.ToString(),
            Off() with
            {
                EmoteSpam = new(true, 2),
            }
        );

        ChatMessageFragment emote = new() { Type = "emote", Text = "Kappa" };

        Assert.Equal(
            1,
            await DeletesAfterAsync(h, Message("Kappa Kappa Kappa", [emote, emote, emote]))
        );
        Assert.Equal(0, await DeletesAfterAsync(h, Message("Kappa Kappa", [emote, emote])));
    }

    [Fact]
    public async Task ASavedBannedPhrasesFilterStillDeletesTheMessage()
    {
        Harness h = await BuildAsync();
        await h.Service.SaveAutomodConfigAsync(
            Channel.ToString(),
            Off() with
            {
                BannedPhrases = new(true, ["buy followers"]),
            }
        );

        Assert.Equal(1, await DeletesAfterAsync(h, Message("please Buy Followers here")));
        Assert.Equal(0, await DeletesAfterAsync(h, Message("hello chat")));
    }
}
