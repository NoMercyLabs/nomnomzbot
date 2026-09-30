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
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Tests.Controllers;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Authorization;

/// <summary>
/// S086e regression: proves the closed permission set over the REAL <see cref="PlatformIamService"/> (SaaS
/// mode, so the platform-marked/no-principal short-circuits don't apply) — a principal holding exactly
/// <c>platform-billing</c>'s bundle can perform every billing write + the refund but is DENIED
/// <c>iam:manage</c> (no privilege bleed), and a principal holding only the support-visit
/// <c>tenant:access</c> key is DENIED admin GDPR erasure while one holding <c>compliance:erasure</c> is
/// allowed.
/// </summary>
public sealed class PlatformBillingAndErasureAuthorizationTests
{
    private static readonly Guid OperatorUser = Guid.Parse("0199a000-0000-7000-8000-000000000b01");

    private static (PlatformIamAuthorizationHandler Handler, ApiTestDbContext Db) BuildSaas()
    {
        ApiTestDbContext db = ApiTestDbContext.New();
        PlatformIamService iam = new(
            db,
            Substitute.For<IEventBus>(),
            TimeProvider.System,
            new(DeploymentMode.Saas)
        );
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(OperatorUser.ToString());
        return (new(iam, currentUser), db);
    }

    private static AuthorizationHandlerContext Context(string permissionKey)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, OperatorUser.ToString()),
            new(ClaimTypes.Role, PlatformIamAuthorizationHandler.PlatformPrincipalRole),
        ];
        ClaimsPrincipal principal = new(new ClaimsIdentity(claims, "TestAuth"));
        return new([new PlatformIamRequirement(permissionKey)], principal, resource: null);
    }

    /// <summary>Seeds a principal for <see cref="OperatorUser"/> holding exactly <paramref name="keys"/>.</summary>
    private static async Task SeedPrincipalWithPermissionsAsync(
        ApiTestDbContext db,
        params string[] keys
    )
    {
        IamPrincipal principal = new()
        {
            PrincipalType = IamPrincipalType.Employee,
            UserId = OperatorUser,
            Name = "operator",
            IsActive = true,
        };
        IamRole role = new() { Name = "role-under-test", IsSystem = true };
        db.IamPrincipals.Add(principal);
        db.IamRoles.Add(role);
        foreach (string key in keys)
        {
            IamPermission permission = new() { Key = key, Category = IamCategory.Billing };
            db.IamPermissions.Add(permission);
            db.IamRolePermissions.Add(new() { RoleId = role.Id, PermissionId = permission.Id });
        }
        db.IamRoleAssignments.Add(
            new()
            {
                PrincipalId = principal.Id,
                RoleId = role.Id,
                AssignedByPrincipalId = principal.Id,
            }
        );
        await db.SaveChangesAsync();
    }

    public static readonly TheoryData<string> PlatformBillingBundleKeys =
    [
        IamPermissionKeys.BillingRead,
        IamPermissionKeys.BillingWrite,
        IamPermissionKeys.BillingRefund,
    ];

    [Theory]
    [MemberData(nameof(PlatformBillingBundleKeys))]
    public async Task Principal_holding_only_the_platform_billing_bundle_is_allowed_every_key_in_it(
        string ownedKey
    )
    {
        (PlatformIamAuthorizationHandler handler, ApiTestDbContext db) = BuildSaas();
        await SeedPrincipalWithPermissionsAsync(
            db,
            IamPermissionKeys.BillingRead,
            IamPermissionKeys.BillingWrite,
            IamPermissionKeys.BillingRefund
        );
        AuthorizationHandlerContext context = Context(ownedKey);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue($"platform-billing holds {ownedKey}");
    }

    [Fact]
    public async Task Principal_holding_only_the_platform_billing_bundle_is_denied_iam_manage()
    {
        (PlatformIamAuthorizationHandler handler, ApiTestDbContext db) = BuildSaas();
        await SeedPrincipalWithPermissionsAsync(
            db,
            IamPermissionKeys.BillingRead,
            IamPermissionKeys.BillingWrite,
            IamPermissionKeys.BillingRefund
        );
        AuthorizationHandlerContext context = Context(IamPermissionKeys.IamManage);

        await handler.HandleAsync(context);

        context
            .HasSucceeded.Should()
            .BeFalse("billing:* must not bleed into iam:manage — no privilege escalation");
    }

    [Fact]
    public async Task Principal_holding_only_tenant_access_is_denied_admin_gdpr_erasure()
    {
        (PlatformIamAuthorizationHandler handler, ApiTestDbContext db) = BuildSaas();
        await SeedPrincipalWithPermissionsAsync(db, IamPermissionKeys.TenantAccess);
        AuthorizationHandlerContext context = Context(IamPermissionKeys.ComplianceErasure);

        await handler.HandleAsync(context);

        context
            .HasSucceeded.Should()
            .BeFalse("a support-visit grant must not permit destructive subject erasure");
    }

    /// <summary>
    /// The data-requests list is read under <c>audit:read</c>; re-running a failed erasure from that list is
    /// gated on <c>compliance:erasure</c>. An operator who can SEE the failed row must not be able to erase.
    /// </summary>
    [Fact]
    public async Task Principal_holding_only_audit_read_is_denied_the_erasure_rerun()
    {
        (PlatformIamAuthorizationHandler handler, ApiTestDbContext db) = BuildSaas();
        await SeedPrincipalWithPermissionsAsync(db, IamPermissionKeys.AuditRead);
        string rerunPolicy = typeof(ComplianceController)
            .GetMethod(nameof(ComplianceController.RetryErasure))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
            .Cast<AuthorizeAttribute>()
            .Single()
            .Policy!;
        AuthorizationHandlerContext context = Context(rerunPolicy);

        await handler.HandleAsync(context);

        rerunPolicy.Should().Be(IamPermissionKeys.ComplianceErasure);
        context
            .HasSucceeded.Should()
            .BeFalse("reading the request ledger must not permit re-running an erasure");
    }

    /// <summary>A signed-in user without the platform-principal marker never reaches the IAM check.</summary>
    [Fact]
    public async Task A_caller_without_the_platform_marker_is_denied_the_erasure_rerun()
    {
        (PlatformIamAuthorizationHandler handler, ApiTestDbContext db) = BuildSaas();
        await SeedPrincipalWithPermissionsAsync(db, IamPermissionKeys.ComplianceErasure);
        ClaimsPrincipal viewer = new(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, OperatorUser.ToString())],
                "TestAuth"
            )
        );
        AuthorizationHandlerContext context = new(
            [new PlatformIamRequirement(IamPermissionKeys.ComplianceErasure)],
            viewer,
            resource: null
        );

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse("only a platform principal may re-run an erasure");
    }

    [Fact]
    public async Task Principal_holding_compliance_erasure_is_allowed_admin_gdpr_erasure()
    {
        (PlatformIamAuthorizationHandler handler, ApiTestDbContext db) = BuildSaas();
        await SeedPrincipalWithPermissionsAsync(db, IamPermissionKeys.ComplianceErasure);
        AuthorizationHandlerContext context = Context(IamPermissionKeys.ComplianceErasure);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }
}
