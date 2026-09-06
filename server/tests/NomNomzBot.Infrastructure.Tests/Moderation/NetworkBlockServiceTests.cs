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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// The network-wide block desk (S-ADMIN-8b) against a real database: the blast-radius preview is real
/// counts, applying fails closed on a stale preview, the block is enforced network-wide (read as a global
/// flag, not a per-tenant row), lifting reverses the real Twitch bans and stays honest on a partial
/// outcome, and the capability requires both the permission and a justification, held only by a NAMED
/// operator rather than granted wholesale by a broad role.
/// </summary>
public sealed class NetworkBlockServiceTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("0199e000-0000-7000-8000-0000000000a1");
    private static readonly Guid TenantB = Guid.Parse("0199e000-0000-7000-8000-0000000000b1");
    private static readonly Guid TenantC = Guid.Parse("0199e000-0000-7000-8000-0000000000c1");
    private static readonly Guid OperatorId = Guid.Parse("0199e000-0000-7000-8000-000000000d01");
    private static readonly Guid OtherPrincipalId = Guid.Parse(
        "0199e000-0000-7000-8000-000000000d02"
    );
    private static readonly Guid TargetUserId = Guid.Parse("0199e000-0000-7000-8000-000000000e01");
    private const string TargetTwitchId = "troll-99";
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    private const string Why = "Ticket #9001 — coordinated cross-channel raid harassment.";

    private readonly SqliteConnection _connection;
    private readonly FakeTimeProvider _time = new(Now);

    public NetworkBlockServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using AppDbContext db = NewDbContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");

        // Tenant C is owned outright by the target actor; A and B are owned by unrelated streamers and the
        // actor is merely present there via community standing (added below).
        foreach (
            (Guid id, string name, Guid ownerUserId) in new[]
            {
                (TenantA, "channel-a", Guid.NewGuid()),
                (TenantB, "channel-b", Guid.NewGuid()),
                (TenantC, "channel-c", TargetUserId),
            }
        )
        {
            db.Channels.Add(
                new Channel
                {
                    Id = id,
                    OwnerUserId = ownerUserId,
                    Provider = AuthEnums.Platform.Twitch,
                    ExternalChannelId = $"ext-{name}",
                    Name = name,
                    NameNormalized = name,
                }
            );
        }

        db.Users.Add(
            new User
            {
                Id = TargetUserId,
                TwitchUserId = TargetTwitchId,
                Username = "Troll99",
                UsernameNormalized = "troll99",
                DisplayName = "Troll99",
            }
        );

        // A real, everyone-floor Gate-2 action — the enforcement tests prove the network block denies it
        // even though the action itself requires no special standing.
        db.ActionDefinitions.Add(
            new ActionDefinition
            {
                ActionKey = "chat:send-command",
                Plane = AuthPlane.Community,
                DefaultLevel = 0,
                FloorLevel = 0,
                FloorTier = DangerTier.Low,
                IsGrantableViaPermit = false,
            }
        );

        // The actor is present (via community standing) in Tenant A and Tenant B, and owns Tenant C's
        // channel outright (seeded above) — three tenants total, the exact blast radius the preview
        // must name.
        db.ChannelCommunityStandings.AddRange(
            new ChannelCommunityStanding
            {
                BroadcasterId = TenantA,
                UserId = TargetUserId,
                Standing = CommunityStanding.Everyone,
            },
            new ChannelCommunityStanding
            {
                BroadcasterId = TenantB,
                UserId = TargetUserId,
                Standing = CommunityStanding.Everyone,
            }
        );
        db.SaveChanges();
    }

    private AppDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    private static (NetworkBlockService Sut, ITwitchModerationApi Twitch) NewService(
        AppDbContext db,
        DeploymentMode mode = DeploymentMode.Saas
    )
    {
        ITwitchModerationApi twitch = Substitute.For<ITwitchModerationApi>();
        twitch
            .BanUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchBanResult("b", "b", TargetTwitchId, DateTimeOffset.UnixEpoch, null)
                )
            );
        twitch
            .UnbanUserAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        NetworkBlockService sut = new(
            db,
            new PlatformIamService(db, new RecordingEventBus(), TimeProvider.System, new(mode)),
            twitch,
            new FakeTimeProvider(Now),
            NullLogger<NetworkBlockService>.Instance
        );
        return (sut, twitch);
    }

    /// <summary>Seeds an operator IAM principal holding exactly <paramref name="permissionKeys"/>.</summary>
    private static void SeedOperator(
        AppDbContext db,
        Guid principalId,
        params string[] permissionKeys
    )
    {
        db.IamPrincipals.Add(
            new()
            {
                Id = principalId,
                PrincipalType = IamPrincipalType.Employee,
                Name = $"operator-{principalId}",
                IsActive = true,
            }
        );
        if (permissionKeys.Length == 0)
        {
            db.SaveChanges();
            return;
        }

        Guid roleId = Guid.NewGuid();
        db.IamRoles.Add(new() { Id = roleId, Name = $"role-{roleId}" });
        foreach (string key in permissionKeys)
        {
            Guid permissionId = Guid.NewGuid();
            db.IamPermissions.Add(
                new()
                {
                    Id = permissionId,
                    Key = key,
                    Category = IamCategory.Tenant,
                }
            );
            db.IamRolePermissions.Add(new() { RoleId = roleId, PermissionId = permissionId });
        }
        db.IamRoleAssignments.Add(
            new()
            {
                PrincipalId = principalId,
                RoleId = roleId,
                AssignedByPrincipalId = principalId,
            }
        );
        db.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    // ---- The blast-radius preview is real, and apply fails closed on a stale one ---------------------

    [Fact]
    public async Task Preview_NamesTheRealTenantsAndCount_ForAnActorPresentInThree()
    {
        using AppDbContext seed = NewDbContext();
        SeedOperator(seed, OperatorId, IamPermissionKeys.NetworkBlockManage);

        using AppDbContext db = NewDbContext();
        (NetworkBlockService sut, _) = NewService(db);

        Result<NetworkBlockPreviewDto> preview = await sut.PreviewAsync(
            OperatorId,
            TargetTwitchId,
            Why
        );

        preview.IsSuccess.Should().BeTrue(preview.ErrorMessage);
        preview.Value.TenantCount.Should().Be(3);
        preview
            .Value.Tenants.Select(t => t.BroadcasterId)
            .Should()
            .BeEquivalentTo([TenantA, TenantB, TenantC]);
        preview.Value.TargetUserId.Should().Be(TargetUserId);
    }

    [Fact]
    public async Task Apply_WithTheFreshCount_BansEveryTenantAndPersistsTheBlock()
    {
        using AppDbContext seed = NewDbContext();
        SeedOperator(seed, OperatorId, IamPermissionKeys.NetworkBlockManage);

        using AppDbContext db = NewDbContext();
        (NetworkBlockService sut, ITwitchModerationApi twitch) = NewService(db);

        Result<NetworkBlockDto> result = await sut.ApplyAsync(
            OperatorId,
            new ApplyNetworkBlockRequest(TargetTwitchId, "cross-channel raid", Why, 3)
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.TenantCount.Should().Be(3);
        result.Value.ChannelCount.Should().Be(3);
        result.Value.Status.Should().Be(NetworkBlockStatus.Active);
        result.Value.AppliedByPrincipalId.Should().Be(OperatorId);

        await twitch
            .Received(1)
            .BanUserAsync(
                TenantA,
                TargetTwitchId,
                "cross-channel raid",
                Arg.Any<CancellationToken>()
            );
        await twitch
            .Received(1)
            .BanUserAsync(
                TenantB,
                TargetTwitchId,
                "cross-channel raid",
                Arg.Any<CancellationToken>()
            );
        await twitch
            .Received(1)
            .BanUserAsync(
                TenantC,
                TargetTwitchId,
                "cross-channel raid",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Apply_WithAStaleConfirmedCount_FailsClosed_AndAppliesNothing()
    {
        using AppDbContext seed = NewDbContext();
        SeedOperator(seed, OperatorId, IamPermissionKeys.NetworkBlockManage);

        using AppDbContext db = NewDbContext();
        (NetworkBlockService sut, ITwitchModerationApi twitch) = NewService(db);

        // The operator saw 3 tenants in an earlier preview; the real count is still 3, but they confirm
        // against a number that no longer matches (2) — the number they never actually saw.
        Result<NetworkBlockDto> result = await sut.ApplyAsync(
            OperatorId,
            new ApplyNetworkBlockRequest(TargetTwitchId, "cross-channel raid", Why, 2)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("PREVIEW_STALE");
        await twitch
            .DidNotReceive()
            .BanUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
        db.NetworkBlocks.Should().BeEmpty("a stale count must act on nothing, not partially apply");
    }

    // ---- Enforcement reads the network flag, not a per-tenant row --------------------------------------

    [Fact]
    public async Task AfterApply_TheActorIsRefused_InATenantTheyWereNeverIndividuallyBlockedIn()
    {
        Guid untouchedTenant = Guid.Parse("0199e000-0000-7000-8000-0000000000f1");

        using AppDbContext seed = NewDbContext();
        SeedOperator(seed, OperatorId, IamPermissionKeys.NetworkBlockManage);
        seed.Channels.Add(
            new Channel
            {
                Id = untouchedTenant,
                OwnerUserId = Guid.NewGuid(),
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "ext-untouched",
                Name = "untouched",
                NameNormalized = "untouched",
            }
        );
        seed.SaveChanges();

        using (AppDbContext apply = NewDbContext())
        {
            (NetworkBlockService applySut, _) = NewService(apply);
            Result<NetworkBlockDto> applied = await applySut.ApplyAsync(
                OperatorId,
                new ApplyNetworkBlockRequest(TargetTwitchId, "raid", Why, 3)
            );
            applied.IsSuccess.Should().BeTrue(applied.ErrorMessage);
        }

        // The enforcement gate: HasCapabilityAsync must deny the actor EVERYWHERE, including a tenant
        // that never had a per-tenant row for them at all — proving it reads the network block, not a
        // per-tenant permit/ban table.
        using AppDbContext check = NewDbContext();
        RoleResolver roles = new(check, TimeProvider.System);
        Result<bool> allowed = await roles.HasCapabilityAsync(
            TargetUserId,
            untouchedTenant,
            "chat:send-command"
        );

        allowed.IsSuccess.Should().BeTrue(allowed.ErrorMessage);
        allowed
            .Value.Should()
            .BeFalse(
                "a network-blocked actor must be refused in every tenant, not only the ones they were seen in"
            );
    }

    [Fact]
    public async Task WithNoNetworkBlock_AnUnrelatedUserIsNotAffected()
    {
        using AppDbContext seed = NewDbContext();
        seed.SaveChanges();

        using AppDbContext check = NewDbContext();
        RoleResolver roles = new(check, TimeProvider.System);
        Result<int> level = await roles.ResolveEffectiveLevelAsync(TargetUserId, TenantA);

        level.IsSuccess.Should().BeTrue();
    }

    // ---- Lifting reverses the real effect, and stays honest on a partial outcome -----------------------

    [Fact]
    public async Task Lift_UnbansEveryTenantTheApplyTouched_AndFullyLiftsWhenAllSucceed()
    {
        Guid blockId;
        using (AppDbContext seed = NewDbContext())
        {
            SeedOperator(seed, OperatorId, IamPermissionKeys.NetworkBlockManage);
            (NetworkBlockService applySut, _) = NewService(seed);
            Result<NetworkBlockDto> applied = await applySut.ApplyAsync(
                OperatorId,
                new ApplyNetworkBlockRequest(TargetTwitchId, "raid", Why, 3)
            );
            applied.IsSuccess.Should().BeTrue(applied.ErrorMessage);
            blockId = applied.Value.Id;
        }

        using AppDbContext db = NewDbContext();
        (NetworkBlockService sut, ITwitchModerationApi twitch) = NewService(db);

        Result<NetworkBlockDto> lifted = await sut.LiftAsync(
            OperatorId,
            blockId,
            "false positive — cleared by review"
        );

        lifted.IsSuccess.Should().BeTrue(lifted.ErrorMessage);
        lifted.Value.Status.Should().Be(NetworkBlockStatus.Lifted);
        lifted.Value.LiftedAt.Should().NotBeNull();
        lifted.Value.LiftedByPrincipalId.Should().Be(OperatorId);
        lifted.Value.LiftJustification.Should().Be("false positive — cleared by review");
        lifted.Value.RestoredChannelCount.Should().Be(3);
        lifted.Value.LiftFailedChannelIds.Should().BeEmpty();

        await twitch
            .Received(1)
            .UnbanUserAsync(TenantA, TargetTwitchId, Arg.Any<CancellationToken>());
        await twitch
            .Received(1)
            .UnbanUserAsync(TenantB, TargetTwitchId, Arg.Any<CancellationToken>());
        await twitch
            .Received(1)
            .UnbanUserAsync(TenantC, TargetTwitchId, Arg.Any<CancellationToken>());

        // The enforcement gate must actually clear once fully lifted.
        RoleResolver roles = new(db, TimeProvider.System);
        Result<bool> allowed = await roles.HasCapabilityAsync(
            TargetUserId,
            TenantA,
            "chat:send-command"
        );
        allowed.Value.Should().BeTrue("a fully lifted block must stop being enforced");
    }

    [Fact]
    public async Task Lift_WithAFailingLeg_RecordsTheHonestPartialOutcome_AndNeverClaimsACleanLift()
    {
        Guid blockId;
        using (AppDbContext seed = NewDbContext())
        {
            SeedOperator(seed, OperatorId, IamPermissionKeys.NetworkBlockManage);
            (NetworkBlockService applySut, _) = NewService(seed);
            Result<NetworkBlockDto> applied = await applySut.ApplyAsync(
                OperatorId,
                new ApplyNetworkBlockRequest(TargetTwitchId, "raid", Why, 3)
            );
            applied.IsSuccess.Should().BeTrue(applied.ErrorMessage);
            blockId = applied.Value.Id;
        }

        using AppDbContext db = NewDbContext();
        (NetworkBlockService sut, ITwitchModerationApi twitch) = NewService(db);
        // Tenant B's Twitch unban call fails (e.g. token revoked) — the other two still restore.
        twitch
            .UnbanUserAsync(TenantB, TargetTwitchId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure("token revoked", "TWITCH_ERROR"));

        Result<NetworkBlockDto> lifted = await sut.LiftAsync(
            OperatorId,
            blockId,
            "attempting to clear"
        );

        lifted.IsSuccess.Should().BeTrue(lifted.ErrorMessage);
        lifted
            .Value.Status.Should()
            .NotBe(NetworkBlockStatus.Lifted, "a partial outcome must never claim a clean lift");
        lifted.Value.LiftedAt.Should().BeNull();
        lifted
            .Value.LiftedByPrincipalId.Should()
            .Be(OperatorId, "who attempted the lift is recorded even on a partial outcome");
        lifted.Value.LiftJustification.Should().Be("attempting to clear");
        lifted.Value.RestoredChannelCount.Should().Be(2);
        lifted
            .Value.LiftFailedChannelIds.Should()
            .ContainSingle()
            .Which.Should()
            .Be(TenantB.ToString());

        // Still enforced network-wide — the honest partial state, not a silent clean lift.
        RoleResolver roles = new(db, TimeProvider.System);
        Result<bool> allowed = await roles.HasCapabilityAsync(
            TargetUserId,
            TenantA,
            "chat:send-command"
        );
        allowed
            .Value.Should()
            .BeFalse("a partially-lifted block stays enforced rather than silently clearing");
    }

    // ---- Authorization: a genuine failure, and a dangerous capability held by a NAMED operator ----------

    [Fact]
    public async Task WithoutThePermission_PreviewIsRefused_AsAGenuineAuthorizationFailure()
    {
        using AppDbContext seed = NewDbContext();
        SeedOperator(seed, OtherPrincipalId); // no permissions at all
        seed.SaveChanges();

        using AppDbContext db = NewDbContext();
        (NetworkBlockService sut, _) = NewService(db);

        Result<NetworkBlockPreviewDto> preview = await sut.PreviewAsync(
            OtherPrincipalId,
            TargetTwitchId,
            Why
        );

        preview
            .IsFailure.Should()
            .BeTrue(
                "a denial must be a genuine authorization failure, never an empty success result"
            );
        preview.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task WithoutAJustification_ApplyIsRefused()
    {
        using AppDbContext seed = NewDbContext();
        SeedOperator(seed, OperatorId, IamPermissionKeys.NetworkBlockManage);

        using AppDbContext db = NewDbContext();
        (NetworkBlockService sut, _) = NewService(db);

        Result<NetworkBlockDto> result = await sut.ApplyAsync(
            OperatorId,
            new ApplyNetworkBlockRequest(TargetTwitchId, "raid", "", 3)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task TheCapability_IsHeldByOneNamedOperator_NotByEveryPrincipalOfABroaderRole()
    {
        using AppDbContext seed = NewDbContext();
        // OperatorId is individually assigned network:block:manage. OtherPrincipalId holds a totally
        // different, broad platform role (trust-safety review) that does NOT carry it — proving the
        // dangerous capability is a per-principal grant, not something a broad role hands out wholesale.
        SeedOperator(seed, OperatorId, IamPermissionKeys.NetworkBlockManage);
        SeedOperator(seed, OtherPrincipalId, IamPermissionKeys.TrustSafetyReview);

        using AppDbContext db = NewDbContext();
        (NetworkBlockService sut, _) = NewService(db);

        Result<NetworkBlockPreviewDto> named = await sut.PreviewAsync(
            OperatorId,
            TargetTwitchId,
            Why
        );
        Result<NetworkBlockPreviewDto> other = await sut.PreviewAsync(
            OtherPrincipalId,
            TargetTwitchId,
            Why
        );

        named.IsSuccess.Should().BeTrue(named.ErrorMessage);
        other
            .IsFailure.Should()
            .BeTrue("holding a different, broad platform role must not carry this capability");
        other.ErrorCode.Should().Be("FORBIDDEN");
    }
}
