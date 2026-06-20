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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves Gate 2 (roles-permissions §3.3): a caller is allowed iff their resolved level meets the action's
/// effective required level; a level denial emits <see cref="AuthorizationDeniedEvent"/>; an unknown action
/// key fails closed with no event; an override below the action's floor is rejected; a raised override changes
/// the effective level and emits <see cref="ActionLevelOverriddenEvent"/>; and a reset reverts to the default.
/// </summary>
public sealed class ActionAuthorizationServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000c1");
    private static readonly Guid User = Guid.Parse("0192a000-0000-7000-8000-0000000000c2");
    private static readonly Guid Actor = Guid.Parse("0192a000-0000-7000-8000-0000000000c3");
    private static readonly DateTimeOffset Now = new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

    private static (ActionAuthorizationService Sut, AuthDbContext Db, RecordingEventBus Bus) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        RecordingEventBus bus = new();
        FakeTimeProvider clock = new(Now);
        RoleResolver resolver = new(db, clock);
        ActionAuthorizationService sut = new(db, resolver, bus, clock);
        return (sut, db, bus);
    }

    private static ActionDefinition SeedAction(
        AuthDbContext db,
        string key,
        int defaultLevel,
        int floor,
        DangerTier tier = DangerTier.Low
    )
    {
        ActionDefinition action = new()
        {
            ActionKey = key,
            Plane = AuthPlane.Management,
            DefaultLevel = defaultLevel,
            FloorLevel = floor,
            FloorTier = tier,
            IsGrantableViaPermit = true,
        };
        db.ActionDefinitions.Add(action);
        return action;
    }

    private static void SeedModerator(AuthDbContext db)
    {
        db.ChannelMemberships.Add(
            new ChannelMembership
            {
                BroadcasterId = Channel,
                UserId = User,
                ManagementRole = ManagementRole.Moderator,
                LevelValue = ManagementRole.Moderator.ToLevel(),
                Source = MembershipSource.TwitchBadge,
                GrantedAt = Now.UtcDateTime,
            }
        );
    }

    [Fact]
    public async Task Authorize_allows_when_caller_level_meets_required()
    {
        (ActionAuthorizationService sut, AuthDbContext db, RecordingEventBus bus) = Build();
        SeedAction(db, "economy:config:read", defaultLevel: 10, floor: 10);
        SeedModerator(db);
        await db.SaveChangesAsync();

        Result<bool> allowed = await sut.AuthorizeActionAsync(User, Channel, "economy:config:read");

        allowed.Value.Should().BeTrue();
        bus.Published.OfType<AuthorizationDeniedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task Authorize_denies_and_emits_event_when_below_required()
    {
        (ActionAuthorizationService sut, AuthDbContext db, RecordingEventBus bus) = Build();
        SeedAction(db, "economy:config:write", defaultLevel: 40, floor: 30);
        SeedModerator(db); // Moderator = 10, below 40
        await db.SaveChangesAsync();

        Result<bool> allowed = await sut.AuthorizeActionAsync(
            User,
            Channel,
            "economy:config:write"
        );

        allowed.Value.Should().BeFalse();
        AuthorizationDeniedEvent denied = bus.Published.OfType<AuthorizationDeniedEvent>().Single();
        denied.CallerUserId.Should().Be(User);
        denied.ActionKey.Should().Be("economy:config:write");
        denied.RequiredLevel.Should().Be(40);
        denied.CallerLevel.Should().Be(10);
        denied.Gate.Should().Be("gate2");
    }

    [Fact]
    public async Task Authorize_fails_closed_on_an_unknown_action_with_no_event()
    {
        (ActionAuthorizationService sut, _, RecordingEventBus bus) = Build();

        Result<bool> allowed = await sut.AuthorizeActionAsync(User, Channel, "does:not:exist");

        allowed.Value.Should().BeFalse();
        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task SetOverride_below_the_floor_is_rejected()
    {
        (ActionAuthorizationService sut, AuthDbContext db, _) = Build();
        SeedAction(db, "moderation:ban", defaultLevel: 10, floor: 10);
        await db.SaveChangesAsync();

        Result<int> result = await sut.SetActionOverrideAsync(Channel, "moderation:ban", 4, Actor);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task SetOverride_raises_the_effective_level_and_emits_the_event()
    {
        (ActionAuthorizationService sut, AuthDbContext db, RecordingEventBus bus) = Build();
        SeedAction(db, "economy:config:read", defaultLevel: 10, floor: 10);
        await db.SaveChangesAsync();

        Result<int> stored = await sut.SetActionOverrideAsync(
            Channel,
            "economy:config:read",
            30,
            Actor
        );

        stored.Value.Should().Be(30);
        (await sut.GetEffectiveLevelAsync(Channel, "economy:config:read")).Value.Should().Be(30);
        ActionLevelOverriddenEvent evt = bus
            .Published.OfType<ActionLevelOverriddenEvent>()
            .Single();
        evt.ActionKey.Should().Be("economy:config:read");
        evt.OldLevel.Should().BeNull();
        evt.NewEffectiveLevel.Should().Be(30);
    }

    [Fact]
    public async Task ResetOverride_reverts_to_the_default()
    {
        (ActionAuthorizationService sut, AuthDbContext db, _) = Build();
        SeedAction(db, "economy:config:read", defaultLevel: 10, floor: 10);
        await db.SaveChangesAsync();
        await sut.SetActionOverrideAsync(Channel, "economy:config:read", 30, Actor);

        await sut.ResetActionOverrideAsync(Channel, "economy:config:read", Actor);

        (await sut.GetEffectiveLevelAsync(Channel, "economy:config:read")).Value.Should().Be(10);
    }
}
