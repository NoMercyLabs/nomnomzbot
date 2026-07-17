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
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves tenant-resolution access (Gate 1, roles-permissions §0/§3.1, post-fix): Gate 1 is pure entry — ANY
/// authenticated caller may resolve tenant context for a channel that exists, whether or not they own it,
/// moderate it, or hold a management membership. This is what lets community-plane participants (viewers,
/// subs, VIPs — Everyone(0) floor) reach community-plane endpoints scoped to a streamer's channel they have no
/// management relationship with (e.g. <c>music:request:submit</c>, <c>pronouns:self:write</c>); Gate 2
/// (<c>IActionAuthorizationService</c> / <c>IRoleResolver</c>) is what still blocks them from management
/// actions, since an unrelated caller's resolved level is 0. Only a malformed id or a channel that does not
/// exist is denied here.
/// </summary>
public sealed class ChannelAccessServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000f01");
    private static readonly Guid User = Guid.Parse("0192a000-0000-7000-8000-000000000f02");
    private static readonly DateTime Now = new(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);

    private static (ChannelAccessService Sut, AuthDbContext Db) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        return (new ChannelAccessService(db), db);
    }

    private static async Task SeedChannelAsync(AuthDbContext db, Guid ownerUserId)
    {
        db.Channels.Add(
            new Channel
            {
                Id = Channel,
                OwnerUserId = ownerUserId,
                TwitchChannelId = "1",
                Name = "ch",
                NameNormalized = "ch",
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Grants_access_to_the_channel_owner()
    {
        (ChannelAccessService sut, AuthDbContext db) = Build();
        await SeedChannelAsync(db, ownerUserId: User);

        (await sut.CanResolveTenantAsync(User.ToString(), Channel.ToString())).Should().BeTrue();
    }

    [Fact]
    public async Task Grants_access_to_an_authenticated_user_with_zero_relationship_to_the_channel()
    {
        // The Gate 1 fix: a plain viewer — not the owner, not a moderator, not a management member, holding no
        // permit grant on this channel at all — still resolves the tenant. Before the fix this returned false
        // and 403'd every non-management participant before Gate 2 (the actual per-action floor check) ever ran.
        (ChannelAccessService sut, AuthDbContext db) = Build();
        await SeedChannelAsync(db, ownerUserId: Guid.NewGuid()); // owned by someone else entirely

        (await sut.CanResolveTenantAsync(User.ToString(), Channel.ToString())).Should().BeTrue();
    }

    [Fact]
    public async Task Grants_access_to_a_management_member_too()
    {
        // Not the reason access is granted anymore (Gate 1 no longer inspects this table), but a management
        // member must still resolve the tenant they administer.
        (ChannelAccessService sut, AuthDbContext db) = Build();
        await SeedChannelAsync(db, ownerUserId: Guid.NewGuid());
        db.ChannelMemberships.Add(
            new ChannelMembership
            {
                BroadcasterId = Channel,
                UserId = User,
                ManagementRole = ManagementRole.Editor,
                LevelValue = ManagementRole.Editor.ToLevel(),
                Source = MembershipSource.HelixEditors,
                GrantedAt = Now,
            }
        );
        await db.SaveChangesAsync();

        (await sut.CanResolveTenantAsync(User.ToString(), Channel.ToString())).Should().BeTrue();
    }

    [Fact]
    public async Task Denies_a_suspended_tenant_even_for_its_owner()
    {
        // Suspension enforcement (stream-admin.md §3.2): a suspended / platform-banned tenant refuses tenant
        // resolution at Gate 1 — its whole channel-scoped API surface goes dark until reinstated.
        (ChannelAccessService sut, AuthDbContext db) = Build();
        await SeedChannelAsync(db, ownerUserId: User);
        Channel channel = db.Channels.Single(c => c.Id == Channel);
        channel.Status = AuthEnums.ChannelStatus.Suspended;
        channel.SuspendedAt = Now;
        await db.SaveChangesAsync();

        (await sut.CanResolveTenantAsync(User.ToString(), Channel.ToString())).Should().BeFalse();
    }

    [Fact]
    public async Task Denies_when_the_channel_does_not_exist()
    {
        (ChannelAccessService sut, _) = Build();

        (await sut.CanResolveTenantAsync(User.ToString(), Channel.ToString())).Should().BeFalse();
    }

    [Fact]
    public async Task Denies_a_malformed_user_id()
    {
        (ChannelAccessService sut, AuthDbContext db) = Build();
        await SeedChannelAsync(db, ownerUserId: User);

        (await sut.CanResolveTenantAsync("not-a-guid", Channel.ToString())).Should().BeFalse();
    }

    [Fact]
    public async Task Denies_a_malformed_channel_id()
    {
        (ChannelAccessService sut, AuthDbContext db) = Build();
        await SeedChannelAsync(db, ownerUserId: User);

        (await sut.CanResolveTenantAsync(User.ToString(), "not-a-guid")).Should().BeFalse();
    }
}
