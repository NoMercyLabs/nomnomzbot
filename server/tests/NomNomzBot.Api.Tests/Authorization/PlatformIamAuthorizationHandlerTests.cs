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
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Platform.Deployment;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Authorization;

/// <summary>
/// Proves Plane-C enforcement end to end — the handler over the REAL <see cref="PlatformIamService"/> and a
/// seeded store, not a mock of it. The hard self-host invariant: <c>AuthorizePlatformAsync</c> short-circuits
/// to ALLOW on self-host — decided from the DEPLOYMENT MODE (fix D2), never from whether any
/// <c>IamPrincipal</c> row exists — so the handler must demand the platform-principal marker (the
/// <c>admin</c> role claim, minted only for <c>User.IsPlatformPrincipal</c>) BEFORE consulting the service —
/// otherwise every authenticated viewer would clear Plane-C on self-host. On SaaS, a principal without the
/// permission is denied AND audited; one with it passes AND is audited.
/// </summary>
public sealed class PlatformIamAuthorizationHandlerTests
{
    private static readonly Guid OperatorUser = Guid.Parse("0199a000-0000-7000-8000-000000000a01");

    private const string Permission = IamPermissionKeys.IamManage;

    private static (PlatformIamAuthorizationHandler Handler, ApiTestDbContext Db) BuildReal(
        Guid userId,
        DeploymentMode mode = DeploymentMode.SelfHostLite
    )
    {
        ApiTestDbContext db = ApiTestDbContext.New();
        PlatformIamService iam = new(
            db,
            Substitute.For<IEventBus>(),
            TimeProvider.System,
            new(mode)
        );
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(userId.ToString());
        return (new(iam, currentUser), db);
    }

    private static AuthorizationHandlerContext Context(bool platformMarked)
    {
        List<Claim> claims = [new(ClaimTypes.NameIdentifier, OperatorUser.ToString())];
        if (platformMarked)
            claims.Add(new(ClaimTypes.Role, PlatformIamAuthorizationHandler.PlatformPrincipalRole));
        ClaimsPrincipal principal = new(new ClaimsIdentity(claims, "TestAuth"));
        return new([new PlatformIamRequirement(Permission)], principal, resource: null);
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
        db.IamRolePermissions.Add(new() { RoleId = role.Id, PermissionId = permission.Id });
        db.IamRoleAssignments.Add(
            new()
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
        (PlatformIamAuthorizationHandler handler, ApiTestDbContext db) = BuildReal(
            OperatorUser,
            DeploymentMode.Saas
        );
        // A DIFFERENT user's principal exists, but the caller has no principal row.
        db.IamPrincipals.Add(
            new()
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

    /// <summary>
    /// Fix D2 item 1 at the handler level: creating a service-account principal must NOT flip a self-host
    /// deployment into default-deny. Before the fix, "is this SaaS" was decided by "does any IamPrincipal row
    /// exist" — this exact scenario (one extra principal, self-host deployment mode unchanged) would have
    /// locked the platform-marked operator out.
    /// </summary>
    [Fact]
    public async Task Self_host_platform_marked_operator_still_passes_after_a_service_account_principal_exists()
    {
        (PlatformIamAuthorizationHandler handler, ApiTestDbContext db) = BuildReal(OperatorUser);
        db.IamPrincipals.Add(
            new()
            {
                PrincipalType = IamPrincipalType.ServiceAccount,
                Name = "ci-bot",
                IsActive = true,
            }
        );
        await db.SaveChangesAsync();
        AuthorizationHandlerContext context = Context(platformMarked: true);

        await handler.HandleAsync(context);

        context
            .HasSucceeded.Should()
            .BeTrue("self-host stays implicitly-full regardless of principal rows");
        (await db.IamAuditLogs.CountAsync()).Should().Be(0);
    }

    // ── SaaS: real authorize + audit consequences ───────────────────────────────

    [Fact]
    public async Task Saas_principal_without_the_permission_is_denied_and_the_denial_is_audited()
    {
        (PlatformIamAuthorizationHandler handler, ApiTestDbContext db) = BuildReal(
            OperatorUser,
            DeploymentMode.Saas
        );
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
        (PlatformIamAuthorizationHandler handler, ApiTestDbContext db) = BuildReal(
            OperatorUser,
            DeploymentMode.Saas
        );
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
