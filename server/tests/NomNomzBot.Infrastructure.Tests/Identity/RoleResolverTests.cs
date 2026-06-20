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
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the effective-level MAX rule (roles-permissions §3.2): the resolved level is the maximum across the
/// three planes — community standing, management role, and an active permit role grant — that revoked /
/// expired permits are excluded, that the breakdown reports the winning source, and that a capability is held
/// either by a direct grant or by meeting the action's effective required level (unknown actions fail closed).
/// </summary>
public sealed class RoleResolverTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000b1");
    private static readonly Guid User = Guid.Parse("0192a000-0000-7000-8000-0000000000b2");
    private static readonly DateTimeOffset Now = new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

    private static (RoleResolver Sut, AuthDbContext Db) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        RoleResolver sut = new(db, new FakeTimeProvider(Now));
        return (sut, db);
    }

    [Fact]
    public async Task ResolveEffectiveLevel_takes_the_MAX_across_all_three_planes()
    {
        (RoleResolver sut, AuthDbContext db) = Build();
        db.ChannelCommunityStandings.Add(
            new ChannelCommunityStanding
            {
                BroadcasterId = Channel,
                UserId = User,
                Standing = CommunityStanding.Vip,
                LevelValue = CommunityStanding.Vip.ToLevel(),
                Source = StandingSource.ChatTags,
            }
        );
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
        db.PermitGrants.Add(
            new PermitGrant
            {
                BroadcasterId = Channel,
                UserId = User,
                GrantType = PermitGrantType.Role,
                GrantedRole = ManagementRole.Editor,
                GrantedByUserId = Channel,
            }
        );
        await db.SaveChangesAsync();

        Result<int> level = await sut.ResolveEffectiveLevelAsync(User, Channel);

        // MAX(Vip 4, Moderator 10, permit Editor 30) = 30.
        level.IsSuccess.Should().BeTrue();
        level.Value.Should().Be(30);
    }

    [Fact]
    public async Task ResolveEffectiveLevel_with_no_records_is_Everyone_zero()
    {
        (RoleResolver sut, _) = Build();

        Result<int> level = await sut.ResolveEffectiveLevelAsync(User, Channel);

        level.Value.Should().Be(0);
    }

    [Fact]
    public async Task ResolveEffectiveLevel_excludes_expired_and_revoked_permits()
    {
        (RoleResolver sut, AuthDbContext db) = Build();
        db.PermitGrants.Add(
            new PermitGrant
            {
                BroadcasterId = Channel,
                UserId = User,
                GrantType = PermitGrantType.Role,
                GrantedRole = ManagementRole.Broadcaster,
                GrantedByUserId = Channel,
                ExpiresAt = Now.UtcDateTime.AddHours(-1), // expired
            }
        );
        db.PermitGrants.Add(
            new PermitGrant
            {
                BroadcasterId = Channel,
                UserId = User,
                GrantType = PermitGrantType.Role,
                GrantedRole = ManagementRole.Editor,
                GrantedByUserId = Channel,
                RevokedAt = Now.UtcDateTime, // revoked
            }
        );
        await db.SaveChangesAsync();

        Result<int> level = await sut.ResolveEffectiveLevelAsync(User, Channel);

        // Both permits are inactive → no contribution → Everyone(0).
        level.Value.Should().Be(0);
    }

    [Fact]
    public async Task ResolveAccess_reports_the_winning_source()
    {
        (RoleResolver sut, AuthDbContext db) = Build();
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
        db.PermitGrants.Add(
            new PermitGrant
            {
                BroadcasterId = Channel,
                UserId = User,
                GrantType = PermitGrantType.Role,
                GrantedRole = ManagementRole.Editor,
                GrantedByUserId = Channel,
            }
        );
        await db.SaveChangesAsync();

        Result<ResolvedAccessDto> access = await sut.ResolveAccessAsync(User, Channel);

        access.Value.EffectiveLevel.Should().Be(30);
        access.Value.WinningSource.Should().Be("permit");
        access.Value.ManagementLevel.Should().Be(10);
        access.Value.PermitRole.Should().Be(ManagementRole.Editor);
    }

    [Fact]
    public async Task HasCapability_is_true_when_resolved_level_meets_the_action_floor()
    {
        (RoleResolver sut, AuthDbContext db) = Build();
        db.ActionDefinitions.Add(
            new ActionDefinition
            {
                ActionKey = "economy:config:read",
                Plane = AuthPlane.Management,
                DefaultLevel = 10,
                FloorLevel = 10,
                FloorTier = DangerTier.Low,
                IsGrantableViaPermit = true,
            }
        );
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
        await db.SaveChangesAsync();

        (await sut.HasCapabilityAsync(User, Channel, "economy:config:read"))
            .Value.Should()
            .BeTrue();
        (await sut.HasCapabilityAsync(User, Channel, "unknown:action")).Value.Should().BeFalse();
    }

    [Fact]
    public async Task HasCapability_is_true_via_a_direct_capability_grant_below_the_floor()
    {
        (RoleResolver sut, AuthDbContext db) = Build();
        ActionDefinition action = new()
        {
            ActionKey = "code:script:author",
            Plane = AuthPlane.Management,
            DefaultLevel = 40,
            FloorLevel = 40,
            FloorTier = DangerTier.Critical,
            IsGrantableViaPermit = true,
        };
        db.ActionDefinitions.Add(action);
        // The caller is only a Moderator (10) — far below the Critical floor (40) — but holds a direct grant.
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
        await db.SaveChangesAsync();
        db.PermitGrants.Add(
            new PermitGrant
            {
                BroadcasterId = Channel,
                UserId = User,
                GrantType = PermitGrantType.Capability,
                ActionDefinitionId = action.Id,
                GrantedByUserId = Channel,
            }
        );
        await db.SaveChangesAsync();

        (await sut.HasCapabilityAsync(User, Channel, "code:script:author")).Value.Should().BeTrue();
    }
}
