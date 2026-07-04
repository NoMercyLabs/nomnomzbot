// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Tests.Controllers;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Authorization;

/// <summary>
/// Proves Plane-C enforcement end to end — the handler over the REAL <see cref="PlatformIamService"/> and a
/// seeded store, not a mock of it. The hard self-host invariant: <c>AuthorizePlatformAsync</c> short-circuits
/// to ALLOW when zero <c>IamPrincipal</c>s exist, so the handler must demand the platform-principal marker
/// (the <c>admin</c> role claim, minted only for <c>User.IsPlatformPrincipal</c>) BEFORE consulting the
/// service — otherwise every authenticated viewer would clear Plane-C on self-host. On a SaaS-shaped store,
/// a principal without the permission is denied AND audited; one with it passes AND is audited.
/// </summary>
public sealed class PlatformIamAuthorizationHandlerTests
{
    private static readonly Guid OperatorUser = Guid.Parse("0199a000-0000-7000-8000-000000000a01");

    private const string Permission = IamPermissionKeys.IamManage;

    private static (PlatformIamAuthorizationHandler Handler, ApiTestDbContext Db) BuildReal(
        Guid userId
    )
    {
        ApiTestDbContext db = ApiTestDbContext.New();
        PlatformIamService iam = new(db, Substitute.For<IEventBus>(), TimeProvider.System);
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(userId.ToString());
        return (new PlatformIamAuthorizationHandler(iam, currentUser), db);
    }

    private static AuthorizationHandlerContext Context(bool platformMarked)
    {
        List<Claim> claims = [new Claim(ClaimTypes.NameIdentifier, OperatorUser.ToString())];
        if (platformMarked)
            claims.Add(
                new Claim(ClaimTypes.Role, PlatformIamAuthorizationHandler.PlatformPrincipalRole)
            );
        ClaimsPrincipal principal = new(new ClaimsIdentity(claims, "TestAuth"));
        return new AuthorizationHandlerContext(
            [new PlatformIamRequirement(Permission)],
            principal,
            resource: null
        );
    }

    /// <summary>Seeds a principal for <see cref="OperatorUser"/> holding exactly <paramref name="permissionKey"/>.</summary>
    private static async Task<Guid> SeedPrincipalWithPermissionAsync(
        ApiTestDbContext db,
        string permissionKey
    )
    {
        IamPrincipal principal = new()
        {
            PrincipalType = IamPrincipalType.Employee,
            UserId = OperatorUser,
            Name = "operator",
            IsActive = true,
        };
        IamRole role = new() { Name = $"role-{permissionKey}", IsSystem = true };
        IamPermission permission = new() { Key = permissionKey, Category = IamCategory.Iam };
        db.IamPrincipals.Add(principal);
        db.IamRoles.Add(role);
        db.IamPermissions.Add(permission);
        db.IamRolePermissions.Add(
            new IamRolePermission { RoleId = role.Id, PermissionId = permission.Id }
        );
        db.IamRoleAssignments.Add(
            new IamRoleAssignment
            {
                PrincipalId = principal.Id,
                RoleId = role.Id,
                AssignedByPrincipalId = principal.Id,
            }
        );
        await db.SaveChangesAsync();
        return principal.Id;
    }

    // ── The hard self-host invariant ────────────────────────────────────────────

    [Fact]
    public async Task Authenticated_caller_without_the_platform_marker_is_denied_and_the_iam_service_is_never_consulted()
    {
        // Mocked service so the zero-interaction fact is provable: were the handler to consult it, the
        // self-host short-circuit would return ALLOW and hand every viewer Plane-C access.
        IPlatformIamService iam = Substitute.For<IPlatformIamService>();
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(OperatorUser.ToString());
        PlatformIamAuthorizationHandler handler = new(iam, currentUser);
        AuthorizationHandlerContext context = Context(platformMarked: false);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        await iam.DidNotReceiveWithAnyArgs().ResolvePrincipalAsync(default, default);
        await iam.DidNotReceiveWithAnyArgs().HasAnyPrincipalsAsync(default);
        await iam.DidNotReceiveWithAnyArgs()
            .AuthorizePlatformAsync(default, default!, default, default, default, default);
    }

    [Fact]
    public async Task Self_host_platform_marked_operator_passes_with_zero_principal_rows()
    {
        (PlatformIamAuthorizationHandler handler, ApiTestDbContext db) = BuildReal(OperatorUser);
        AuthorizationHandlerContext context = Context(platformMarked: true);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
        (await db.IamAuditLogs.CountAsync()).Should().Be(0, "self-host writes no audit");
    }

    [Fact]
    public async Task Saas_platform_marked_caller_without_a_principal_row_is_denied()
    {
        (PlatformIamAuthorizationHandler handler, ApiTestDbContext db) = BuildReal(OperatorUser);
        // A DIFFERENT user's principal exists → SaaS-shaped store, but the caller has no principal row.
        db.IamPrincipals.Add(
            new IamPrincipal
            {
                PrincipalType = IamPrincipalType.Employee,
                UserId = Guid.NewGuid(),
                Name = "someone-else",
                IsActive = true,
            }
        );
        await db.SaveChangesAsync();
        AuthorizationHandlerContext context = Context(platformMarked: true);

        await handler.HandleAsync(context);

        context
            .HasSucceeded.Should()
            .BeFalse("a marker without a principal row is a misconfiguration");
    }

    // ── SaaS: real authorize + audit consequences ───────────────────────────────

    [Fact]
    public async Task Saas_principal_without_the_permission_is_denied_and_the_denial_is_audited()
    {
        (PlatformIamAuthorizationHandler handler, ApiTestDbContext db) = BuildReal(OperatorUser);
        Guid principalId = await SeedPrincipalWithPermissionAsync(
            db,
            IamPermissionKeys.BillingRead // holds billing:read, NOT the required iam:manage
        );
        AuthorizationHandlerContext context = Context(platformMarked: true);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        IamAuditLog audit = await db.IamAuditLogs.SingleAsync();
        audit.PrincipalId.Should().Be(principalId);
        audit.Permission.Should().Be(Permission);
        audit.Outcome.Should().Be(IamOutcome.Denied);
    }

    [Fact]
    public async Task Saas_principal_with_the_permission_passes_and_the_allow_is_audited()
    {
        (PlatformIamAuthorizationHandler handler, ApiTestDbContext db) = BuildReal(OperatorUser);
        Guid principalId = await SeedPrincipalWithPermissionAsync(db, Permission);
        AuthorizationHandlerContext context = Context(platformMarked: true);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
        IamAuditLog audit = await db.IamAuditLogs.SingleAsync();
        audit.PrincipalId.Should().Be(principalId);
        audit.Permission.Should().Be(Permission);
        audit.Outcome.Should().Be(IamOutcome.Allowed);
        audit
            .TargetBroadcasterId.Should()
            .BeNull("controller-level Plane-C checks are platform-global");
    }

    [Fact]
    public async Task Unauthenticated_caller_is_denied_before_anything_else()
    {
        IPlatformIamService iam = Substitute.For<IPlatformIamService>();
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        currentUser.IsAuthenticated.Returns(false);
        currentUser.UserId.Returns((string?)null);
        PlatformIamAuthorizationHandler handler = new(iam, currentUser);
        AuthorizationHandlerContext context = Context(platformMarked: true);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        await iam.DidNotReceiveWithAnyArgs().ResolvePrincipalAsync(default, default);
    }
}
