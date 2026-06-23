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
/// Proves tenant-resolution access (the IDOR gate / roles-permissions Gate 1, §3.1): a caller may resolve a
/// channel if they own it, hold a legacy moderator grant, OR — the new branch — hold a management membership
/// (Moderator/LeadModerator/Editor/Broadcaster); everyone else, and any malformed id, is denied.
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

    [Fact]
    public async Task Grants_access_to_the_channel_owner()
    {
        (ChannelAccessService sut, AuthDbContext db) = Build();
        db.Channels.Add(
            new Channel
            {
                Id = Channel,
                OwnerUserId = User,
                TwitchChannelId = "1",
                Name = "ch",
                NameNormalized = "ch",
            }
        );
        await db.SaveChangesAsync();

        (await sut.CanResolveTenantAsync(User.ToString(), Channel.ToString())).Should().BeTrue();
    }

    [Fact]
    public async Task Grants_access_to_a_management_member()
    {
        (ChannelAccessService sut, AuthDbContext db) = Build();
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
    public async Task Grants_access_to_a_legacy_moderator_grant()
    {
        (ChannelAccessService sut, AuthDbContext db) = Build();
        db.ChannelModerators.Add(
            new ChannelModerator
            {
                ChannelId = Channel,
                UserId = User,
                Role = "moderator",
                GrantedAt = Now,
            }
        );
        await db.SaveChangesAsync();

        (await sut.CanResolveTenantAsync(User.ToString(), Channel.ToString())).Should().BeTrue();
    }

    [Fact]
    public async Task Denies_a_user_with_no_relationship_to_the_channel()
    {
        (ChannelAccessService sut, _) = Build();

        (await sut.CanResolveTenantAsync(User.ToString(), Channel.ToString())).Should().BeFalse();
    }

    [Fact]
    public async Task Denies_a_malformed_id()
    {
        (ChannelAccessService sut, _) = Build();

        (await sut.CanResolveTenantAsync("not-a-guid", Channel.ToString())).Should().BeFalse();
    }
}
