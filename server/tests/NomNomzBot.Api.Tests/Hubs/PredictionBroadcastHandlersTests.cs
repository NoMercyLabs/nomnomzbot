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
/// Proves the prediction broadcasters forward begin/progress/lock/end to dashboard clients over the generic
/// <c>ChannelEvent</c> taxonomy, with outcomes mapped onto <see cref="PredictionOutcomeDto"/> — the gap this
/// closes: the read-model handlers never reached a hub client.
/// </summary>
public sealed class PredictionBroadcastHandlersTests
{
    private static readonly PredictionOutcome[] Outcomes =
    [
        new("o1", "Yes", 1000, 12, "blue"),
        new("o2", "No", 500, 8, "pink"),
    ];

    [Fact]
    public async Task PredictionBegan_MapsOutcomesAndWindow_AsPredictionBeginChannelEvent()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        PredictionBeganBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();
        DateTimeOffset locksAt = new(2026, 7, 1, 12, 5, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new PredictionBeganEvent
            {
                BroadcasterId = channel,
                PredictionId = "pred-1",
                Title = "Will it rain?",
                Outcomes = Outcomes,
                WindowSeconds = 300,
                LocksAt = locksAt,
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "prediction_begin",
                Arg.Is<object>(data =>
                    data is PredictionBeganAlertDto
                    && ((PredictionBeganAlertDto)data).PredictionId == "pred-1"
                    && ((PredictionBeganAlertDto)data).WindowSeconds == 300
                    && ((PredictionBeganAlertDto)data).LocksAt == locksAt
                    && ((PredictionBeganAlertDto)data).Outcomes.Count == 2
                    && ((PredictionBeganAlertDto)data).Outcomes[0]
                        == new PredictionOutcomeDto("o1", "Yes", 1000, 12, "blue")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PredictionProgress_MapsRunningPools_AsPredictionProgressChannelEvent()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        PredictionProgressBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();
        DateTimeOffset locksAt = new(2026, 7, 1, 12, 5, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new PredictionProgressEvent
            {
                BroadcasterId = channel,
                PredictionId = "pred-1",
                Title = "Will it rain?",
                Outcomes = Outcomes,
                LocksAt = locksAt,
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "prediction_progress",
                Arg.Is<object>(data =>
                    data is PredictionProgressAlertDto
                    && ((PredictionProgressAlertDto)data).PredictionId == "pred-1"
                    && ((PredictionProgressAlertDto)data).LocksAt == locksAt
                    && ((PredictionProgressAlertDto)data).Outcomes.Count == 2
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PredictionLocked_MapsOutcomes_AsPredictionLockChannelEvent()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        PredictionLockedBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();

        await handler.HandleAsync(
            new PredictionLockedEvent
            {
                BroadcasterId = channel,
                PredictionId = "pred-1",
                Title = "Will it rain?",
                Outcomes = Outcomes,
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "prediction_lock",
                Arg.Is<object>(data =>
                    data is PredictionLockedAlertDto
                    && ((PredictionLockedAlertDto)data).PredictionId == "pred-1"
                    && ((PredictionLockedAlertDto)data).Outcomes.Count == 2
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PredictionEnded_MapsStatusAndWinner_AsPredictionEndChannelEvent()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        PredictionEndedBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();

        await handler.HandleAsync(
            new PredictionEndedEvent
            {
                BroadcasterId = channel,
                PredictionId = "pred-1",
                Title = "Will it rain?",
                Status = "resolved",
                Outcomes = Outcomes,
                WinningOutcomeId = "o1",
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "prediction_end",
                Arg.Is<object>(data =>
                    data is PredictionEndedAlertDto
                    && ((PredictionEndedAlertDto)data).Status == "resolved"
                    && ((PredictionEndedAlertDto)data).WinningOutcomeId == "o1"
                    && ((PredictionEndedAlertDto)data).Outcomes.Count == 2
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PredictionBegan_PlatformSentinelChannel_DoesNotNotify()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        PredictionBeganBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new PredictionBeganEvent
            {
                BroadcasterId = Guid.Empty,
                PredictionId = "pred-1",
                Title = "t",
                Outcomes = Outcomes,
                WindowSeconds = 60,
                LocksAt = DateTimeOffset.UtcNow,
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
