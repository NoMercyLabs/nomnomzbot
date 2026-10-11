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
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Content.Identity;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// S-IA-MOD-FLOORS: moderators manage stream and channel behaviors; the risky items stay with the channel
/// manager (Editor) and up. Runs against the REAL seeded catalogue and the real gate.
/// </summary>
public sealed class ModeratorStreamAndChannelFloorsTests
{
    private const int ModLevel = 10;
    private const int EditorLevel = 30;

    private static readonly Guid Channel = Guid.Parse("0192b000-0000-7000-8000-0000000000e1");
    private static readonly Guid Moderator = Guid.Parse("0192b000-0000-7000-8000-0000000000e2");
    private static readonly Guid Vip = Guid.Parse("0192b000-0000-7000-8000-0000000000e3");
    private static readonly Guid Owner = Guid.Parse("0192b000-0000-7000-8000-0000000000e4");
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    public static readonly string[] ModeratorKeys =
    [
        "stream:preset:write",
        "stream:schedule:write",
        "channel:title:write",
        "channel:game:write",
        "channel:tags:write",
        "channel:language:write",
        "channel:extensions:write",
        "live-ops:predictions:write",
        "live-ops:schedule:write",
        "music:config:write",
        "moderation:chatcolor:write",
        "economy:config:write",
        "economy:earning-rules:write",
        "economy:earning-rules:delete",
        "economy:games:write",
        "economy:catalog:create",
        "economy:catalog:update",
        "economy:catalog:delete",
        "economy:leaderboards:config:write",
        "economy:leaderboards:config:delete",
        "economy:jars:update",
        "economy:jars:delete",
        "bundles:export",
        "bundles:import",
    ];

    public static readonly string[] RiskyKeys =
    [
        "channel:ccl:write",
        "channel:brandedcontent:write",
        "live-ops:ads:write",
        "live-ops:raids:write",
        "webhooks:inbound:write",
        "webhooks:outbound:write",
        "egress:allowlist:write",
        "integration:write",
        "automation:tokens:write",
        "eventsub:subscribe",
        "eventsub:unsubscribe",
        "chat:whisper:send",
    ];

    public static IEnumerable<object[]> ModeratorKeyData() =>
        ModeratorKeys.Select(k => new object[] { k });

    public static IEnumerable<object[]> RiskyKeyData() => RiskyKeys.Select(k => new object[] { k });

    private static async Task<(ActionAuthorizationService Sut, AuthDbContext Db)> BuildAsync()
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
        db.ChannelCommunityStandings.Add(
            new()
            {
                BroadcasterId = Channel,
                UserId = Vip,
                Standing = CommunityStanding.Vip,
                LevelValue = CommunityStanding.Vip.ToLevel(),
                Source = StandingSource.ChatTags,
            }
        );
        await db.SaveChangesAsync();

        FakeTimeProvider clock = new(Now);
        RoleResolver resolver = new(db, clock, Substitute.For<IActAsMembershipOverlay>());
        ActionAuthorizationService sut = new(db, resolver, new RecordingEventBus(), clock);
        return (sut, db);
    }

    [Theory]
    [MemberData(nameof(ModeratorKeyData))]
    public async Task Moderator_passes_the_gate_and_a_vip_is_refused(string key)
    {
        (ActionAuthorizationService sut, _) = await BuildAsync();

        (await sut.AuthorizeActionAsync(Moderator, Channel, key))
            .Value.Should()
            .BeTrue($"'{key}' is stream/channel behavior a Moderator manages");
        (await sut.AuthorizeActionAsync(Vip, Channel, key))
            .Value.Should()
            .BeFalse($"'{key}' is still a Moderator-floored action; a VIP must not pass");
    }

    [Theory]
    [MemberData(nameof(ModeratorKeyData))]
    public async Task Seeder_moves_an_existing_install_with_the_old_floor_to_the_moderator_floor(
        string key
    )
    {
        (_, AuthDbContext db) = await BuildAsync();
        ActionDefinition row = await db.ActionDefinitions.SingleAsync(a => a.ActionKey == key);
        row.DefaultLevel = EditorLevel;
        row.FloorLevel = EditorLevel;
        await db.SaveChangesAsync();

        await new ActionDefinitionSeeder(db).SeedAsync();
        await db.SaveChangesAsync();

        ActionDefinition after = await db.ActionDefinitions.SingleAsync(a => a.ActionKey == key);
        after.DefaultLevel.Should().Be(ModLevel);
        after.FloorLevel.Should().Be(ModLevel);
    }

    [Theory]
    [MemberData(nameof(ModeratorKeyData))]
    public async Task A_broadcaster_override_that_raises_the_action_is_kept_across_a_reseed(
        string key
    )
    {
        (ActionAuthorizationService sut, AuthDbContext db) = await BuildAsync();
        (await sut.SetActionOverrideAsync(Channel, key, EditorLevel, Owner))
            .IsSuccess.Should()
            .BeTrue();

        await new ActionDefinitionSeeder(db).SeedAsync();
        await db.SaveChangesAsync();

        (await sut.GetEffectiveLevelAsync(Channel, key)).Value.Should().Be(EditorLevel);
        (await sut.AuthorizeActionAsync(Moderator, Channel, key))
            .Value.Should()
            .BeFalse("the channel raised the bar past the Moderator default");
    }

    [Theory]
    [MemberData(nameof(RiskyKeyData))]
    public async Task Risky_items_stay_above_the_moderator_floor(string key)
    {
        (ActionAuthorizationService sut, _) = await BuildAsync();

        (await sut.AuthorizeActionAsync(Moderator, Channel, key))
            .Value.Should()
            .BeFalse($"'{key}' is a risky item left to the channel manager and up");
    }
}
