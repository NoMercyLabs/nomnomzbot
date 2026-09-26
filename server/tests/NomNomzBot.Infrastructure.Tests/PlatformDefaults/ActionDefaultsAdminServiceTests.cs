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
using NomNomzBot.Application.PlatformDefaults.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Content.Identity;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.PlatformDefaults;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.PlatformDefaults;

/// <summary>
/// Plan item A4, family 1: the platform admin edits an action's DEFAULT level at runtime. Proves the edit moves
/// what Gate 2 actually enforces for a channel that has no override of its own, leaves a channel WITH an
/// override alone, survives the seeder's next run, refuses a level below the floor or off the ladder, demands
/// confirmation for a dangerous action, counts the blast radius right, refuses a stale count, and reads back.
/// </summary>
public sealed class ActionDefaultsAdminServiceTests
{
    // commands:write ships Default = Floor = Moderator(10) in the real catalogue — the seeder test re-runs it.
    private const string CommandsWrite = "commands:write";
    private static readonly Guid Follower = Guid.Parse("0199f100-0000-7000-8000-00000000a001");
    private static readonly Guid Overridden = Guid.Parse("0199f100-0000-7000-8000-00000000a002");
    private static readonly Guid Suspended = Guid.Parse("0199f100-0000-7000-8000-00000000a003");
    private static readonly Guid Mod = Guid.Parse("0199f100-0000-7000-8000-00000000a004");
    private static readonly Guid Admin = Guid.Parse("0199f100-0000-7000-8000-00000000a005");
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        AuthDbContext Db,
        ActionDefaultsAdminService Sut,
        ActionAuthorizationService Gate
    );

    private static async Task<Harness> BuildAsync(DangerTier tier = DangerTier.Low)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        FakeTimeProvider clock = new(Now);
        db.Channels.AddRange(
            NewChannel(Follower, "alpha", AuthEnums.ChannelStatus.Active),
            NewChannel(Overridden, "bravo", AuthEnums.ChannelStatus.Active),
            NewChannel(Suspended, "charlie", AuthEnums.ChannelStatus.Suspended)
        );
        ActionDefinition action = new()
        {
            ActionKey = CommandsWrite,
            Plane = AuthPlane.Management,
            DefaultLevel = 10,
            FloorLevel = 10,
            FloorTier = tier,
            IsGrantableViaPermit = true,
        };
        db.ActionDefinitions.Add(action);
        // bravo pins the action at Moderator on purpose — its own setting must keep winning.
        db.ChannelActionOverrides.Add(
            new()
            {
                BroadcasterId = Overridden,
                ActionDefinitionId = action.Id,
                OverrideLevel = 10,
                SetByUserId = Overridden,
            }
        );
        foreach (Guid channel in new[] { Follower, Overridden })
        {
            db.ChannelMemberships.Add(
                new()
                {
                    BroadcasterId = channel,
                    UserId = Mod,
                    ManagementRole = ManagementRole.Moderator,
                    LevelValue = ManagementRole.Moderator.ToLevel(),
                    Source = MembershipSource.TwitchBadge,
                    GrantedAt = Now.UtcDateTime,
                }
            );
        }
        await db.SaveChangesAsync();

        ActionAuthorizationService gate = new(
            db,
            new RoleResolver(db, clock),
            new RecordingEventBus(),
            clock
        );
        return new(db, new(db, clock), gate);
    }

    private static Channel NewChannel(Guid id, string name, string status) =>
        new()
        {
            Id = id,
            OwnerUserId = id,
            Provider = AuthEnums.Platform.Twitch,
            ExternalChannelId = name + "-ext",
            Name = name,
            NameNormalized = name,
            Status = status,
        };

    private static SetActionDefaultRequest Raise(int level, int confirmed, bool danger = false) =>
        new(level, confirmed, danger);

    [Fact]
    public async Task Raising_the_default_changes_gate2_for_a_follower_but_not_for_an_overridden_channel()
    {
        Harness h = await BuildAsync();
        (await h.Gate.AuthorizeActionAsync(Mod, Follower, CommandsWrite)).Value.Should().BeTrue();

        Result<ActionDefaultDto> saved = await h.Sut.SetAsync(
            CommandsWrite,
            Raise(30, confirmed: 1),
            Admin
        );

        saved.IsSuccess.Should().BeTrue(saved.ErrorMessage);
        (await h.Gate.AuthorizeActionAsync(Mod, Follower, CommandsWrite))
            .Value.Should()
            .BeFalse("alpha has no override, so it follows the new Editor default");
        (await h.Gate.GetEffectiveLevelAsync(Follower, CommandsWrite)).Value.Should().Be(30);
        (await h.Gate.AuthorizeActionAsync(Mod, Overridden, CommandsWrite))
            .Value.Should()
            .BeTrue("the Moderator override of bravo keeps winning over the platform default");
        (await h.Gate.GetEffectiveLevelAsync(Overridden, CommandsWrite)).Value.Should().Be(10);
    }

    [Fact]
    public async Task The_saved_row_is_read_back_and_audited_with_the_confirmed_blast_radius()
    {
        Harness h = await BuildAsync();

        Result<ActionDefaultDto> saved = await h.Sut.SetAsync(
            CommandsWrite,
            Raise(30, confirmed: 1),
            Admin
        );

        saved
            .Value.Should()
            .BeEquivalentTo(
                new ActionDefaultDto(
                    CommandsWrite,
                    AuthPlane.Management,
                    null,
                    ShippedDefaultLevel: 10,
                    PlatformDefaultLevel: 30,
                    EffectiveDefaultLevel: 30,
                    FloorLevel: 10,
                    DangerTier.Low,
                    ChannelOverrideCount: 1
                )
            );
        ActionDefinition stored = await h.Db.ActionDefinitions.AsNoTracking().SingleAsync();
        stored.PlatformDefaultSetByUserId.Should().Be(Admin);
        stored.PlatformDefaultSetAt.Should().Be(Now.UtcDateTime);
        IamAuditLog audit = await h.Db.IamAuditLogs.SingleAsync();
        audit.Permission.Should().Be("platform_default:action_level");
        audit.TargetResource.Should().Be(CommandsWrite);
        audit.PrincipalId.Should().Be(Admin);
        audit.AffectedTenantCount.Should().Be(1);
        audit.Justification.Should().Be("old=10;new=30");
    }

    [Fact]
    public async Task The_admin_edit_survives_the_seeder_re_running_on_the_next_deploy()
    {
        Harness h = await BuildAsync();
        await h.Sut.SetAsync(CommandsWrite, Raise(30, confirmed: 1), Admin);

        await new ActionDefinitionSeeder(h.Db).SeedAsync();
        await h.Db.SaveChangesAsync();

        ActionDefinition stored = await h
            .Db.ActionDefinitions.AsNoTracking()
            .SingleAsync(a => a.ActionKey == CommandsWrite);
        stored.DefaultLevel.Should().Be(10, "the seeder still owns the shipped default");
        stored.PlatformDefaultLevel.Should().Be(30, "the seeder never overwrites the admin edit");
        (await h.Gate.GetEffectiveLevelAsync(Follower, CommandsWrite)).Value.Should().Be(30);
    }

    [Fact]
    public async Task Blast_radius_counts_active_followers_and_the_channels_keeping_their_own_setting()
    {
        Harness h = await BuildAsync();

        PlatformDefaultBlastRadiusDto radius = (await h.Sut.PreviewAsync(CommandsWrite, 30)).Value;

        radius
            .ChannelsAffected.Should()
            .Be(1, "only alpha follows — bravo overrides, charlie is suspended");
        radius.ChannelsKeepingOwnSetting.Should().Be(1);
        radius.SampleChannelNames.Should().Equal("alpha");
        radius.RequiresDangerConfirmation.Should().BeFalse();

        PlatformDefaultBlastRadiusDto noChange = (
            await h.Sut.PreviewAsync(CommandsWrite, 10)
        ).Value;
        noChange.ChannelsAffected.Should().Be(0, "the proposed level equals the current default");
    }

    [Fact]
    public async Task A_stale_confirmed_count_is_refused_and_nothing_changes()
    {
        Harness h = await BuildAsync();

        Result<ActionDefaultDto> result = await h.Sut.SetAsync(
            CommandsWrite,
            Raise(30, confirmed: 2),
            Admin
        );

        result.ErrorCode.Should().Be("PREVIEW_STALE");
        (await h.Db.ActionDefinitions.AsNoTracking().SingleAsync())
            .PlatformDefaultLevel.Should()
            .BeNull();
        (await h.Db.IamAuditLogs.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(4)] // Vip — below the Moderator floor
    [InlineData(15)] // not a rung on the ladder
    [InlineData(50)] // above Broadcaster
    public async Task A_level_below_the_floor_or_off_the_ladder_is_refused(int level)
    {
        Harness h = await BuildAsync();

        Result<ActionDefaultDto> result = await h.Sut.SetAsync(
            CommandsWrite,
            Raise(level, confirmed: 1),
            Admin
        );

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await h.Db.ActionDefinitions.AsNoTracking().SingleAsync())
            .PlatformDefaultLevel.Should()
            .BeNull();
    }

    [Fact]
    public async Task A_dangerous_action_needs_explicit_confirmation()
    {
        Harness h = await BuildAsync(DangerTier.Critical);
        (await h.Sut.PreviewAsync(CommandsWrite, 30))
            .Value.RequiresDangerConfirmation.Should()
            .BeTrue();

        Result<ActionDefaultDto> refused = await h.Sut.SetAsync(
            CommandsWrite,
            Raise(30, confirmed: 1, danger: false),
            Admin
        );
        refused.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await h.Db.ActionDefinitions.AsNoTracking().SingleAsync())
            .PlatformDefaultLevel.Should()
            .BeNull();

        Result<ActionDefaultDto> confirmed = await h.Sut.SetAsync(
            CommandsWrite,
            Raise(30, confirmed: 1, danger: true),
            Admin
        );
        confirmed.Value.EffectiveDefaultLevel.Should().Be(30);
    }

    [Fact]
    public async Task Clearing_the_platform_default_returns_followers_to_the_shipped_level()
    {
        Harness h = await BuildAsync();
        await h.Sut.SetAsync(CommandsWrite, Raise(30, confirmed: 1), Admin);

        Result<ActionDefaultDto> cleared = await h.Sut.SetAsync(
            CommandsWrite,
            new(null, 1, false),
            Admin
        );

        cleared.Value.PlatformDefaultLevel.Should().BeNull();
        cleared.Value.EffectiveDefaultLevel.Should().Be(10);
        (await h.Gate.AuthorizeActionAsync(Mod, Follower, CommandsWrite)).Value.Should().BeTrue();
    }

    [Fact]
    public async Task The_channel_matrix_reports_the_platform_default_as_the_channel_default()
    {
        Harness h = await BuildAsync();
        await h.Sut.SetAsync(CommandsWrite, Raise(20, confirmed: 1), Admin);

        ActionPermissionDto row = (await h.Gate.GetActionMatrixAsync(Follower)).Value.Single();

        row.DefaultLevel.Should().Be(20);
        row.EffectiveLevel.Should().Be(20);
        row.OverrideLevel.Should().BeNull();
    }
}
