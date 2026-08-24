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
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves Plane-C platform IAM (roles-permissions §3.7): self-host authorizes everything with no audit
/// REGARDLESS of how many <c>IamPrincipal</c> rows exist (fix D2 — the deployment mode decides, never a row
/// count); on SaaS a principal is allowed only if its role assignments grant the permission, every decision
/// is audited + evented; effective permissions are the scoped union over active assignments; management ops
/// are gated; revocation removes a permission; and a service-account is created with its key returned once.
/// </summary>
public sealed class PlatformIamServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

    private static (PlatformIamService Sut, AuthDbContext Db, RecordingEventBus Bus) Build(
        DeploymentMode mode = DeploymentMode.SelfHostLite
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        RecordingEventBus bus = new();
        PlatformIamService sut = new(db, bus, new FakeTimeProvider(Now), new(mode));
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
            new()
            {
                Id = principalId,
                PrincipalType = IamPrincipalType.Employee,
                Name = "operator",
                IsActive = true,
            }
        );
        db.IamRoles.Add(new() { Id = roleId, Name = $"role-{permissionId}" });
        db.IamPermissions.Add(
            new()
            {
                Id = permissionId,
                Key = permissionKey,
                Category = IamCategory.Iam,
            }
        );
        db.IamRolePermissions.Add(new() { RoleId = roleId, PermissionId = permissionId });
        db.IamRoleAssignments.Add(
            new()
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

    /// <summary>
    /// Fix D2 item 1: the regression this whole slice exists to close. Before the fix, "is this SaaS" was
    /// decided by "does any IamPrincipal row exist" — so creating a single service-account principal (e.g.
    /// the platform onboarding a support tool) flipped a self-host deployment into default-deny and locked
    /// the owner out of every Plane-C route, even though the deployment mode never changed. The fact must be
    /// the deployment mode, not a row count.
    /// </summary>
    [Fact]
    public async Task Self_host_still_authorizes_the_owner_after_a_service_account_principal_exists()
    {
        (PlatformIamService sut, AuthDbContext db, _) = Build(DeploymentMode.SelfHostLite);
        // Creating ONE principal (e.g. a service account onboarded for automation) must not change the
        // deployment-mode fact the whole plane keys on.
        db.IamPrincipals.Add(
            new()
            {
                PrincipalType = IamPrincipalType.ServiceAccount,
                Name = "ci-bot",
                IsActive = true,
            }
        );
        await db.SaveChangesAsync();

        Result<bool> ownerCheck = await sut.AuthorizePlatformAsync(
            Guid.NewGuid(), // the owner has no principal row of their own in this scenario
            "iam:manage",
            targetBroadcasterId: null,
            breakGlass: false,
            justification: null
        );

        ownerCheck
            .Value.Should()
            .BeTrue("self-host stays implicitly-full regardless of principal rows");
        (await db.IamAuditLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Saas_allows_a_held_permission_and_audits_it()
    {
        (PlatformIamService sut, AuthDbContext db, RecordingEventBus bus) = Build(
            DeploymentMode.Saas
        );
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
        (PlatformIamService sut, AuthDbContext db, _) = Build(DeploymentMode.Saas);
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
        (PlatformIamService sut, AuthDbContext db, _) = Build(DeploymentMode.Saas);
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
        (PlatformIamService sut, AuthDbContext db, _) = Build(DeploymentMode.Saas);
        Guid manager = SeedPrincipalWithPermission(db, "iam:manage");
        Guid target = Guid.NewGuid();
        Guid roleId = Guid.NewGuid();
        db.IamPrincipals.Add(
            new()
            {
                Id = target,
                PrincipalType = IamPrincipalType.Employee,
                Name = "target",
                IsActive = true,
            }
        );
        db.IamRoles.Add(new() { Id = roleId, Name = "support" });
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
        (PlatformIamService sut, AuthDbContext db, _) = Build(DeploymentMode.Saas);
        // iam:manage is what lets `manager` call revoke at all; the assignment actually being revoked
        // grants an UNRELATED permission, so this proves the general revoke mechanics without tripping
        // the last-iam:manage-holder lockout guard (proven separately below).
        Guid manager = SeedPrincipalWithPermission(db, "iam:manage");
        SeedPrincipalWithPermission(db, "iam:tenant:read"); // keeps helper's role/perm ids distinct
        Guid revocableRoleId = Guid.NewGuid();
        Guid revocablePermissionId = Guid.NewGuid();
        db.IamRoles.Add(new() { Id = revocableRoleId, Name = "revocable-role" });
        db.IamPermissions.Add(
            new()
            {
                Id = revocablePermissionId,
                Key = "iam:tenant:write",
                Category = IamCategory.Iam,
            }
        );
        db.IamRolePermissions.Add(
            new() { RoleId = revocableRoleId, PermissionId = revocablePermissionId }
        );
        db.IamRoleAssignments.Add(
            new()
            {
                PrincipalId = manager,
                RoleId = revocableRoleId,
                AssignedByPrincipalId = manager,
            }
        );
        await db.SaveChangesAsync();
        IamRoleAssignment assignment = await db.IamRoleAssignments.FirstAsync(a =>
            a.PrincipalId == manager && a.RoleId == revocableRoleId
        );

        await sut.RevokeAssignmentAsync(manager, assignment.Id, reason: "offboarded");

        (await sut.GetEffectivePermissionsAsync(manager, null))
            .Value.Should()
            .NotContain("iam:tenant:write");
    }

    [Fact]
    public async Task Create_service_account_returns_the_key_once_and_stores_only_a_hash()
    {
        (PlatformIamService sut, AuthDbContext db, _) = Build(DeploymentMode.Saas);
        Guid creator = SeedPrincipalWithPermission(db, "iam:principal:create");
        await db.SaveChangesAsync();

        Result<IamPrincipalDto> result = await sut.CreatePrincipalAsync(
            creator,
            new(
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
        (PlatformIamService sut, AuthDbContext db, _) = Build(DeploymentMode.Saas);
        Guid creator = SeedPrincipalWithPermission(db, "iam:principal:create");
        await db.SaveChangesAsync();

        Result<IamPrincipalDto> result = await sut.CreatePrincipalAsync(
            creator,
            new(
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
        (PlatformIamService sut, AuthDbContext db, _) = Build(DeploymentMode.Saas);
        Guid creator = SeedPrincipalWithPermission(db, "iam:principal:create");
        User user = NewUser("promoted");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        Result<IamPrincipalDto> result = await sut.CreatePrincipalAsync(
            creator,
            new(
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
        (PlatformIamService sut, AuthDbContext db, _) = Build(DeploymentMode.Saas);
        Guid creator = SeedPrincipalWithPermission(db, "iam:principal:create");
        await db.SaveChangesAsync();

        Result<IamPrincipalDto> result = await sut.CreatePrincipalAsync(
            creator,
            new(
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
        (PlatformIamService sut, AuthDbContext db, _) = Build(DeploymentMode.Saas);
        Guid manager = SeedPrincipalWithPermission(db, "iam:manage");
        User user = NewUser("demotee");
        user.IsPlatformPrincipal = true;
        db.Users.Add(user);
        Guid targetId = Guid.NewGuid();
        db.IamPrincipals.Add(
            new()
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
        (PlatformIamService sut, AuthDbContext db, _) = Build(DeploymentMode.Saas);
        Guid manager = SeedPrincipalWithPermission(db, "iam:manage");
        await db.SaveChangesAsync();

        Result result = await sut.DeactivatePrincipalAsync(manager, manager, reason: null);

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await db.IamPrincipals.SingleAsync(p => p.Id == manager)).IsActive.Should().BeTrue();
    }

    // ─── S086c: IAM mutations are audited and guarded ────

    [Fact]
    public async Task Assign_writes_one_audit_row_naming_actor_target_and_role()
    {
        (PlatformIamService sut, AuthDbContext db, _) = Build(DeploymentMode.Saas);
        Guid manager = SeedPrincipalWithPermission(db, "iam:manage");
        Guid target = Guid.NewGuid();
        Guid roleId = Guid.NewGuid();
        db.IamPrincipals.Add(
            new()
            {
                Id = target,
                PrincipalType = IamPrincipalType.Employee,
                Name = "target",
                IsActive = true,
            }
        );
        db.IamRoles.Add(new() { Id = roleId, Name = "support" });
        await db.SaveChangesAsync();

        Result<IamRoleAssignmentDto> result = await sut.AssignRoleAsync(
            manager,
            target,
            roleId,
            scopeChannelId: null,
            expiresAt: null,
            reason: "onboarding"
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        IamAuditLog audit = await db.IamAuditLogs.SingleAsync();
        audit.PrincipalId.Should().Be(manager);
        audit.TargetPrincipalId.Should().Be(target);
        audit.RoleId.Should().Be(roleId);
        audit.Permission.Should().Be("iam:manage");
        audit.Justification.Should().Be("onboarding");
        audit.Outcome.Should().Be(IamOutcome.Allowed);
    }

    [Fact]
    public async Task Revoke_writes_one_audit_row_naming_actor_target_and_role()
    {
        (PlatformIamService sut, AuthDbContext db, _) = Build(DeploymentMode.Saas);
        // Two managers so revoking one assignment never trips the last-holder guard here.
        Guid manager = SeedPrincipalWithPermission(db, "iam:manage");
        Guid otherManager = SeedPrincipalWithPermission(db, "iam:manage");
        await db.SaveChangesAsync();
        IamRoleAssignment assignment = await db.IamRoleAssignments.FirstAsync(a =>
            a.PrincipalId == manager
        );

        Result result = await sut.RevokeAssignmentAsync(otherManager, assignment.Id, "offboarded");

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        IamAuditLog audit = await db.IamAuditLogs.SingleAsync();
        audit.PrincipalId.Should().Be(otherManager);
        audit.TargetPrincipalId.Should().Be(manager);
        audit.RoleId.Should().Be(assignment.RoleId);
        audit.Justification.Should().Be("offboarded");
        audit.Outcome.Should().Be(IamOutcome.Allowed);
    }

    [Fact]
    public async Task Create_writes_one_audit_row_naming_actor_and_the_new_principal()
    {
        (PlatformIamService sut, AuthDbContext db, _) = Build(DeploymentMode.Saas);
        Guid creator = SeedPrincipalWithPermission(db, "iam:principal:create");
        await db.SaveChangesAsync();

        Result<IamPrincipalDto> result = await sut.CreatePrincipalAsync(
            creator,
            new(
                IamPrincipalType.ServiceAccount,
                UserId: null,
                DisplayName: "ci-bot",
                RoleIds: [],
                ServiceAccountName: "ci-bot"
            )
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        IamAuditLog audit = await db.IamAuditLogs.SingleAsync();
        audit.PrincipalId.Should().Be(creator);
        audit.TargetPrincipalId.Should().Be(result.Value.Id);
        audit.Permission.Should().Be("iam:principal:create");
        audit.Outcome.Should().Be(IamOutcome.Allowed);
    }

    [Fact]
    public async Task Deactivate_and_reactivate_each_write_one_audit_row_naming_actor_and_target()
    {
        (PlatformIamService sut, AuthDbContext db, _) = Build(DeploymentMode.Saas);
        Guid manager = SeedPrincipalWithPermission(db, "iam:manage");
        Guid otherManager = SeedPrincipalWithPermission(db, "iam:manage");
        await db.SaveChangesAsync();

        (await sut.DeactivatePrincipalAsync(manager, otherManager, "offboarded"))
            .IsSuccess.Should()
            .BeTrue();
        IamAuditLog deactivateAudit = await db.IamAuditLogs.SingleAsync();
        deactivateAudit.PrincipalId.Should().Be(manager);
        deactivateAudit.TargetPrincipalId.Should().Be(otherManager);
        deactivateAudit.Justification.Should().Be("offboarded");

        (await sut.ReactivatePrincipalAsync(manager, otherManager)).IsSuccess.Should().BeTrue();
        (await db.IamAuditLogs.CountAsync()).Should().Be(2);
        IamAuditLog reactivateAudit = await db
            .IamAuditLogs.OrderByDescending(a => a.Id)
            .FirstAsync();
        reactivateAudit.PrincipalId.Should().Be(manager);
        reactivateAudit.TargetPrincipalId.Should().Be(otherManager);
    }

    [Fact]
    public async Task Create_employee_for_an_unknown_user_leaves_no_principal_row_behind()
    {
        (PlatformIamService sut, AuthDbContext db, _) = Build(DeploymentMode.Saas);
        Guid creator = SeedPrincipalWithPermission(db, "iam:principal:create");
        await db.SaveChangesAsync();
        int principalsBefore = await db.IamPrincipals.CountAsync();

        Result<IamPrincipalDto> result = await sut.CreatePrincipalAsync(
            creator,
            new(
                IamPrincipalType.Employee,
                UserId: Guid.NewGuid(),
                DisplayName: "ghost",
                RoleIds: [],
                ServiceAccountName: null
            )
        );
        // Nothing further should be tracked from the failed attempt — a later, unrelated save on the
        // same context must not flush an orphaned principal (the bug this slice closes).
        await db.SaveChangesAsync();

        result.ErrorCode.Should().Be("NOT_FOUND");
        (await db.IamPrincipals.CountAsync()).Should().Be(principalsBefore);
    }

    [Fact]
    public async Task A_duplicate_active_assignment_is_refused()
    {
        (PlatformIamService sut, AuthDbContext db, _) = Build(DeploymentMode.Saas);
        Guid manager = SeedPrincipalWithPermission(db, "iam:manage");
        Guid target = Guid.NewGuid();
        Guid roleId = Guid.NewGuid();
        db.IamPrincipals.Add(
            new()
            {
                Id = target,
                PrincipalType = IamPrincipalType.Employee,
                Name = "target",
                IsActive = true,
            }
        );
        db.IamRoles.Add(new() { Id = roleId, Name = "support" });
        await db.SaveChangesAsync();
        (await sut.AssignRoleAsync(manager, target, roleId, null, null, null))
            .IsSuccess.Should()
            .BeTrue();

        Result<IamRoleAssignmentDto> duplicate = await sut.AssignRoleAsync(
            manager,
            target,
            roleId,
            null,
            null,
            null
        );

        duplicate.ErrorCode.Should().Be("DUPLICATE_ASSIGNMENT");
        (await db.IamRoleAssignments.CountAsync(a => a.PrincipalId == target && a.RoleId == roleId))
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task An_assignment_to_an_inactive_target_is_refused()
    {
        (PlatformIamService sut, AuthDbContext db, _) = Build(DeploymentMode.Saas);
        Guid manager = SeedPrincipalWithPermission(db, "iam:manage");
        Guid target = Guid.NewGuid();
        Guid roleId = Guid.NewGuid();
        db.IamPrincipals.Add(
            new()
            {
                Id = target,
                PrincipalType = IamPrincipalType.Employee,
                Name = "target",
                IsActive = false,
            }
        );
        db.IamRoles.Add(new() { Id = roleId, Name = "support" });
        await db.SaveChangesAsync();

        Result<IamRoleAssignmentDto> result = await sut.AssignRoleAsync(
            manager,
            target,
            roleId,
            null,
            null,
            null
        );

        result.ErrorCode.Should().Be("TARGET_INACTIVE");
    }

    /// <summary>
    /// The capability guard is reachable independent of whether the acting caller itself holds
    /// iam:manage — self-host bypasses the actor permission check entirely (fix D2), so this is the
    /// scenario where it matters: nothing here checks the caller's own grants, only whether the target
    /// is the platform's last active iam:manage holder.
    /// </summary>
    [Fact]
    public async Task Deactivating_the_last_iam_manage_holder_is_refused()
    {
        (PlatformIamService sut, AuthDbContext db, _) = Build(DeploymentMode.SelfHostLite);
        Guid onlyManager = SeedPrincipalWithPermission(db, "iam:manage");
        await db.SaveChangesAsync();

        Result result = await sut.DeactivatePrincipalAsync(
            Guid.NewGuid(),
            onlyManager,
            reason: null
        );

        result.ErrorCode.Should().Be("LAST_MANAGER");
        (await db.IamPrincipals.SingleAsync(p => p.Id == onlyManager)).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Deactivating_a_non_last_iam_manage_holder_succeeds()
    {
        (PlatformIamService sut, AuthDbContext db, _) = Build(DeploymentMode.Saas);
        Guid manager = SeedPrincipalWithPermission(db, "iam:manage");
        Guid otherManager = SeedPrincipalWithPermission(db, "iam:manage");
        await db.SaveChangesAsync();

        Result result = await sut.DeactivatePrincipalAsync(manager, otherManager, reason: null);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        (await db.IamPrincipals.SingleAsync(p => p.Id == otherManager)).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Revoking_the_last_active_grant_of_iam_manage_is_refused()
    {
        (PlatformIamService sut, AuthDbContext db, _) = Build(DeploymentMode.Saas);
        Guid onlyManager = SeedPrincipalWithPermission(db, "iam:manage");
        await db.SaveChangesAsync();
        IamRoleAssignment assignment = await db.IamRoleAssignments.FirstAsync(a =>
            a.PrincipalId == onlyManager
        );

        Result result = await sut.RevokeAssignmentAsync(onlyManager, assignment.Id, reason: null);

        result.ErrorCode.Should().Be("LAST_MANAGER");
        (await db.IamRoleAssignments.SingleAsync(a => a.Id == assignment.Id))
            .RevokedAt.Should()
            .BeNull();
    }

    [Fact]
    public async Task List_roles_returns_each_role_with_its_permission_bundle()
    {
        (PlatformIamService sut, AuthDbContext db, _) = Build(DeploymentMode.Saas);
        SeedPrincipalWithPermission(db, "iam:manage"); // seeds one role bound to iam:manage
        db.IamRoles.Add(
            new()
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
        (PlatformIamService sut, AuthDbContext db, _) = Build(DeploymentMode.Saas);
        Guid principalId = SeedPrincipalWithPermission(db, "iam:manage");
        Guid roleId = Guid.NewGuid();
        db.IamRoles.Add(new() { Id = roleId, Name = "stale-role" });
        // A revoked and an expired assignment — both must be filtered out of the summary.
        db.IamRoleAssignments.Add(
            new()
            {
                PrincipalId = principalId,
                RoleId = roleId,
                AssignedByPrincipalId = principalId,
                RevokedAt = Now.UtcDateTime.AddDays(-1),
            }
        );
        db.IamRoleAssignments.Add(
            new()
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
