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
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Content.Identity;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// S-MOD-PERMS end-to-end: proves the owner's real complaint — "I feel super restricted when I act as a
/// moderator on someone else's channel" — is actually fixed, against the REAL seeded catalogue (not a
/// hand-crafted <see cref="ActionDefinition"/>), for a caller who holds only a
/// <see cref="ManagementRole.Moderator"/> membership on a channel they do not own.
/// </summary>
public sealed class ModeratorPermissionsScenarioTests
{
    private static readonly Guid Channel = Guid.Parse("0192b000-0000-7000-8000-0000000000d1");
    private static readonly Guid Moderator = Guid.Parse("0192b000-0000-7000-8000-0000000000d2");
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-0000000000d3");
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    private static async Task<(ActionAuthorizationService Sut, AuthDbContext Db)> BuildSeededAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        await new ActionDefinitionSeeder(db).SeedAsync();
        db.ChannelMemberships.Add(
            new()
            {
                BroadcasterId = Channel,
                UserId = Moderator,
                ManagementRole = ManagementRole.Moderator,
                LevelValue = ManagementRole.Moderator.ToLevel(),
                Source = MembershipSource.TwitchBadge,
                GrantedAt = Now.UtcDateTime,
            }
        );
        await db.SaveChangesAsync();

        FakeTimeProvider clock = new(Now);
        RoleResolver resolver = new(db, clock);
        ActionAuthorizationService sut = new(db, resolver, new RecordingEventBus(), clock);
        return (sut, db);
    }

    [Theory]
    [InlineData("commands:write")]
    [InlineData("music:remote:control")]
    [InlineData("code:script:author")]
    public async Task Moderator_can_use_bot_internal_tooling_on_a_channel_they_do_not_own(
        string key
    )
    {
        (ActionAuthorizationService sut, _) = await BuildSeededAsync();

        Result<bool> allowed = await sut.AuthorizeActionAsync(Moderator, Channel, key);

        allowed
            .Value.Should()
            .BeTrue($"'{key}' is bot-internal tooling, opened to Moderator by default");
    }

    [Theory]
    [InlineData("billing:manage")]
    [InlineData("integration:write")]
    [InlineData("roles:manage")]
    [InlineData("automation:tokens:write")]
    public async Task Moderator_cannot_touch_money_credentials_or_privilege_on_that_same_channel(
        string key
    )
    {
        (ActionAuthorizationService sut, _) = await BuildSeededAsync();

        Result<bool> allowed = await sut.AuthorizeActionAsync(Moderator, Channel, key);

        allowed
            .Value.Should()
            .BeFalse($"'{key}' touches money, credentials, or privilege — Broadcaster-only always");
    }

    [Fact]
    public async Task A_deliberate_channel_override_still_wins_over_the_new_default()
    {
        // The streamer may still TIGHTEN a category-2 action back up past its new Moderator default —
        // opening bot tooling to moderators must not remove the broadcaster's ability to restrict it again.
        (ActionAuthorizationService sut, AuthDbContext db) = await BuildSeededAsync();

        Result<int> stored = await sut.SetActionOverrideAsync(
            Channel,
            "commands:write",
            40,
            Broadcaster
        );
        stored.IsSuccess.Should().BeTrue();

        Result<bool> allowed = await sut.AuthorizeActionAsync(Moderator, Channel, "commands:write");

        allowed
            .Value.Should()
            .BeFalse(
                "a per-channel override that raises the bar past the seeded default must still win"
            );
    }

    [Fact]
    public async Task A_deliberate_channel_override_can_also_loosen_below_the_new_default()
    {
        // Symmetric proof: the broadcaster may also lower a floor-lowerable action further via override —
        // the new defaults are a floor for convenience, not a ceiling on the broadcaster's own choice.
        (ActionAuthorizationService sut, AuthDbContext db) = await BuildSeededAsync();
        Guid trustedVip = Guid.Parse("0192b000-0000-7000-8000-0000000000d4");
        db.ChannelCommunityStandings.Add(
            new()
            {
                BroadcasterId = Channel,
                UserId = trustedVip,
                Standing = CommunityStanding.Vip,
                LevelValue = CommunityStanding.Vip.ToLevel(),
                Source = StandingSource.ChatTags,
            }
        );
        await db.SaveChangesAsync();

        Result<int> stored = await sut.SetActionOverrideAsync(
            Channel,
            "commands:read",
            4,
            Broadcaster
        );
        stored.IsSuccess.Should().BeTrue();

        Result<bool> allowed = await sut.AuthorizeActionAsync(trustedVip, Channel, "commands:read");

        allowed.Value.Should().BeTrue("the broadcaster lowered commands:read to its Vip(4) floor");
    }

    [Fact]
    public async Task Reseeding_an_existing_install_applies_the_new_default_but_preserves_the_override()
    {
        // The owner's install-upgrade concern: a live channel that already set its OWN override for
        // commands:write must keep that choice across a reseed that also corrects the global default.
        AuthDbContext db = AuthTestBuilder.NewContext();
        ActionDefinitionSeeder seeder = new(db);
        await seeder.SeedAsync();
        await db.SaveChangesAsync();

        FakeTimeProvider clock = new(Now);
        RoleResolver resolver = new(db, clock);
        ActionAuthorizationService sut = new(db, resolver, new RecordingEventBus(), clock);
        await sut.SetActionOverrideAsync(Channel, "commands:write", 40, Broadcaster);

        // Re-run the seeder exactly as a deploy/startup would.
        await seeder.SeedAsync();
        await db.SaveChangesAsync();

        ActionDefinition definition = await db.ActionDefinitions.SingleAsync(a =>
            a.ActionKey == "commands:write"
        );
        definition
            .DefaultLevel.Should()
            .Be(10, "the reseed still applies the corrected global default");

        (await sut.GetEffectiveLevelAsync(Channel, "commands:write"))
            .Value.Should()
            .Be(40, "the channel's own override survives the reseed untouched");
    }
}
