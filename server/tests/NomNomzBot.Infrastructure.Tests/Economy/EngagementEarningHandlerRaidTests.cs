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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Twitch.EventSub;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.Economy;
using NomNomzBot.Infrastructure.Economy.EventHandlers;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Platform.Eventing.Translators;
using NomNomzBot.Infrastructure.Tests.EventStore;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Economy;

/// <summary>
/// A raid arriving on Twitch must pay the raider the Raid earning. The real channel.raid translator
/// publishes <see cref="RaidEvent"/>; the real earning handler must consume that event and the ledger
/// must show the credit (rate x viewer count), once, even when Twitch redelivers the notification.
/// </summary>
public sealed class EngagementEarningHandlerRaidTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000d1");
    private static readonly Guid Raider = Guid.Parse("0192a000-0000-7000-8000-0000000000d2");
    private static readonly FakeTimeProvider Clock = new(new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero));

    private static EventSubNotification IncomingRaid() =>
        new()
        {
            MessageId = "msg-raid-1",
            MessageTimestamp = new(2026, 6, 21, 11, 59, 0, TimeSpan.Zero),
            SubscriptionType = "channel.raid",
            SubscriptionVersion = "1",
            BroadcasterId = Channel,
            TwitchBroadcasterUserId = "broadcaster-99",
            Event = JsonDocument
                .Parse(
                    """
                    {
                        "from_broadcaster_user_id": "5678",
                        "from_broadcaster_user_login": "raiding_streamer",
                        "from_broadcaster_user_name": "Raiding_Streamer",
                        "to_broadcaster_user_id": "broadcaster-99",
                        "to_broadcaster_user_login": "streamer",
                        "to_broadcaster_user_name": "Streamer",
                        "viewers": 250
                    }
                    """
                )
                .RootElement.Clone(),
        };

    private static IUserService RaiderUserService()
    {
        IUserService users = Substitute.For<IUserService>();
        users
            .GetOrCreateAsync(
                "5678",
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new UserDto(
                        Raider.ToString(),
                        "raiding_streamer",
                        "Raiding_Streamer",
                        null,
                        null,
                        DateTime.UnixEpoch,
                        DateTime.UnixEpoch
                    )
                )
            );
        return users;
    }

    [Fact]
    public async Task An_incoming_raid_credits_the_raider_rate_times_viewers_once()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        EventStoreTestDbContext db = database.NewContext();
        EventStoreTestUnitOfWork uow = new(db);
        RecordingEventBus economyBus = new();
        CurrencyAccountService accounts = new(
            db,
            new TenantSequenceAllocator(db),
            uow,
            economyBus,
            Clock
        );
        CurrencyEarningService earning = new(db, accounts, economyBus, Clock);
        db.CurrencyConfigs.Add(
            new()
            {
                BroadcasterId = Channel,
                CurrencyName = "points",
                IsEnabled = true,
                StartingBalance = 0,
            }
        );
        db.EarningRules.Add(
            new()
            {
                BroadcasterId = Channel,
                Source = EarningSource.Raid,
                IsEnabled = true,
                Rate = 2,
            }
        );
        await db.SaveChangesAsync();

        CapturingEventBus translated = new();
        await new ChannelRaidTranslator(translated, Clock).TranslateAsync(IncomingRaid());
        RaidEvent raid = translated.EventsOf<RaidEvent>().Should().ContainSingle().Subject;

        object handler = new EngagementEarningHandler(earning, RaiderUserService());
        IEventHandler<RaidEvent> raidHandler = (IEventHandler<RaidEvent>)handler;
        await raidHandler.HandleAsync(raid);
        await raidHandler.HandleAsync(raid);

        CurrencyLedgerEntry entry = (
            await db
                .CurrencyLedgerEntries.Where(e =>
                    e.ViewerUserId == Raider && e.EventId == raid.EventId
                )
                .ToListAsync()
        )
            .Should()
            .ContainSingle("the same raid delivered twice pays once")
            .Subject;
        entry.Amount.Should().Be(500, "rate 2 x 250 raiding viewers");
        entry.BalanceAfter.Should().Be(500);
        entry.BroadcasterId.Should().Be(Channel);
    }
}
