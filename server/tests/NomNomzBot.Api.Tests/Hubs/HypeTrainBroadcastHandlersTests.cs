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
using NomNomzBot.Domain.Community.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves the hype train broadcasters forward begin/progress/end to DASHBOARD clients over the generic
/// <c>ChannelEvent</c> taxonomy, with contributions mapped onto <see cref="HypeTrainContributionDto"/>. The
/// overlay-facing counterpart is <c>WidgetHypeTrainBeganAlertHandler</c> et al. in <c>WidgetAlertHandlers.cs</c>.
/// </summary>
public sealed class HypeTrainBroadcastHandlersTests
{
    private static readonly HypeTrainContribution[] Contributions =
    [
        new("u1", "user1", "User1", "bits", 500),
        new("u2", "user2", "User2", "subscription", 1),
    ];

    [Fact]
    public async Task HypeTrainBegan_MapsLevelProgressAndContributions_AsHypeTrainBeginChannelEvent()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        HypeTrainBeganBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();
        DateTimeOffset expiresAt = new(2026, 7, 1, 12, 5, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new HypeTrainBeganEvent
            {
                BroadcasterId = channel,
                HypeTrainId = "ht-1",
                Level = 2,
                Total = 3000,
                Progress = 1200,
                Goal = 5000,
                TopContributions = Contributions,
                ExpiresAt = expiresAt,
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "hype_train_begin",
                Arg.Is<object>(data =>
                    data is HypeTrainBeganAlertDto
                    && ((HypeTrainBeganAlertDto)data).HypeTrainId == "ht-1"
                    && ((HypeTrainBeganAlertDto)data).Level == 2
                    && ((HypeTrainBeganAlertDto)data).Total == 3000
                    && ((HypeTrainBeganAlertDto)data).Progress == 1200
                    && ((HypeTrainBeganAlertDto)data).Goal == 5000
                    && ((HypeTrainBeganAlertDto)data).ExpiresAt == expiresAt
                    && ((HypeTrainBeganAlertDto)data).TopContributions.Count == 2
                    && ((HypeTrainBeganAlertDto)data).TopContributions[0]
                        == new HypeTrainContributionDto("u1", "user1", "User1", "bits", 500)
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HypeTrainProgress_MapsLevelAndProgress_AsHypeTrainProgressChannelEvent()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        HypeTrainProgressBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();
        DateTimeOffset expiresAt = new(2026, 7, 1, 12, 5, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new HypeTrainProgressEvent
            {
                BroadcasterId = channel,
                HypeTrainId = "ht-1",
                Level = 3,
                Total = 4500,
                Progress = 2200,
                Goal = 6000,
                TopContributions = Contributions,
                ExpiresAt = expiresAt,
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "hype_train_progress",
                Arg.Is<object>(data =>
                    data is HypeTrainProgressAlertDto
                    && ((HypeTrainProgressAlertDto)data).Level == 3
                    && ((HypeTrainProgressAlertDto)data).Progress == 2200
                    && ((HypeTrainProgressAlertDto)data).Goal == 6000
                    && ((HypeTrainProgressAlertDto)data).TopContributions.Count == 2
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HypeTrainEnded_MapsFinalLevel_AsHypeTrainEndChannelEvent()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        HypeTrainEndedBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();
        DateTimeOffset endedAt = new(2026, 7, 1, 12, 10, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new HypeTrainEndedEvent
            {
                BroadcasterId = channel,
                HypeTrainId = "ht-1",
                Level = 4,
                Total = 8000,
                TopContributions = Contributions,
                EndedAt = endedAt,
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "hype_train_end",
                Arg.Is<object>(data =>
                    data is HypeTrainEndedAlertDto
                    && ((HypeTrainEndedAlertDto)data).Level == 4
                    && ((HypeTrainEndedAlertDto)data).Total == 8000
                    && ((HypeTrainEndedAlertDto)data).EndedAt == endedAt
                    && ((HypeTrainEndedAlertDto)data).TopContributions.Count == 2
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HypeTrainBegan_PlatformSentinelChannel_DoesNotNotify()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        HypeTrainBeganBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new HypeTrainBeganEvent
            {
                BroadcasterId = Guid.Empty,
                HypeTrainId = "ht-1",
                Level = 1,
                Total = 1,
                Progress = 1,
                Goal = 1,
                TopContributions = Contributions,
                ExpiresAt = DateTimeOffset.UtcNow,
            }
        );

        await notifier
            .DidNotReceive()
            .NotifyChannelAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>()
            );
    }
}
