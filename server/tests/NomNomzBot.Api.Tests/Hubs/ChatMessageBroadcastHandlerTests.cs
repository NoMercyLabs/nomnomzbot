// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Domain.Chat.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves <see cref="ChatMessageBroadcastHandler"/> carries the GAP E3-2 hub-broadcast-layer enrichment
/// additively on <see cref="DashboardChatMessageDto"/> (avatar/pronouns), alongside the badges/roles it already
/// resolves — populated when the enricher has data, and <c>null</c> (never a crash) when it doesn't.
/// </summary>
public sealed class ChatMessageBroadcastHandlerTests
{
    [Fact]
    public async Task Message_from_known_chatter_carries_the_enriched_fields()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IChatMessageDecorator decorator = Substitute.For<IChatMessageDecorator>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        Guid channel = Guid.CreateVersion7();

        decorator
            .DecorateAsync(Arg.Any<ChatMessageReceivedEvent>(), Arg.Any<CancellationToken>())
            .Returns(new DecoratedChatMessage { Fragments = [] });
        enricher
            .EnrichAsync(channel, "u1", Arg.Any<CancellationToken>())
            .Returns(new HubUserEnrichment("Stoney", "https://cdn/avatar.png", "they/them", "Vip"));

        ChatMessageBroadcastHandler handler = new(
            notifier,
            decorator,
            enricher,
            TimeProvider.System
        );

        await handler.HandleAsync(Event(channel));

        await notifier
            .Received(1)
            .SendChatMessageAsync(
                channel.ToString(),
                Arg.Is<DashboardChatMessageDto>(dto =>
                    dto.AvatarUrl == "https://cdn/avatar.png" && dto.Pronouns == "they/them"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Message_from_unknown_chatter_carries_null_enrichment_not_a_crash()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IChatMessageDecorator decorator = Substitute.For<IChatMessageDecorator>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        Guid channel = Guid.CreateVersion7();

        decorator
            .DecorateAsync(Arg.Any<ChatMessageReceivedEvent>(), Arg.Any<CancellationToken>())
            .Returns(new DecoratedChatMessage { Fragments = [] });
        enricher
            .EnrichAsync(channel, "u1", Arg.Any<CancellationToken>())
            .Returns((HubUserEnrichment?)null);

        ChatMessageBroadcastHandler handler = new(
            notifier,
            decorator,
            enricher,
            TimeProvider.System
        );

        await handler.HandleAsync(Event(channel));

        await notifier
            .Received(1)
            .SendChatMessageAsync(
                channel.ToString(),
                Arg.Is<DashboardChatMessageDto>(dto =>
                    dto.AvatarUrl == null && dto.Pronouns == null
                ),
                Arg.Any<CancellationToken>()
            );
    }

    private static ChatMessageReceivedEvent Event(Guid channel) =>
        new()
        {
            BroadcasterId = channel,
            MessageId = "m1",
            TwitchBroadcasterId = "123",
            UserId = "u1",
            UserDisplayName = "Stoney",
            UserLogin = "stoney_eagle",
            Message = "hello",
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };
}
