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
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NSubstitute;
using NSubstitute.Core;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves the lockdown events reach the channel's dashboard under the names the app listens for, carrying the
/// window id, platform and control lists (controls only, never people).
/// </summary>
public sealed class LockdownBroadcastHandlersTests
{
    private static readonly Guid WindowId = Guid.Parse("0199c000-0000-7000-8000-0000000000c1");
    private static readonly DateTimeOffset Start = new(2026, 10, 8, 20, 0, 0, TimeSpan.Zero);

    private static object CapturedPayload(IDashboardNotifier notifier, Guid channel, string name)
    {
        List<ICall> calls = notifier
            .ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IDashboardNotifier.NotifyChannelAsync))
            .ToList();
        ICall call = calls.Should().ContainSingle().Subject;
        object?[] args = call.GetArguments();
        args[0].Should().Be(channel.ToString());
        args[1].Should().Be(name);
        return args[2]!;
    }

    [Fact]
    public async Task Engaged_is_pushed_as_lockdown_engaged_with_the_three_control_lists()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid channel = Guid.CreateVersion7();

        await new LockdownEngagedBroadcastHandler(notifier).HandleAsync(
            new()
            {
                BroadcasterId = channel,
                WindowId = WindowId,
                Platform = "twitch",
                Trigger = "hate raid",
                StartedAt = Start,
                ExpiresAt = Start.AddMinutes(15),
                Engaged = [LockdownControl.FollowersOnly, LockdownControl.SlowMode],
                Unavailable = [LockdownControl.BlockedTerms],
                ApplyFailed = [LockdownControl.ShieldMode],
            }
        );

        LockdownEngagedAlertDto dto = CapturedPayload(notifier, channel, "lockdown_engaged")
            .Should()
            .BeOfType<LockdownEngagedAlertDto>()
            .Subject;
        dto.WindowId.Should().Be(WindowId);
        dto.Platform.Should().Be("twitch");
        dto.Trigger.Should().Be("hate raid");
        dto.StartedAt.Should().Be(Start);
        dto.ExpiresAt.Should().Be(Start.AddMinutes(15));
        dto.Engaged.Should().Equal("FollowersOnly", "SlowMode");
        dto.Unavailable.Should().Equal("BlockedTerms");
        dto.ApplyFailed.Should().Equal("ShieldMode");
    }

    [Fact]
    public async Task Extended_is_pushed_as_lockdown_extended_with_the_new_end_time()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid channel = Guid.CreateVersion7();

        await new LockdownExtendedBroadcastHandler(notifier).HandleAsync(
            new()
            {
                BroadcasterId = channel,
                WindowId = WindowId,
                Platform = "twitch",
                ExpiresAt = Start.AddMinutes(25),
            }
        );

        LockdownExtendedAlertDto dto = CapturedPayload(notifier, channel, "lockdown_extended")
            .Should()
            .BeOfType<LockdownExtendedAlertDto>()
            .Subject;
        dto.WindowId.Should().Be(WindowId);
        dto.Platform.Should().Be("twitch");
        dto.ExpiresAt.Should().Be(Start.AddMinutes(25));
    }

    [Fact]
    public async Task Restored_is_pushed_as_lockdown_restored_with_the_controls_put_back()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid channel = Guid.CreateVersion7();

        await new LockdownRestoredBroadcastHandler(notifier).HandleAsync(
            new()
            {
                BroadcasterId = channel,
                WindowId = WindowId,
                Platform = "twitch",
                RestoredAt = Start.AddMinutes(16),
                Restored = [LockdownControl.ShieldMode],
            }
        );

        LockdownRestoredAlertDto dto = CapturedPayload(notifier, channel, "lockdown_restored")
            .Should()
            .BeOfType<LockdownRestoredAlertDto>()
            .Subject;
        dto.WindowId.Should().Be(WindowId);
        dto.RestoredAt.Should().Be(Start.AddMinutes(16));
        dto.Restored.Should().Equal("ShieldMode");
    }

    [Fact]
    public async Task RestoreFailed_is_pushed_as_lockdown_restore_failed_with_the_controls_still_tightened()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid channel = Guid.CreateVersion7();

        await new LockdownRestoreFailedBroadcastHandler(notifier).HandleAsync(
            new()
            {
                BroadcasterId = channel,
                WindowId = WindowId,
                Platform = "twitch",
                Failed = [LockdownControl.ShieldMode, LockdownControl.SlowMode],
            }
        );

        LockdownRestoreFailedAlertDto dto = CapturedPayload(
                notifier,
                channel,
                "lockdown_restore_failed"
            )
            .Should()
            .BeOfType<LockdownRestoreFailedAlertDto>()
            .Subject;
        dto.WindowId.Should().Be(WindowId);
        dto.Failed.Should().Equal("ShieldMode", "SlowMode");
    }

    [Fact]
    public async Task PlatformSentinelChannel_DoesNotNotify()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();

        await new LockdownRestoredBroadcastHandler(notifier).HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                WindowId = WindowId,
                Platform = "twitch",
                RestoredAt = Start,
                Restored = [],
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
