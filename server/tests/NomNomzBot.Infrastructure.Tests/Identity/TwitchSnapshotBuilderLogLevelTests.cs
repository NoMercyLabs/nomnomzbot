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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Tests.Seeding;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the periodic snapshot builders treat a <c>missing_scope</c> read failure as the known, already-surfaced
/// state it is (Debug), while every other failure code stays a Warning — so an un-granted scope does not write a
/// warning on every pass.
/// </summary>
public sealed class TwitchSnapshotBuilderLogLevelTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-00000000c101");

    [Fact]
    public async Task Management_builder_logs_a_missing_scope_failure_at_debug()
    {
        (TwitchManagementSnapshotBuilder sut, ListLogger<TwitchManagementSnapshotBuilder> log) =
            BuildManagement(TwitchErrorCodes.MissingScope);

        await sut.BuildAsync(Broadcaster);

        log.Entries.Where(e => e.Level == LogLevel.Warning).Should().BeEmpty();
        log.Entries.Count(e => e.Level == LogLevel.Debug).Should().Be(2);
    }

    [Fact]
    public async Task Management_builder_still_logs_any_other_failure_at_warning()
    {
        (TwitchManagementSnapshotBuilder sut, ListLogger<TwitchManagementSnapshotBuilder> log) =
            BuildManagement(TwitchErrorCodes.Unauthorized);

        await sut.BuildAsync(Broadcaster);

        log.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("unauthorized"))
            .Should()
            .Be(2);
    }

    [Fact]
    public async Task Standing_builder_logs_a_missing_scope_failure_at_debug()
    {
        (TwitchStandingSnapshotBuilder sut, ListLogger<TwitchStandingSnapshotBuilder> log) =
            BuildStanding(TwitchErrorCodes.MissingScope);

        await sut.BuildAsync(Broadcaster);

        log.Entries.Where(e => e.Level == LogLevel.Warning).Should().BeEmpty();
        log.Entries.Count(e => e.Level == LogLevel.Debug).Should().Be(2);
    }

    [Fact]
    public async Task Standing_builder_still_logs_any_other_failure_at_warning()
    {
        (TwitchStandingSnapshotBuilder sut, ListLogger<TwitchStandingSnapshotBuilder> log) =
            BuildStanding(TwitchErrorCodes.Unauthorized);

        await sut.BuildAsync(Broadcaster);

        log.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("unauthorized"))
            .Should()
            .Be(2);
    }

    private static (
        TwitchManagementSnapshotBuilder Sut,
        ListLogger<TwitchManagementSnapshotBuilder> Log
    ) BuildManagement(string errorCode)
    {
        ITwitchModeratorsApi mods = Substitute.For<ITwitchModeratorsApi>();
        ITwitchChannelsApi channels = Substitute.For<ITwitchChannelsApi>();
        mods.GetModeratorsAsync(
                Broadcaster,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<TwitchPage<TwitchModerator>>("denied", errorCode));
        channels
            .GetChannelEditorsAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<TwitchChannelEditor>>("denied", errorCode));
        ListLogger<TwitchManagementSnapshotBuilder> log = new();
        return (new(Substitute.For<IUserService>(), mods, channels, log), log);
    }

    private static (
        TwitchStandingSnapshotBuilder Sut,
        ListLogger<TwitchStandingSnapshotBuilder> Log
    ) BuildStanding(string errorCode)
    {
        ITwitchSubscriptionsApi subs = Substitute.For<ITwitchSubscriptionsApi>();
        ITwitchModeratorsApi mods = Substitute.For<ITwitchModeratorsApi>();
        subs.GetBroadcasterSubscriptionsAsync(
                Broadcaster,
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<TwitchPage<TwitchBroadcasterSubscription>>("denied", errorCode)
            );
        mods.GetVipsAsync(Broadcaster, Arg.Any<TwitchPageRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<TwitchPage<TwitchVip>>("denied", errorCode));
        ListLogger<TwitchStandingSnapshotBuilder> log = new();
        return (new(Substitute.For<IUserService>(), subs, mods, log), log);
    }
}
