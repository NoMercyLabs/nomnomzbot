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
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves <c>!permit</c> / <c>!unpermit</c> (roles-permissions §3.6): role grants honor no-escalation;
/// capability grants are default-deny (must be permit-grantable) AND no-escalation (the grantor must hold the
/// action); a granted role/capability actually lifts the target's resolved access; revoke removes matching
/// grants; and the expiry sweep revokes only past-due grants.
/// </summary>
public sealed class PermitServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000a00");
    private static readonly Guid Owner = Guid.Parse("0192a000-0000-7000-8000-000000000a01");
    private static readonly Guid Mod = Guid.Parse("0192a000-0000-7000-8000-000000000a02");
    private static readonly Guid Target = Guid.Parse("0192a000-0000-7000-8000-000000000a03");
    private static readonly DateTimeOffset Now = new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

    private static (
        PermitService Sut,
        AuthDbContext Db,
        RecordingEventBus Bus,
        RoleResolver Resolver
    ) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        RecordingEventBus bus = new();
        FakeTimeProvider clock = new(Now);
        RoleResolver resolver = new(db, clock);
        PermitService sut = new(db, resolver, bus, clock);
        return (sut, db, bus, resolver);
    }

    private static void SeedMembership(AuthDbContext db, Guid userId, ManagementRole role) =>
        db.ChannelMemberships.Add(
            new ChannelMembership
            {
                BroadcasterId = Channel,
                UserId = userId,
                ManagementRole = role,
                LevelValue = role.ToLevel(),
                Source = MembershipSource.TwitchBadge,
                GrantedAt = Now.UtcDateTime,
            }
        );

    private static void SeedAction(AuthDbContext db, string key, int floor, bool grantable) =>
        db.ActionDefinitions.Add(
            new ActionDefinition
            {
                ActionKey = key,
                Plane = AuthPlane.Management,
                DefaultLevel = floor,
                FloorLevel = floor,
                FloorTier = grantable ? DangerTier.Low : DangerTier.Critical,
                IsGrantableViaPermit = grantable,
            }
        );

    [Fact]
    public async Task GrantRole_blocks_escalation_above_the_grantor()
    {
        (PermitService sut, AuthDbContext db, _, _) = Build();
        SeedMembership(db, Mod, ManagementRole.Moderator); // 10
        await db.SaveChangesAsync();

        Result<PermitGrantDto> result = await sut.GrantRoleAsync(
            Channel,
            Target,
            ManagementRole.Editor, // 30
            Mod,
            expiresAt: null,
            reason: null
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task GrantRole_succeeds_lifts_the_target_and_emits()
    {
        (PermitService sut, AuthDbContext db, RecordingEventBus bus, RoleResolver resolver) =
            Build();
        SeedMembership(db, Owner, ManagementRole.Broadcaster); // 40
        await db.SaveChangesAsync();

        Result<PermitGrantDto> result = await sut.GrantRoleAsync(
            Channel,
            Target,
            ManagementRole.Editor,
            Owner,
            expiresAt: null,
            reason: "guest editor"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.GrantedRole.Should().Be(ManagementRole.Editor);
        (await resolver.ResolveEffectiveLevelAsync(Target, Channel)).Value.Should().Be(30);
        bus.Published.OfType<PermitGrantedEvent>()
            .Single()
            .GrantedRole.Should()
            .Be(ManagementRole.Editor);
    }

    [Fact]
    public async Task GrantCapability_default_denies_a_non_grantable_action()
    {
        (PermitService sut, AuthDbContext db, _, _) = Build();
        SeedMembership(db, Owner, ManagementRole.Broadcaster);
        SeedAction(db, "roles:manage", floor: 40, grantable: false);
        await db.SaveChangesAsync();

        Result<PermitGrantDto> result = await sut.GrantCapabilityAsync(
            Channel,
            Target,
            "roles:manage",
            Owner,
            expiresAt: null,
            reason: null
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task GrantCapability_blocks_a_grantor_who_does_not_hold_the_action()
    {
        (PermitService sut, AuthDbContext db, _, _) = Build();
        SeedMembership(db, Mod, ManagementRole.Moderator); // 10
        SeedAction(db, "economy:config:write", floor: 30, grantable: true); // Mod (10) doesn't clear it
        await db.SaveChangesAsync();

        Result<PermitGrantDto> result = await sut.GrantCapabilityAsync(
            Channel,
            Target,
            "economy:config:write",
            Mod,
            expiresAt: null,
            reason: null
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task GrantCapability_succeeds_and_lifts_the_target_for_that_action()
    {
        (PermitService sut, AuthDbContext db, _, RoleResolver resolver) = Build();
        SeedMembership(db, Owner, ManagementRole.Broadcaster);
        SeedAction(db, "economy:config:read", floor: 10, grantable: true);
        await db.SaveChangesAsync();

        (await resolver.HasCapabilityAsync(Target, Channel, "economy:config:read"))
            .Value.Should()
            .BeFalse();

        Result<PermitGrantDto> result = await sut.GrantCapabilityAsync(
            Channel,
            Target,
            "economy:config:read",
            Owner,
            expiresAt: null,
            reason: null
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.CapabilityActionKey.Should().Be("economy:config:read");
        (await resolver.HasCapabilityAsync(Target, Channel, "economy:config:read"))
            .Value.Should()
            .BeTrue();
    }

    [Fact]
    public async Task Revoke_by_role_removes_the_grant_and_lowers_the_target()
    {
        (PermitService sut, AuthDbContext db, _, RoleResolver resolver) = Build();
        SeedMembership(db, Owner, ManagementRole.Broadcaster);
        await db.SaveChangesAsync();
        await sut.GrantRoleAsync(Channel, Target, ManagementRole.Editor, Owner, null, null);
        (await resolver.ResolveEffectiveLevelAsync(Target, Channel)).Value.Should().Be(30);

        Result result = await sut.RevokeAsync(Channel, Target, "Editor", Owner);

        result.IsSuccess.Should().BeTrue();
        (await resolver.ResolveEffectiveLevelAsync(Target, Channel)).Value.Should().Be(0);
    }

    [Fact]
    public async Task Revoke_with_null_selector_removes_all_active_grants()
    {
        (PermitService sut, AuthDbContext db, _, _) = Build();
        SeedMembership(db, Owner, ManagementRole.Broadcaster);
        SeedAction(db, "economy:config:read", floor: 10, grantable: true);
        await db.SaveChangesAsync();
        await sut.GrantRoleAsync(Channel, Target, ManagementRole.Moderator, Owner, null, null);
        await sut.GrantCapabilityAsync(Channel, Target, "economy:config:read", Owner, null, null);

        await sut.RevokeAsync(Channel, Target, actionKeyOrRole: null, Owner);

        (await sut.ListActiveGrantsAsync(Channel)).Value.Should().BeEmpty();
    }

    [Fact]
    public async Task ExpireDueGrants_revokes_only_past_due_grants()
    {
        (PermitService sut, AuthDbContext db, RecordingEventBus bus, _) = Build();
        SeedMembership(db, Owner, ManagementRole.Broadcaster);
        await db.SaveChangesAsync();
        await sut.GrantRoleAsync(
            Channel,
            Target,
            ManagementRole.Moderator,
            Owner,
            expiresAt: Now.UtcDateTime.AddMinutes(-1), // due
            reason: null
        );
        await sut.GrantRoleAsync(
            Channel,
            Mod,
            ManagementRole.Moderator,
            Owner,
            expiresAt: Now.UtcDateTime.AddHours(1), // not due
            reason: null
        );
        bus.Published.Clear();

        Result<int> swept = await sut.ExpireDueGrantsAsync();

        swept.Value.Should().Be(1);
        bus.Published.OfType<PermitRevokedEvent>().Single().Reason.Should().Be("expired");
        (await sut.ListActiveGrantsAsync(Channel))
            .Value.Should()
            .ContainSingle(g => g.UserId == Mod);
    }
}
