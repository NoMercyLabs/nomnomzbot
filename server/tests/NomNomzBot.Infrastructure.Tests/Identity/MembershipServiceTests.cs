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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves Plane-B membership writes + sync (roles-permissions §3.4): an upsert recomputes the ladder level and
/// emits the change event; the no-escalation guard blocks granting above the grantor's own level; the owner is
/// non-removable; and Twitch sync reconciles only the externally-sourced rows — adding new members, pruning
/// stale ones, and leaving owner / bot-grant memberships untouched.
/// </summary>
public sealed class MembershipServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000d0");
    private static readonly Guid Target = Guid.Parse("0192a000-0000-7000-8000-0000000000d1");
    private static readonly Guid Grantor = Guid.Parse("0192a000-0000-7000-8000-0000000000d2");
    private static readonly DateTimeOffset Now = new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

    private static (MembershipService Sut, AuthDbContext Db, RecordingEventBus Bus) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        RecordingEventBus bus = new();
        FakeTimeProvider clock = new(Now);
        MembershipService sut = new(db, new RoleResolver(db, clock), bus, clock);
        return (sut, db, bus);
    }

    private static void SeedMembership(
        AuthDbContext db,
        Guid userId,
        ManagementRole role,
        MembershipSource source
    ) =>
        db.ChannelMemberships.Add(
            new ChannelMembership
            {
                BroadcasterId = Channel,
                UserId = userId,
                ManagementRole = role,
                LevelValue = role.ToLevel(),
                Source = source,
                GrantedAt = Now.UtcDateTime,
            }
        );

    [Fact]
    public async Task Set_creates_membership_recomputes_level_and_emits_event()
    {
        (MembershipService sut, AuthDbContext db, RecordingEventBus bus) = Build();

        Result<ChannelMembershipDto> result = await sut.SetManagementRoleAsync(
            Channel,
            Target,
            ManagementRole.Editor,
            MembershipSource.BotGrant,
            grantedByUserId: null
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Role.Should().Be(ManagementRole.Editor);
        result.Value.LevelValue.Should().Be(30);
        (await db.ChannelMemberships.CountAsync(m => m.UserId == Target)).Should().Be(1);
        ManagementRoleChangedEvent evt = bus
            .Published.OfType<ManagementRoleChangedEvent>()
            .Single();
        evt.OldRole.Should().BeNull();
        evt.NewRole.Should().Be(ManagementRole.Editor);
    }

    [Fact]
    public async Task Set_enforces_no_escalation_above_the_grantor_level()
    {
        (MembershipService sut, AuthDbContext db, _) = Build();
        SeedMembership(db, Grantor, ManagementRole.Moderator, MembershipSource.TwitchBadge); // grantor = 10
        await db.SaveChangesAsync();

        Result<ChannelMembershipDto> result = await sut.SetManagementRoleAsync(
            Channel,
            Target,
            ManagementRole.Editor, // 30 > 10
            MembershipSource.BotGrant,
            grantedByUserId: Grantor
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("FORBIDDEN");
        (await db.ChannelMemberships.AnyAsync(m => m.UserId == Target)).Should().BeFalse();
    }

    [Fact]
    public async Task Set_allows_a_grantor_at_or_above_the_granted_role()
    {
        (MembershipService sut, AuthDbContext db, _) = Build();
        SeedMembership(db, Grantor, ManagementRole.Broadcaster, MembershipSource.Owner); // 40
        await db.SaveChangesAsync();

        Result<ChannelMembershipDto> result = await sut.SetManagementRoleAsync(
            Channel,
            Target,
            ManagementRole.Editor,
            MembershipSource.BotGrant,
            grantedByUserId: Grantor
        );

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Remove_soft_deletes_and_emits_a_null_role_event()
    {
        (MembershipService sut, AuthDbContext db, RecordingEventBus bus) = Build();
        SeedMembership(db, Target, ManagementRole.Moderator, MembershipSource.TwitchBadge);
        await db.SaveChangesAsync();

        Result result = await sut.RemoveManagementRoleAsync(Channel, Target, removedByUserId: null);

        result.IsSuccess.Should().BeTrue();
        (await db.ChannelMemberships.AnyAsync(m => m.UserId == Target && m.DeletedAt == null))
            .Should()
            .BeFalse();
        bus.Published.OfType<ManagementRoleChangedEvent>().Single().NewRole.Should().BeNull();
    }

    [Fact]
    public async Task Remove_refuses_to_delete_the_owner()
    {
        (MembershipService sut, AuthDbContext db, _) = Build();
        SeedMembership(db, Target, ManagementRole.Broadcaster, MembershipSource.Owner);
        await db.SaveChangesAsync();

        Result result = await sut.RemoveManagementRoleAsync(Channel, Target, removedByUserId: null);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await db.ChannelMemberships.AnyAsync(m => m.UserId == Target && m.DeletedAt == null))
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task Sync_adds_new_prunes_stale_and_leaves_owner_and_bot_grant_untouched()
    {
        (MembershipService sut, AuthDbContext db, _) = Build();
        Guid owner = Guid.Parse("0192a000-0000-7000-8000-0000000000e0");
        Guid botGrant = Guid.Parse("0192a000-0000-7000-8000-0000000000e1");
        Guid staleBadge = Guid.Parse("0192a000-0000-7000-8000-0000000000e2");
        Guid fresh = Guid.Parse("0192a000-0000-7000-8000-0000000000e3");
        SeedMembership(db, owner, ManagementRole.Broadcaster, MembershipSource.Owner);
        SeedMembership(db, botGrant, ManagementRole.Editor, MembershipSource.BotGrant);
        SeedMembership(db, staleBadge, ManagementRole.Moderator, MembershipSource.TwitchBadge);
        await db.SaveChangesAsync();

        List<TwitchManagementMember> snapshot =
        [
            new(fresh, "tw-fresh", ManagementRole.Editor, MembershipSource.HelixEditors),
        ];
        Result result = await sut.SyncManagementFromTwitchAsync(Channel, snapshot);

        result.IsSuccess.Should().BeTrue();
        List<ChannelMembership> live = await db
            .ChannelMemberships.Where(m => m.DeletedAt == null)
            .ToListAsync();
        live.Select(m => m.UserId).Should().BeEquivalentTo([owner, botGrant, fresh]);
        live.Single(m => m.UserId == fresh).LastSyncedAt.Should().Be(Now.UtcDateTime);
        // The stale badge-sourced row was pruned; owner + bot-grant survive.
        (await db.ChannelMemberships.AnyAsync(m => m.UserId == staleBadge && m.DeletedAt == null))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task List_returns_members_highest_level_first_with_usernames()
    {
        (MembershipService sut, AuthDbContext db, _) = Build();
        db.Users.Add(
            new User
            {
                Id = Target,
                Username = "mod_user",
                UsernameNormalized = "mod_user",
                TwitchUserId = "1",
                DisplayName = "Mod User",
            }
        );
        db.Users.Add(
            new User
            {
                Id = Grantor,
                Username = "editor_user",
                UsernameNormalized = "editor_user",
                TwitchUserId = "2",
                DisplayName = "Editor User",
            }
        );
        SeedMembership(db, Target, ManagementRole.Moderator, MembershipSource.TwitchBadge);
        SeedMembership(db, Grantor, ManagementRole.Editor, MembershipSource.HelixEditors);
        await db.SaveChangesAsync();

        Result<PagedList<ChannelMembershipDto>> result = await sut.ListMembershipsAsync(
            Channel,
            page: 1,
            pageSize: 25
        );

        result.Value.Items.Should().HaveCount(2);
        result.Value.Items[0].Role.Should().Be(ManagementRole.Editor); // 30 before 10
        result.Value.Items[0].Username.Should().Be("editor_user");
        result.Value.Items[1].Username.Should().Be("mod_user");
    }
}
