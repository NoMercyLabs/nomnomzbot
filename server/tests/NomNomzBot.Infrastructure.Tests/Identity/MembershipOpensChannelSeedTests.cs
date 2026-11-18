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

using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves a moderator membership that OPENS a channel (an onboarded user moderates a channel that never
/// onboarded) raises the onboarding event once, so the default content is seeded; a membership that opens
/// nothing raises nothing.
/// </summary>
public sealed class MembershipOpensChannelSeedTests
{
    private static readonly Guid Moderator = Guid.Parse("0192a000-0000-7000-8000-00000000f001");
    private static readonly Guid ModeratorHome = Guid.Parse("0192a000-0000-7000-8000-00000000f002");
    private static readonly Guid Target = Guid.Parse("0192a000-0000-7000-8000-00000000f003");
    private static readonly Guid Stranger = Guid.Parse("0192a000-0000-7000-8000-00000000f004");
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private static (MembershipService Sut, AuthDbContext Db, RecordingEventBus Bus) Build(
        bool targetOnboarded
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(Channel(ModeratorHome, Moderator, "tw-home", "homechan", true));
        db.Channels.Add(
            Channel(Target, Guid.NewGuid(), "tw-target", "targetchan", targetOnboarded)
        );
        db.SaveChanges();

        RecordingEventBus bus = new();
        FakeTimeProvider clock = new(Now);
        MembershipService sut = new(
            db,
            new RoleResolver(
                db,
                clock,
                Substitute.For<IActAsMembershipOverlay>(),
                Substitute.For<IViewAsScope>()
            ),
            bus,
            clock
        );
        return (sut, db, bus);
    }

    // Stoney, 2026-11-18: an onboarded moderator no longer opens a channel that did not onboard, so the bot
    // seeds nothing there ("you were to monitor channels for spam that are not onboarded").
    [Fact]
    public async Task Moderator_grant_by_an_onboarded_user_raises_no_onboarding_event()
    {
        (MembershipService sut, _, RecordingEventBus bus) = Build(targetOnboarded: false);

        await sut.SetManagementRoleAsync(
            Target,
            Moderator,
            ManagementRole.Moderator,
            MembershipSource.TwitchBadge,
            grantedByUserId: null
        );
        await sut.SetManagementRoleAsync(
            Target,
            Moderator,
            ManagementRole.Editor,
            MembershipSource.TwitchBadge,
            grantedByUserId: null
        );

        bus.Published.OfType<ChannelOnboardedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task Twitch_sync_that_adds_an_onboarded_moderator_raises_no_onboarding_event()
    {
        (MembershipService sut, _, RecordingEventBus bus) = Build(targetOnboarded: false);

        await sut.SyncManagementFromTwitchAsync(
            Target,
            [
                new(
                    Moderator,
                    "tw-moderator",
                    ManagementRole.Moderator,
                    MembershipSource.TwitchBadge
                ),
            ],
            new HashSet<MembershipSource> { MembershipSource.TwitchBadge }
        );

        bus.Published.OfType<ChannelOnboardedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task Moderator_who_owns_no_onboarded_channel_raises_no_onboarding_event()
    {
        (MembershipService sut, _, RecordingEventBus bus) = Build(targetOnboarded: false);

        await sut.SetManagementRoleAsync(
            Target,
            Stranger,
            ManagementRole.Moderator,
            MembershipSource.TwitchBadge,
            grantedByUserId: null
        );

        bus.Published.OfType<ChannelOnboardedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task Grant_on_an_already_onboarded_channel_raises_no_onboarding_event()
    {
        (MembershipService sut, _, RecordingEventBus bus) = Build(targetOnboarded: true);

        await sut.SetManagementRoleAsync(
            Target,
            Moderator,
            ManagementRole.Moderator,
            MembershipSource.TwitchBadge,
            grantedByUserId: null
        );

        bus.Published.OfType<ChannelOnboardedEvent>().Should().BeEmpty();
    }

    private static Channel Channel(
        Guid id,
        Guid ownerUserId,
        string twitchId,
        string name,
        bool isOnboarded
    ) =>
        new()
        {
            Id = id,
            OwnerUserId = ownerUserId,
            TwitchChannelId = twitchId,
            Name = name,
            NameNormalized = name,
            IsOnboarded = isOnboarded,
        };
}
