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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

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
        MembershipService sut = new(
            db,
            new RoleResolver(
                db,
                clock,
                Substitute.For<IActAsMembershipOverlay>(),
                Substitute.For<NomNomzBot.Application.Abstractions.Auth.IViewAsScope>()
            ),
            bus,
            clock
        );
        return (sut, db, bus);
    }

    private static void SeedMembership(
        AuthDbContext db,
        Guid userId,
        ManagementRole role,
        MembershipSource source
    ) =>
        db.ChannelMemberships.Add(
            new()
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

    private static void SeedChannelOwnedBy(AuthDbContext db, Guid ownerUserId) =>
        db.Channels.Add(
            new()
            {
                Id = Channel,
                OwnerUserId = ownerUserId,
                TwitchChannelId = "tw-owned",
                Name = "owned",
                NameNormalized = "owned",
            }
        );

    [Fact]
    public async Task Set_refuses_to_change_the_real_owners_role()
    {
        (MembershipService sut, AuthDbContext db, RecordingEventBus bus) = Build();
        SeedChannelOwnedBy(db, Target);
        SeedMembership(db, Target, ManagementRole.Editor, MembershipSource.BotGrant);
        await db.SaveChangesAsync();

        Result<ChannelMembershipDto> result = await sut.SetManagementRoleAsync(
            Channel,
            Target,
            ManagementRole.Moderator,
            MembershipSource.BotGrant,
            grantedByUserId: null
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        ChannelMembership row = await db.ChannelMemberships.SingleAsync(m => m.UserId == Target);
        row.ManagementRole.Should().Be(ManagementRole.Editor);
        row.LevelValue.Should().Be(30);
        bus.Published.OfType<ManagementRoleChangedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task Set_refuses_the_broadcaster_role_for_anyone_but_the_owner()
    {
        (MembershipService sut, AuthDbContext db, RecordingEventBus bus) = Build();
        SeedChannelOwnedBy(db, Grantor);
        await db.SaveChangesAsync();

        Result<ChannelMembershipDto> result = await sut.SetManagementRoleAsync(
            Channel,
            Target,
            ManagementRole.Broadcaster,
            MembershipSource.BotGrant,
            grantedByUserId: Grantor
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await db.ChannelMemberships.AnyAsync(m => m.UserId == Target)).Should().BeFalse();
        bus.Published.OfType<ManagementRoleChangedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task Set_lets_the_owner_demote_a_non_owner_who_holds_a_stored_broadcaster_label()
    {
        (MembershipService sut, AuthDbContext db, _) = Build();
        SeedChannelOwnedBy(db, Grantor);
        SeedMembership(db, Target, ManagementRole.Broadcaster, MembershipSource.BotGrant);
        await db.SaveChangesAsync();

        Result<ChannelMembershipDto> result = await sut.SetManagementRoleAsync(
            Channel,
            Target,
            ManagementRole.LeadModerator,
            MembershipSource.BotGrant,
            grantedByUserId: Grantor
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Role.Should().Be(ManagementRole.LeadModerator);
        result.Value.LevelValue.Should().Be(20);
        result.Value.IsOwner.Should().BeFalse();
        (await db.ChannelMemberships.SingleAsync(m => m.UserId == Target))
            .LevelValue.Should()
            .Be(20);
    }

    [Fact]
    public async Task Remove_refuses_the_real_owner_even_with_a_bot_grant_row()
    {
        (MembershipService sut, AuthDbContext db, _) = Build();
        SeedChannelOwnedBy(db, Target);
        SeedMembership(db, Target, ManagementRole.Editor, MembershipSource.BotGrant);
        await db.SaveChangesAsync();

        Result result = await sut.RemoveManagementRoleAsync(Channel, Target, removedByUserId: null);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await db.ChannelMemberships.AnyAsync(m => m.UserId == Target && m.DeletedAt == null))
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task List_flags_only_the_real_owner_as_owner()
    {
        (MembershipService sut, AuthDbContext db, _) = Build();
        SeedChannelOwnedBy(db, Grantor);
        SeedMembership(db, Grantor, ManagementRole.Editor, MembershipSource.BotGrant);
        SeedMembership(db, Target, ManagementRole.Broadcaster, MembershipSource.BotGrant);
        await db.SaveChangesAsync();

        Result<PagedList<ChannelMembershipDto>> result = await sut.ListMembershipsAsync(
            Channel,
            1,
            50
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Single(m => m.UserId == Grantor).IsOwner.Should().BeTrue();
        result.Value.Items.Single(m => m.UserId == Target).IsOwner.Should().BeFalse();
    }

    [Fact]
    public async Task A_stored_broadcaster_row_on_a_non_owner_resolves_to_the_editor_ceiling()
    {
        (_, AuthDbContext db, _) = Build();
        FakeTimeProvider clock = new(Now);
        RoleResolver resolver = new(
            db,
            clock,
            Substitute.For<IActAsMembershipOverlay>(),
            Substitute.For<NomNomzBot.Application.Abstractions.Auth.IViewAsScope>()
        );
        SeedChannelOwnedBy(db, Grantor);
        SeedMembership(db, Target, ManagementRole.Broadcaster, MembershipSource.BotGrant);
        await db.SaveChangesAsync();

        Result<int> level = await resolver.ResolveEffectiveLevelAsync(Target, Channel);

        level.Value.Should().Be(30);
        (await resolver.ResolveEffectiveLevelAsync(Grantor, Channel)).Value.Should().Be(40);
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
        Result result = await sut.SyncManagementFromTwitchAsync(
            Channel,
            snapshot,
            new HashSet<MembershipSource>
            {
                MembershipSource.TwitchBadge,
                MembershipSource.HelixEditors,
            }
        );

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
    public async Task Sync_leaves_a_bot_grant_untouched_when_that_user_also_appears_in_the_twitch_snapshot()
    {
        // Regression (2026-08-24 prod incident, aaoa_): a user already holding an active BotGrant/Editor row
        // also showed up as a Twitch moderator. The partial unique index allows only ONE active row per
        // (broadcaster, user) — the old lookup only considered synced-source rows, so it missed the existing
        // BotGrant row and tried to INSERT a second active row for the same user, throwing 23505 and aborting
        // every subsequent onboarding-seed step sharing the DbContext.
        (MembershipService sut, AuthDbContext db, _) = Build();
        SeedMembership(db, Target, ManagementRole.Editor, MembershipSource.BotGrant);
        await db.SaveChangesAsync();

        List<TwitchManagementMember> snapshot =
        [
            new(Target, "tw-target", ManagementRole.Moderator, MembershipSource.TwitchBadge),
        ];
        Result result = await sut.SyncManagementFromTwitchAsync(
            Channel,
            snapshot,
            new HashSet<MembershipSource>
            {
                MembershipSource.TwitchBadge,
                MembershipSource.HelixEditors,
            }
        );

        result.IsSuccess.Should().BeTrue();
        (await db.ChannelMemberships.CountAsync(m => m.UserId == Target)).Should().Be(1);
        ChannelMembership row = await db.ChannelMemberships.SingleAsync(m => m.UserId == Target);
        row.Source.Should().Be(MembershipSource.BotGrant);
        row.ManagementRole.Should().Be(ManagementRole.Editor);
    }

    [Fact]
    public async Task Sync_does_not_prune_a_source_whose_read_failed()
    {
        // Prune-safety: only HelixEditors was read this run (badge read failed). A stale badge row must survive —
        // a transient moderator-read failure must never wipe the channel's mod roles — while a stale editor row
        // (authoritative source, absent from the snapshot) is still pruned.
        (MembershipService sut, AuthDbContext db, _) = Build();
        Guid staleBadge = Guid.Parse("0192a000-0000-7000-8000-0000000000f0");
        Guid staleEditor = Guid.Parse("0192a000-0000-7000-8000-0000000000f1");
        SeedMembership(db, staleBadge, ManagementRole.Moderator, MembershipSource.TwitchBadge);
        SeedMembership(db, staleEditor, ManagementRole.Editor, MembershipSource.HelixEditors);
        await db.SaveChangesAsync();

        Result result = await sut.SyncManagementFromTwitchAsync(
            Channel,
            [],
            new HashSet<MembershipSource> { MembershipSource.HelixEditors }
        );

        result.IsSuccess.Should().BeTrue();
        (await db.ChannelMemberships.AnyAsync(m => m.UserId == staleBadge && m.DeletedAt == null))
            .Should()
            .BeTrue("the badge source was not read this run, so its rows are left intact");
        (await db.ChannelMemberships.AnyAsync(m => m.UserId == staleEditor && m.DeletedAt == null))
            .Should()
            .BeFalse(
                "the editor source was authoritative and this row was absent from the snapshot"
            );
    }

    [Fact]
    public async Task List_returns_members_highest_level_first_with_usernames()
    {
        (MembershipService sut, AuthDbContext db, _) = Build();
        db.Users.Add(
            new()
            {
                Id = Target,
                Username = "mod_user",
                UsernameNormalized = "mod_user",
                TwitchUserId = "1",
                DisplayName = "Mod User",
            }
        );
        db.Users.Add(
            new()
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
