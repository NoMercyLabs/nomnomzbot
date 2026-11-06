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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Moderation.EventHandlers;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing;

/// <summary>
/// A moderator who watches a channel that never onboarded still moderates it: the spam-defence stack must see
/// the channel's chat through the real event bus, so the verdict can be carried out as that moderator.
/// </summary>
public sealed class NonOnboardedSpamDefenseTests
{
    private static readonly Guid ClosedTenant = Guid.Parse("0192a000-0000-7000-8000-0000000000e1");

    [Fact]
    public async Task Chat_in_a_non_onboarded_channel_reaches_the_spam_defence_stack()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new Channel
            {
                Id = ClosedTenant,
                OwnerUserId = Guid.Parse("0192a000-0000-7000-8000-0000000000e9"),
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "closed-spam",
                TwitchChannelId = "closed-spam",
                Name = "closed-spam",
                NameNormalized = "closed-spam",
                IsOnboarded = false,
                DeploymentMode = AuthEnums.DeploymentMode.Saas,
                BillingTierKey = "free",
            }
        );
        await db.SaveChangesAsync();

        ISpamDefenseService spamDefense = Substitute.For<ISpamDefenseService>();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<EventLogger>();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton(spamDefense);
        services.AddSingleton<IChannelOnboardingGate, ChannelOnboardingGate>();
        services.AddSingleton<EventBus>();
        services.AddSingleton<IEventHandler<ChatMessageReceivedEvent>, SpamDefenseHandler>();
        ServiceProvider provider = services.BuildServiceProvider();

        await provider
            .GetRequiredService<EventBus>()
            .PublishAsync(
                new ChatMessageReceivedEvent
                {
                    BroadcasterId = ClosedTenant,
                    IsChannelIngress = true,
                    MessageId = "m-spam-1",
                    TwitchBroadcasterId = "closed-spam",
                    UserId = "1596785648",
                    UserDisplayName = "spammer",
                    UserLogin = "spammer",
                    Message = "Best viewers with streamboo .com",
                    Fragments = [],
                    Badges = [],
                    IsBroadcaster = false,
                    IsModerator = false,
                    IsVip = false,
                    IsSubscriber = false,
                }
            );

        await spamDefense
            .Received(1)
            .EvaluateAsync(
                Arg.Is<SpamEvaluationRequest>(r =>
                    r.BroadcasterId == ClosedTenant && r.MessageId == "m-spam-1"
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
