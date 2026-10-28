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
using NomNomzBot.Domain.Rewards.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves an automatic channel-point reward and a custom Power-up reach the dashboard as one
/// <see cref="RedemptionActivityDto"/> each, so the chat page can show them as lines like Twitch does.
/// </summary>
public sealed class RedemptionActivityBroadcastHandlersTests
{
    [Fact]
    public async Task Automatic_reward_is_forwarded_with_its_kind_cost_in_points_and_message()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        AutomaticRewardBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();
        DateTimeOffset occurredAt = new(2026, 10, 15, 20, 0, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new AutomaticRewardRedeemedEvent
            {
                BroadcasterId = channel,
                OccurredAt = occurredAt,
                RedemptionId = "auto-1",
                UserId = "u1",
                UserLogin = "viewer",
                UserDisplayName = "Viewer",
                RewardType = "send_highlighted_message",
                Cost = 50,
                Message = "hello chat",
            }
        );

        await notifier
            .Received(1)
            .SendRedemptionActivityAsync(
                channel.ToString(),
                Arg.Is<RedemptionActivityDto>(dto =>
                    dto.Kind == "automatic_reward"
                    && dto.RedemptionId == "auto-1"
                    && dto.Title == "send_highlighted_message"
                    && dto.UserId == "u1"
                    && dto.UserLogin == "viewer"
                    && dto.UserDisplayName == "Viewer"
                    && dto.Cost == 50
                    && dto.CostUnit == "channel_points"
                    && dto.UserInput == "hello chat"
                    && dto.Timestamp == occurredAt.ToString("O")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Custom_power_up_is_forwarded_with_its_title_and_cost_in_bits()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        CustomPowerUpBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();

        await handler.HandleAsync(
            new CustomPowerUpRedeemedEvent
            {
                BroadcasterId = channel,
                OccurredAt = new(2026, 10, 15, 20, 0, 0, TimeSpan.Zero),
                RedemptionId = "pu-1",
                UserId = "u2",
                UserLogin = "fan",
                UserDisplayName = "Fan",
                PowerUpId = "p1",
                PowerUpTitle = "Confetti",
                Bits = 100,
                Status = "unfulfilled",
                UserInput = "go go",
            }
        );

        await notifier
            .Received(1)
            .SendRedemptionActivityAsync(
                channel.ToString(),
                Arg.Is<RedemptionActivityDto>(dto =>
                    dto.Kind == "custom_power_up"
                    && dto.Title == "Confetti"
                    && dto.Cost == 100
                    && dto.CostUnit == "bits"
                    && dto.UserInput == "go go"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Celebration_power_up_is_forwarded_as_a_power_up_line_in_bits()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        PowerUpBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();

        await handler.HandleAsync(
            new BitsUsedEvent
            {
                BroadcasterId = channel,
                OccurredAt = new(2026, 10, 15, 20, 0, 0, TimeSpan.Zero),
                UserId = "u3",
                UserLogin = "party",
                UserDisplayName = "Party",
                Bits = 250,
                Type = "power_up",
                PowerUpType = "celebration",
            }
        );

        await notifier
            .Received(1)
            .SendRedemptionActivityAsync(
                channel.ToString(),
                Arg.Is<RedemptionActivityDto>(dto =>
                    dto.Kind == "power_up"
                    && dto.Title == "celebration"
                    && dto.UserLogin == "party"
                    && dto.Cost == 250
                    && dto.CostUnit == "bits"
                    && dto.RedemptionId.Length > 0
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData("cheer", null)]
    [InlineData("power_up", "message_effect")]
    [InlineData("power_up", "gigantify_an_emote")]
    public async Task Cheers_and_power_ups_shown_on_their_own_chat_message_get_no_extra_line(
        string type,
        string? powerUpType
    )
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        PowerUpBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new BitsUsedEvent
            {
                BroadcasterId = Guid.CreateVersion7(),
                UserId = "u3",
                UserLogin = "party",
                UserDisplayName = "Party",
                Bits = 100,
                Type = type,
                PowerUpType = powerUpType,
            }
        );

        await notifier
            .DidNotReceiveWithAnyArgs()
            .SendRedemptionActivityAsync(default!, default!, default);
    }

    [Fact]
    public async Task An_event_with_no_channel_is_dropped()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        CustomPowerUpBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new CustomPowerUpRedeemedEvent
            {
                BroadcasterId = Guid.Empty,
                RedemptionId = "pu-2",
                UserId = "u2",
                UserLogin = "fan",
                UserDisplayName = "Fan",
                PowerUpId = "p1",
                PowerUpTitle = "Confetti",
                Bits = 100,
                Status = "unfulfilled",
            }
        );

        await notifier
            .DidNotReceiveWithAnyArgs()
            .SendRedemptionActivityAsync(default!, default!, default);
    }
}
