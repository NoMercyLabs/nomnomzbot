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
/// Proves Plane-C platform IAM (roles-permissions §3.7): self-host (no principals) authorizes everything with
/// no audit; on SaaS a principal is allowed only if its role assignments grant the permission, every decision
/// is audited + evented; effective permissions are the scoped union over active assignments; management ops
/// are gated; revocation removes a permission; and a service-account is created with its key returned once.
/// </summary>
public sealed class PlatformIamServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

    private static (PlatformIamService Sut, AuthDbContext Db, RecordingEventBus Bus) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        RecordingEventBus bus = new();
        PlatformIamService sut = new(db, bus, new FakeTimeProvider(Now));
        return (sut, db, bus);
    }

    private static Guid SeedPrincipalWithPermission(
        AuthDbContext db,
        string permissionKey,
        Guid? scope = null
    )
    {
        Guid principalId = Guid.NewGuid();
        Guid roleId = Guid.NewGuid();
        Guid permissionId = Guid.NewGuid();
        db.IamPrincipals.Add(
            new IamPrincipal
            {
                Id = principalId,
                PrincipalType = IamPrincipalType.Employee,
                Name = "operator",
                IsActive = true,
            }
        );
        db.IamRoles.Add(new IamRole { Id = roleId, Name = $"role-{permissionId}" });
        db.IamPermissions.Add(
            new IamPermission
            {
                Id = permissionId,
                Key = permissionKey,
                Category = IamCategory.Iam,
            }
        );
        db.IamRolePermissions.Add(
            new IamRolePermission { RoleId = roleId, PermissionId = permissionId }
        );
        db.IamRoleAssignments.Add(
            new IamRoleAssignment
            {
                PrincipalId = principalId,
                RoleId = roleId,
                ScopeChannelId = scope,
                AssignedByPrincipalId = principalId,
            }
        );
        return principalId;
    }

    [Fact]
    public async Task Self_host_authorizes_everything_with_no_audit()
    {
        (PlatformIamService sut, AuthDbContext db, RecordingEventBus bus) = Build();

        Result<bool> result = await sut.AuthorizePlatformAsync(
            Guid.NewGuid(),
            "iam:manage",
            targetBroadcasterId: null,
            breakGlass: false,
            justification: null
        );

        result.Value.Should().BeTrue();
        (await db.IamAuditLogs.CountAsync()).Should().Be(0);
        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Saas_allows_a_held_permission_and_audits_it()
    {
        (PlatformIamService sut, AuthDbContext db, RecordingEventBus bus) = Build();
        Guid principalId = SeedPrincipalWithPermission(db, "iam:tenant:read");
        await db.SaveChangesAsync();

        Result<bool> result = await sut.AuthorizePlatformAsync(
            principalId,
            "iam:tenant:read",
            targetBroadcasterId: null,
            breakGlass: false,
            justification: "support ticket 42"
        );

        result.Value.Should().BeTrue();
        IamAuditLog audit = await db.IamAuditLogs.SingleAsync();
        audit.Outcome.Should().Be(IamOutcome.Allowed);
        audit.Permission.Should().Be("iam:tenant:read");
        bus.Published.OfType<IamAccessEvaluatedEvent>()
            .Single()
            .Outcome.Should()
            .Be(IamOutcome.Allowed);
    }

    [Fact]
    public async Task Saas_denies_an_unheld_permission_and_audits_it()
    {
        (PlatformIamService sut, AuthDbContext db, _) = Build();
        Guid principalId = SeedPrincipalWithPermission(db, "iam:tenant:read");
        await db.SaveChangesAsync();

        Result<bool> result = await sut.AuthorizePlatformAsync(
            principalId,
            "iam:billing:write",
            targetBroadcasterId: null,
            breakGlass: false,
            justification: null
        );

        result.Value.Should().BeFalse();
        (await db.IamAuditLogs.SingleAsync()).Outcome.Should().Be(IamOutcome.Denied);
    }

    [Fact]
    public async Task Effective_permissions_respect_channel_scope()
    {
        (PlatformIamService sut, AuthDbContext db, _) = Build();
        Guid channel = Guid.NewGuid();
        Guid principalId = SeedPrincipalWithPermission(db, "iam:tenant:read", scope: channel);
        await db.SaveChangesAsync();

        // In-scope channel sees the permission; a different channel does not.
        (await sut.GetEffectivePermissionsAsync(principalId, channel))
            .Value.Should()
            .Contain("iam:tenant:read");
        (await sut.GetEffectivePermissionsAsync(principalId, Guid.NewGuid()))
            .Value.Should()
            .BeEmpty();
    }

    [Fact]
    public async Task Assign_requires_manage_then_grants_the_role()
    {
        (PlatformIamService sut, AuthDbContext db, _) = Build();
        Guid manager = SeedPrincipalWithPermission(db, "iam:manage");
        Guid target = Guid.NewGuid();
        Guid roleId = Guid.NewGuid();
        db.IamPrincipals.Add(
            new IamPrincipal
            {
                Id = target,
                PrincipalType = IamPrincipalType.Employee,
                Name = "target",
                IsActive = true,
            }
        );
        db.IamRoles.Add(new IamRole { Id = roleId, Name = "support" });
        await db.SaveChangesAsync();

        Result<IamRoleAssignmentDto> ok = await sut.AssignRoleAsync(
            manager,
            target,
            roleId,
            scopeChannelId: null,
            expiresAt: null,
            reason: null
        );
        ok.IsSuccess.Should().BeTrue();
        ok.Value.RoleName.Should().Be("support");

        // A principal without iam:manage cannot assign.
        Result<IamRoleAssignmentDto> denied = await sut.AssignRoleAsync(
            target,
            target,
            roleId,
            null,
            null,
            null
        );
        denied.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task Revoke_removes_the_permission_from_the_effective_set()
    {
        (PlatformIamService sut, AuthDbContext db, _) = Build();
        Guid manager = SeedPrincipalWithPermission(db, "iam:manage");
        await db.SaveChangesAsync();
        IamRoleAssignment assignment = await db.IamRoleAssignments.FirstAsync(a =>
            a.PrincipalId == manager
        );

        await sut.RevokeAssignmentAsync(manager, assignment.Id, reason: "offboarded");

        (await sut.GetEffectivePermissionsAsync(manager, null)).Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_service_account_returns_the_key_once_and_stores_only_a_hash()
    {
        (PlatformIamService sut, AuthDbContext db, _) = Build();
        Guid creator = SeedPrincipalWithPermission(db, "iam:principal:create");
        await db.SaveChangesAsync();

        Result<IamPrincipalDto> result = await sut.CreatePrincipalAsync(
            creator,
            new CreatePrincipalRequest(
                IamPrincipalType.ServiceAccount,
                UserId: null,
                DisplayName: "ci-bot",
                RoleIds: [],
                ServiceAccountName: "ci-bot"
            )
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.ServiceAccountKey.Should().NotBeNullOrEmpty();
        IamPrincipal stored = await db.IamPrincipals.SingleAsync(p => p.Id == result.Value.Id);
        stored.ServiceAccountKeyHash.Should().NotBeNullOrEmpty();
        stored.ServiceAccountKeyHash.Should().NotBe(result.Value.ServiceAccountKey); // hash, not the key
    }

    [Fact]
    public async Task Create_employee_without_a_user_id_is_rejected()
    {
        (PlatformIamService sut, AuthDbContext db, _) = Build();
        Guid creator = SeedPrincipalWithPermission(db, "iam:principal:create");
        await db.SaveChangesAsync();

        Result<IamPrincipalDto> result = await sut.CreatePrincipalAsync(
            creator,
            new CreatePrincipalRequest(
                IamPrincipalType.Employee,
                UserId: null,
                DisplayName: "no-user",
                RoleIds: [],
                ServiceAccountName: null
            )
        );

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    // ─── §5.4 management surface (roles-permissions, decided 2026-07-17) ────

    [Fact]
    public async Task Create_employee_promotes_the_backing_user_to_platform_principal()
    {
        (PlatformIamService sut, AuthDbContext db, _) = Build();
        Guid creator = SeedPrincipalWithPermission(db, "iam:principal:create");
        User user = NewUser("promoted");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        Result<IamPrincipalDto> result = await sut.CreatePrincipalAsync(
            creator,
            new CreatePrincipalRequest(
                IamPrincipalType.Employee,
                UserId: user.Id,
                DisplayName: "Promoted Operator",
                RoleIds: [],
                ServiceAccountName: null
            )
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        // The promote wiring: without this marker the new principal could never ENTER Plane-C
        // (the authorization handler gates entry on the `admin` role claim it mints).
        (await db.Users.SingleAsync(u => u.Id == user.Id))
            .IsPlatformPrincipal.Should()
            .BeTrue();
    }

    [Fact]
    public async Task Create_employee_for_an_unknown_user_is_rejected()
    {
        (PlatformIamService sut, AuthDbContext db, _) = Build();
        Guid creator = SeedPrincipalWithPermission(db, "iam:principal:create");
        await db.SaveChangesAsync();

        Result<IamPrincipalDto> result = await sut.CreatePrincipalAsync(
            creator,
            new CreatePrincipalRequest(
                IamPrincipalType.Employee,
                UserId: Guid.NewGuid(),
                DisplayName: "ghost",
                RoleIds: [],
                ServiceAccountName: null
            )
        );

        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Deactivate_clears_the_user_marker_and_reactivate_restores_it()
    {
        (PlatformIamService sut, AuthDbContext db, _) = Build();
        Guid manager = SeedPrincipalWithPermission(db, "iam:manage");
        User user = NewUser("demotee");
        user.IsPlatformPrincipal = true;
        db.Users.Add(user);
        Guid targetId = Guid.NewGuid();
        db.IamPrincipals.Add(
            new IamPrincipal
            {
                Id = targetId,
                PrincipalType = IamPrincipalType.Employee,
                UserId = user.Id,
                Name = "demotee",
                IsActive = true,
            }
        );
        await db.SaveChangesAsync();

        // Deactivate = the demote: both the principal AND the user's Plane-C entry marker flip.
        (await sut.DeactivatePrincipalAsync(manager, targetId, "offboarded"))
            .IsSuccess.Should()
            .BeTrue();
        (await db.IamPrincipals.SingleAsync(p => p.Id == targetId)).IsActive.Should().BeFalse();
        (await db.Users.SingleAsync(u => u.Id == user.Id)).IsPlatformPrincipal.Should().BeFalse();

        // Reactivate restores both.
        (await sut.ReactivatePrincipalAsync(manager, targetId))
            .IsSuccess.Should()
            .BeTrue();
        (await db.IamPrincipals.SingleAsync(p => p.Id == targetId)).IsActive.Should().BeTrue();
        (await db.Users.SingleAsync(u => u.Id == user.Id)).IsPlatformPrincipal.Should().BeTrue();
    }

    [Fact]
    public async Task A_principal_cannot_deactivate_itself()
    {
        (PlatformIamService sut, AuthDbContext db, _) = Build();
        Guid manager = SeedPrincipalWithPermission(db, "iam:manage");
        await db.SaveChangesAsync();

        Result result = await sut.DeactivatePrincipalAsync(manager, manager, reason: null);

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await db.IamPrincipals.SingleAsync(p => p.Id == manager)).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task List_roles_returns_each_role_with_its_permission_bundle()
    {
        (PlatformIamService sut, AuthDbContext db, _) = Build();
        SeedPrincipalWithPermission(db, "iam:manage"); // seeds one role bound to iam:manage
        db.IamRoles.Add(
            new IamRole
            {
                Id = Guid.NewGuid(),
                Name = "empty-role",
                IsSystem = true,
                Description = "no permissions",
            }
        );
        await db.SaveChangesAsync();

        Result<IReadOnlyList<IamRoleDto>> result = await sut.ListRolesAsync();

        result.Value.Should().HaveCount(2);
        IamRoleDto bound = result.Value.Single(r => r.Name != "empty-role");
        bound.PermissionKeys.Should().Equal("iam:manage");
        IamRoleDto empty = result.Value.Single(r => r.Name == "empty-role");
        empty.PermissionKeys.Should().BeEmpty();
        empty.IsSystem.Should().BeTrue();
    }

    [Fact]
    public async Task List_principals_carries_only_active_assignments()
    {
        (PlatformIamService sut, AuthDbContext db, _) = Build();
        Guid principalId = SeedPrincipalWithPermission(db, "iam:manage");
        Guid roleId = Guid.NewGuid();
        db.IamRoles.Add(new IamRole { Id = roleId, Name = "stale-role" });
        // A revoked and an expired assignment — both must be filtered out of the summary.
        db.IamRoleAssignments.Add(
            new IamRoleAssignment
            {
                PrincipalId = principalId,
                RoleId = roleId,
                AssignedByPrincipalId = principalId,
                RevokedAt = Now.UtcDateTime.AddDays(-1),
            }
        );
        db.IamRoleAssignments.Add(
            new IamRoleAssignment
            {
                PrincipalId = principalId,
                RoleId = roleId,
                AssignedByPrincipalId = principalId,
                ExpiresAt = Now.UtcDateTime.AddDays(-2),
            }
        );
        await db.SaveChangesAsync();

        Result<IReadOnlyList<IamPrincipalSummaryDto>> result = await sut.ListPrincipalsAsync();

        IamPrincipalSummaryDto summary = result.Value.Single();
        summary.Id.Should().Be(principalId);
        summary.ActiveAssignments.Should().ContainSingle(); // only the live one from the seed
        summary.ActiveAssignments[0].RoleName.Should().NotBe("stale-role");
    }

    private static User NewUser(string name) =>
        new()
        {
            Username = name,
            UsernameNormalized = name,
            DisplayName = name,
        };
}
