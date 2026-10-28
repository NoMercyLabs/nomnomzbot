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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Content.Identity;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// roles-permissions §0.3, against the REAL seeded catalogue: any OBS write needs Editor, a plain Moderator is
/// refused, reach below Editor is only a named per-user grant, and the Bot manager bundle opens the authoring
/// surfaces without opening moderation or OBS.
/// </summary>
public sealed class ObsFloorAndBotManagerTests
{
    private static readonly Guid Channel = Guid.Parse("0192b000-0000-7000-8000-0000000000e1");
    private static readonly Guid Mod = Guid.Parse("0192b000-0000-7000-8000-0000000000e2");
    private static readonly Guid EditorUser = Guid.Parse("0192b000-0000-7000-8000-0000000000e3");
    private static readonly Guid Viewer = Guid.Parse("0192b000-0000-7000-8000-0000000000e4");
    private static readonly Guid Owner = Guid.Parse("0192b000-0000-7000-8000-0000000000e5");
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    // The write keys of ObsController: every POST, plus the three config writes and the broadcast bundle.
    private static readonly string[] ObsWriteKeys =
    [
        "obs:control",
        "obs:control:broadcast",
        "obs:config:write",
    ];

    private static readonly string[] BundleSpecList =
    [
        "commands:*",
        "pipelines:*",
        "eventresponses:*",
        "chattriggers:*",
        "voicetriggers:*",
        "chatpolls:*",
        "timers:*",
        "widget:*",
        "sounds:*",
        "quotes:*",
        "sdk:read",
        "code:script:author",
        "bundles:read",
        "bundles:export",
        "bundles:import",
        "tts:config:*",
        "tts:voice:*",
        "music:config:*",
        "music:library:write",
    ];

    private static async Task<(
        ActionAuthorizationService Sut,
        RoleResolver Resolver,
        AuthDbContext Db
    )> BuildAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        await new ActionDefinitionSeeder(db).SeedAsync();
        AddMembership(db, Mod, ManagementRole.Moderator);
        AddMembership(db, EditorUser, ManagementRole.Editor);
        await db.SaveChangesAsync();

        FakeTimeProvider clock = new(Now);
        RoleResolver resolver = new(
            db,
            clock,
            Substitute.For<IActAsMembershipOverlay>(),
            Substitute.For<IViewAsScope>()
        );
        ActionAuthorizationService sut = new(db, resolver, new RecordingEventBus(), clock);
        return (sut, resolver, db);
    }

    private static void AddMembership(AuthDbContext db, Guid user, ManagementRole role) =>
        db.ChannelMemberships.Add(
            new()
            {
                BroadcasterId = Channel,
                UserId = user,
                ManagementRole = role,
                LevelValue = role.ToLevel(),
                Source = MembershipSource.TwitchBadge,
                GrantedAt = Now.UtcDateTime,
            }
        );

    private static async Task<PermitGrant> GrantAsync(AuthDbContext db, Guid user, string key)
    {
        Guid actionId = await db
            .ActionDefinitions.Where(a => a.ActionKey == key)
            .Select(a => a.Id)
            .SingleAsync();
        PermitGrant grant = new()
        {
            BroadcasterId = Channel,
            UserId = user,
            GrantType = PermitGrantType.Capability,
            ActionDefinitionId = actionId,
            GrantedByUserId = Owner,
        };
        db.PermitGrants.Add(grant);
        await db.SaveChangesAsync();
        return grant;
    }

    [Theory]
    [InlineData("obs:control")]
    [InlineData("obs:control:broadcast")]
    [InlineData("obs:config:write")]
    public async Task A_plain_Moderator_is_refused_every_OBS_write_key(string key)
    {
        (ActionAuthorizationService sut, _, _) = await BuildAsync();

        Result<bool> allowed = await sut.AuthorizeActionAsync(Mod, Channel, key);

        allowed.Value.Should().BeFalse($"'{key}' is an OBS write; Moderator is below the default");
    }

    /// <summary>Stoney 2026-10-19: "obs is a new thing, editor with lowering options to mods and individuals".</summary>
    [Fact]
    public async Task The_seeded_obs_control_row_defaults_to_Editor_can_be_lowered_to_Moderator_and_is_grantable()
    {
        (_, _, AuthDbContext db) = await BuildAsync();

        ActionDefinition control = await db.ActionDefinitions.SingleAsync(a =>
            a.ActionKey == "obs:control"
        );

        control.DefaultLevel.Should().Be(ManagementRole.Editor.ToLevel());
        control.FloorLevel.Should().Be(ManagementRole.Moderator.ToLevel());
        control.FloorTier.Should().Be(DangerTier.Critical);
        control.IsGrantableViaPermit.Should().BeTrue();
    }

    [Fact]
    public async Task A_broadcaster_who_lowers_obs_control_to_Moderator_lets_a_Moderator_control_OBS()
    {
        (ActionAuthorizationService sut, _, AuthDbContext db) = await BuildAsync();
        Guid controlId = await db
            .ActionDefinitions.Where(a => a.ActionKey == "obs:control")
            .Select(a => a.Id)
            .SingleAsync();
        db.ChannelActionOverrides.Add(
            new()
            {
                BroadcasterId = Channel,
                ActionDefinitionId = controlId,
                OverrideLevel = ManagementRole.Moderator.ToLevel(),
            }
        );
        await db.SaveChangesAsync();

        (await sut.AuthorizeActionAsync(Mod, Channel, "obs:control")).Value.Should().BeTrue();
        (await sut.AuthorizeActionAsync(Mod, Channel, "obs:control:broadcast"))
            .Value.Should()
            .BeFalse("going live and ending the stream stay with the broadcaster");
        (await sut.AuthorizeActionAsync(Viewer, Channel, "obs:control"))
            .Value.Should()
            .BeFalse("the floor stops at Moderator");
    }

    [Fact]
    public async Task An_Editor_is_allowed_obs_control_but_not_the_broadcaster_only_keys()
    {
        (ActionAuthorizationService sut, _, _) = await BuildAsync();

        (await sut.AuthorizeActionAsync(EditorUser, Channel, "obs:control"))
            .Value.Should()
            .BeTrue();
        (await sut.AuthorizeActionAsync(EditorUser, Channel, "obs:config:write"))
            .Value.Should()
            .BeFalse();
    }

    [Fact]
    public async Task A_Moderator_can_read_the_scene_list_but_not_write()
    {
        (ActionAuthorizationService sut, _, _) = await BuildAsync();

        (await sut.AuthorizeActionAsync(Mod, Channel, "obs:scene:read")).Value.Should().BeTrue();
        (await sut.AuthorizeActionAsync(Mod, Channel, "obs:control")).Value.Should().BeFalse();
    }

    [Fact]
    public async Task A_per_user_obs_control_grant_opens_it_for_a_Moderator_until_revoked()
    {
        (ActionAuthorizationService sut, _, AuthDbContext db) = await BuildAsync();
        (await sut.AuthorizeActionAsync(Mod, Channel, "obs:control")).Value.Should().BeFalse();

        PermitGrant grant = await GrantAsync(db, Mod, "obs:control");

        (await sut.AuthorizeActionAsync(Mod, Channel, "obs:control")).Value.Should().BeTrue();
        (await sut.AuthorizeActionAsync(Mod, Channel, "obs:control:broadcast"))
            .Value.Should()
            .BeFalse("the grant names obs:control only, not the broadcast key");

        grant.RevokedAt = Now.UtcDateTime;
        await db.SaveChangesAsync();

        (await sut.AuthorizeActionAsync(Mod, Channel, "obs:control")).Value.Should().BeFalse();
    }

    [Theory]
    [InlineData("commands:write")]
    [InlineData("pipelines:write")]
    [InlineData("eventresponses:write")]
    [InlineData("timers:write")]
    [InlineData("widget:write")]
    [InlineData("code:script:author")]
    [InlineData("tts:config:write")]
    [InlineData("music:library:write")]
    public async Task A_Bot_manager_who_is_a_plain_viewer_can_edit_the_authoring_surfaces(
        string key
    )
    {
        (ActionAuthorizationService sut, _, AuthDbContext db) = await BuildAsync();
        (await sut.AuthorizeActionAsync(Viewer, Channel, key))
            .Value.Should()
            .BeFalse("a viewer without the bundle holds none of the authoring keys");

        await GrantAsync(db, Viewer, BotManagerBundle.Key);

        (await sut.AuthorizeActionAsync(Viewer, Channel, key)).Value.Should().BeTrue();
    }

    [Theory]
    [InlineData("moderation:ban")]
    [InlineData("moderation:timeout")]
    [InlineData("obs:control")]
    [InlineData("obs:control:broadcast")]
    [InlineData("roles:manage")]
    [InlineData("bundles:publish")]
    public async Task A_Bot_manager_is_refused_moderation_obs_and_role_management(string key)
    {
        (ActionAuthorizationService sut, _, AuthDbContext db) = await BuildAsync();
        await GrantAsync(db, Viewer, BotManagerBundle.Key);

        Result<bool> allowed = await sut.AuthorizeActionAsync(Viewer, Channel, key);

        allowed.Value.Should().BeFalse($"the Bot manager bundle never covers '{key}'");
    }

    [Fact]
    public async Task A_revoked_Bot_manager_bundle_stops_opening_the_authoring_keys()
    {
        (ActionAuthorizationService sut, _, AuthDbContext db) = await BuildAsync();
        PermitGrant grant = await GrantAsync(db, Viewer, BotManagerBundle.Key);
        (await sut.AuthorizeActionAsync(Viewer, Channel, "commands:write")).Value.Should().BeTrue();

        grant.RevokedAt = Now.UtcDateTime;
        await db.SaveChangesAsync();

        (await sut.AuthorizeActionAsync(Viewer, Channel, "commands:write"))
            .Value.Should()
            .BeFalse();
    }

    [Fact]
    public async Task The_effective_permissions_of_a_Bot_manager_list_the_bundle_expansion_without_obs_writes()
    {
        (_, RoleResolver resolver, AuthDbContext db) = await BuildAsync();
        await GrantAsync(db, Viewer, BotManagerBundle.Key);

        Result<ResolvedAccessDto> access = await resolver.ResolveAccessAsync(Viewer, Channel);

        access
            .Value.HeldActionKeys.Should()
            .Contain(["commands:write", "pipelines:write", "widget:write", "code:script:author"]);
        access
            .Value.HeldActionKeys.Should()
            .NotContain(["obs:control", "moderation:ban", "bundles:publish"]);
    }

    [Fact]
    public void The_bundle_member_list_equals_the_spec_list()
    {
        BotManagerBundle.Members.Should().Equal(BundleSpecList);
        BotManagerBundle.Key.Should().Be("bot:manager:author");
    }

    [Fact]
    public async Task Every_bundle_member_is_a_seeded_action_key_prefix_or_exact_key()
    {
        (_, _, AuthDbContext db) = await BuildAsync();
        List<string> seeded = await db.ActionDefinitions.Select(a => a.ActionKey).ToListAsync();

        foreach (string member in BotManagerBundle.Members)
        {
            bool found = member.EndsWith('*')
                ? seeded.Any(k => k.StartsWith(member[..^1], StringComparison.Ordinal))
                : seeded.Contains(member);
            found.Should().BeTrue($"bundle member '{member}' must match a seeded key");
        }
    }

    [Theory]
    [InlineData("bundles:publish", false)]
    [InlineData("obs:control", false)]
    [InlineData("roles:manage", false)]
    [InlineData("permit:issue", false)]
    [InlineData("moderation:ban", false)]
    [InlineData("commands:write", true)]
    [InlineData("tts:config:write", true)]
    [InlineData("code:script:author", true)]
    public void Covers_matches_exact_keys_and_wildcard_prefixes_only(string key, bool covered)
    {
        BotManagerBundle.Covers(key).Should().Be(covered);
    }
}
