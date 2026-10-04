// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.Platform.Eventing;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing;

/// <summary>
/// The inbox is a first-in first-out queue: <see cref="DatabaseEventSubInbox.PeekAsync"/> orders by ReceivedAt,
/// then by Id. Notifications received in one clock tick share a ReceivedAt, so the order rests on the Id, which
/// Guid.CreateVersion7 leaves random inside one millisecond.
/// </summary>
public sealed class DatabaseEventSubInboxOrderTests
{
    [Fact]
    public async Task Notifications_received_in_the_same_tick_are_peeked_in_the_order_they_arrived()
    {
        string dbName = $"inbox-order-{Guid.NewGuid():N}";
        using EventSubTestDbContext keepAlive = EventSubTestDbContext.Shared(dbName);
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IApplicationDbContext>(_ => EventSubTestDbContext.Shared(dbName))
            .BuildServiceProvider();
        DatabaseEventSubInbox inbox = new(provider.GetRequiredService<IServiceScopeFactory>());
        DateTime tick = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

        List<string> arrived = [];
        for (int i = 0; i < 40; i++)
        {
            string messageId = $"msg-{i:D3}";
            arrived.Add(messageId);
            bool stored = await inbox.EnqueueAsync(
                new EventSubInboxMessage
                {
                    MessageId = messageId,
                    MessageTimestamp = tick,
                    SubscriptionType = "channel.chat.message",
                    SubscriptionVersion = "1",
                    TwitchBroadcasterUserId = "1234",
                    EventJson = "{}",
                    ReceivedAt = tick,
                }
            );
            stored.Should().BeTrue();
        }

        IReadOnlyList<EventSubInboxMessage> peeked = await inbox.PeekAsync(100);

        peeked.Select(m => m.MessageId).Should().Equal(arrived);
    }
}
