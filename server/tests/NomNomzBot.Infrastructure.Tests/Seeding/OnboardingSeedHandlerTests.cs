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
using NomNomzBot.Application.Community.Services;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Infrastructure.Community.EventHandlers;
using NomNomzBot.Infrastructure.Identity.EventHandlers;
using NomNomzBot.Infrastructure.Rewards.EventHandlers;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace NomNomzBot.Infrastructure.Tests.Seeding;

/// <summary>
/// Proves the onboarding seed-job handlers (the three <c>IEventHandler&lt;ChannelOnboardedEvent&gt;</c>): each
/// calls its own domain's Twitch sync for the event's broadcaster, and each isolates a sync failure — it is
/// caught and logged, the handler never throws, so one failing seed job cannot abort the others.
/// </summary>
public sealed class OnboardingSeedHandlerTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-00000000a001");

    private static ChannelOnboardedEvent Event() =>
        new()
        {
            BroadcasterId = Broadcaster,
            OwnerUserId = Guid.Parse("0192a000-0000-7000-8000-00000000a002"),
            TwitchChannelId = "tw-123",
            Name = "stoney_eagle",
        };

    // ── Rewards seed handler ────────────────────────────────────────────────

    [Fact]
    public async Task Reward_handler_syncs_rewards_for_the_event_broadcaster()
    {
        IRewardService rewards = Substitute.For<IRewardService>();
        rewards
            .SyncWithTwitchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        ListLogger<RewardSeedOnOnboardingHandler> log = new();
        RewardSeedOnOnboardingHandler sut = new(rewards, log);

        await sut.HandleAsync(Event());

        await rewards
            .Received(1)
            .SyncWithTwitchAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reward_handler_catches_and_logs_a_sync_failure_without_throwing()
    {
        IRewardService rewards = Substitute.For<IRewardService>();
        rewards
            .SyncWithTwitchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("twitch is down"));
        ListLogger<RewardSeedOnOnboardingHandler> log = new();
        RewardSeedOnOnboardingHandler sut = new(rewards, log);

        Func<Task> act = () => sut.HandleAsync(Event());

        await act.Should().NotThrowAsync();
        log.Entries.Should().Contain(e => e.Level == LogLevel.Error);
    }

    // ── Community moderator-roster seed handler ──────────────────────────────

    [Fact]
    public async Task Community_handler_syncs_moderator_roster_for_the_event_broadcaster()
    {
        ICommunityRosterService roster = Substitute.For<ICommunityRosterService>();
        roster
            .SyncModeratorsFromTwitchAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(3));
        ListLogger<ModeratorRosterSeedOnOnboardingHandler> log = new();
        ModeratorRosterSeedOnOnboardingHandler sut = new(roster, log);

        await sut.HandleAsync(Event());

        await roster
            .Received(1)
            .SyncModeratorsFromTwitchAsync(Broadcaster, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Community_handler_catches_and_logs_a_sync_failure_without_throwing()
    {
        ICommunityRosterService roster = Substitute.For<ICommunityRosterService>();
        roster
            .SyncModeratorsFromTwitchAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("twitch is down"));
        ListLogger<ModeratorRosterSeedOnOnboardingHandler> log = new();
        ModeratorRosterSeedOnOnboardingHandler sut = new(roster, log);

        Func<Task> act = () => sut.HandleAsync(Event());

        await act.Should().NotThrowAsync();
        log.Entries.Should().Contain(e => e.Level == LogLevel.Error);
    }

    // ── Membership (Plane-B roles) seed handler ──────────────────────────────

    [Fact]
    public async Task Membership_handler_builds_a_snapshot_and_syncs_for_the_event_broadcaster()
    {
        IMembershipService membership = Substitute.For<IMembershipService>();
        IUserService users = Substitute.For<IUserService>();
        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        ITwitchChannelsApi channels = Substitute.For<ITwitchChannelsApi>();

        Guid modUserGuid = Guid.Parse("0192a000-0000-7000-8000-00000000b001");
        Guid editorUserGuid = Guid.Parse("0192a000-0000-7000-8000-00000000b002");

        moderators
            .GetModeratorsAsync(
                Broadcaster,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchModerator>(
                        [new TwitchModerator("tw-mod", "modlogin", "ModName")],
                        null,
                        1
                    )
                )
            );
        channels
            .GetChannelEditorsAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<TwitchChannelEditor>>([
                    new TwitchChannelEditor("tw-editor", "EditorName", DateTimeOffset.UtcNow),
                ])
            );

        users
            .GetOrCreateAsync("tw-mod", "modlogin", "ModName", Arg.Any<CancellationToken>())
            .Returns(UserResult(modUserGuid, "modlogin", "ModName"));
        users
            .GetOrCreateAsync("tw-editor", "EditorName", "EditorName", Arg.Any<CancellationToken>())
            .Returns(UserResult(editorUserGuid, "EditorName", "EditorName"));

        membership
            .SyncManagementFromTwitchAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyList<TwitchManagementMember>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        ListLogger<MembershipSeedOnOnboardingHandler> log = new();
        MembershipSeedOnOnboardingHandler sut = new(membership, users, moderators, channels, log);

        await sut.HandleAsync(Event());

        // The sync ran for the event's broadcaster with both the mod (badge) and the editor (Helix editors),
        // each mapped to its resolved User Guid and the correct role/source.
        await membership
            .Received(1)
            .SyncManagementFromTwitchAsync(
                Broadcaster,
                Arg.Is<IReadOnlyList<TwitchManagementMember>>(snapshot =>
                    snapshot.Count == 2
                    && snapshot.Any(m =>
                        m.UserId == modUserGuid
                        && m.TwitchUserId == "tw-mod"
                        && m.Role == ManagementRole.Moderator
                        && m.Source == MembershipSource.TwitchBadge
                    )
                    && snapshot.Any(m =>
                        m.UserId == editorUserGuid
                        && m.TwitchUserId == "tw-editor"
                        && m.Role == ManagementRole.Editor
                        && m.Source == MembershipSource.HelixEditors
                    )
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Membership_handler_catches_and_logs_a_failure_without_throwing()
    {
        IMembershipService membership = Substitute.For<IMembershipService>();
        IUserService users = Substitute.For<IUserService>();
        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        ITwitchChannelsApi channels = Substitute.For<ITwitchChannelsApi>();

        moderators
            .GetModeratorsAsync(
                Arg.Any<Guid>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new InvalidOperationException("twitch is down"));

        ListLogger<MembershipSeedOnOnboardingHandler> log = new();
        MembershipSeedOnOnboardingHandler sut = new(membership, users, moderators, channels, log);

        Func<Task> act = () => sut.HandleAsync(Event());

        await act.Should().NotThrowAsync();
        log.Entries.Should().Contain(e => e.Level == LogLevel.Error);
        // The downstream sync was never reached, but the failure was contained.
        await membership
            .DidNotReceive()
            .SyncManagementFromTwitchAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyList<TwitchManagementMember>>(),
                Arg.Any<CancellationToken>()
            );
    }

    private static Result<UserDto> UserResult(Guid id, string username, string displayName) =>
        Result.Success(
            new UserDto(
                id.ToString(),
                username,
                displayName,
                null,
                null,
                DateTime.UtcNow,
                DateTime.UtcNow
            )
        );
}
