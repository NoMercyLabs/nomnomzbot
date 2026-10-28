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

using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves AutoMod queue changes reach dashboard clients live (S-OWN22): a hold and a resolution each push
/// <c>automod_queue_changed</c>, so the Home attention inbox and the Moderation queue panel can re-fetch
/// without polling. Before this slice neither event had a hub consumer — the inbox only changed on reload.
/// </summary>
public sealed class AutoModQueueBroadcastHandlerTests
{
    [Fact]
    public async Task HandleAsync_AHeldMessage_NotifiesAutoModQueueChanged()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        AutoModMessageHeldBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                MessageId = "amsg-1",
                UserId = "9001",
                UserDisplayName = "Chatter",
                UserLogin = "chatter",
                Text = "buy viewers",
                Category = "spam",
                Level = 4,
                HeldAt = new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero),
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "automod_queue_changed",
                Arg.Is<AutoModQueueChangedAlertDto>(d =>
                    d.MessageId == "amsg-1" && d.UserDisplayName == "Chatter" && d.Change == "held"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HandleAsync_AResolution_NotifiesWithTheTwitchVerdict()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        AutoModMessageUpdatedBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                MessageId = "amsg-2",
                UserId = "9001",
                UserDisplayName = "Chatter",
                UserLogin = "chatter",
                ModeratorId = "5005",
                ModeratorDisplayName = "ModHere",
                Status = "denied",
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "automod_queue_changed",
                Arg.Is<AutoModQueueChangedAlertDto>(d =>
                    d.MessageId == "amsg-2" && d.Change == "denied"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HandleAsync_WithoutABroadcaster_StaysSilent()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        AutoModMessageHeldBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                MessageId = "amsg-3",
                UserId = "9001",
                UserDisplayName = "Chatter",
                UserLogin = "chatter",
                Text = "text",
                Category = "spam",
                Level = 1,
                HeldAt = new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero),
            }
        );

        await notifier
            .DidNotReceiveWithAnyArgs()
            .NotifyChannelAsync(default!, default!, default(AutoModQueueChangedAlertDto)!, default);
    }
}
