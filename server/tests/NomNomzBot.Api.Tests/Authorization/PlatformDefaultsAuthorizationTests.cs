// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
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
/// Plan item A4: every platform-default editor is gated on <c>platform:defaults:manage</c> at the controller
/// level, and the gate runs against the REAL <see cref="PlatformIamService"/> on SaaS — an operator holding the
/// key passes, one holding only a neighbouring admin key (or no platform grant at all) is refused.
/// </summary>
public sealed class PlatformDefaultsAuthorizationTests
{
    private static readonly Guid OperatorUser = Guid.Parse("0199f100-0000-7000-8000-000000000d01");

    public static TheoryData<Type> Controllers => new() { typeof(ActionDefaultsAdminController) };

    [Theory]
    [MemberData(nameof(Controllers))]
    public void Every_platform_default_controller_is_gated_on_its_own_key(Type controller)
    {
        AuthorizeAttribute? gate = controller
            .GetCustomAttributes<AuthorizeAttribute>()
            .SingleOrDefault(a => !string.IsNullOrEmpty(a.Policy));

        gate.Should().NotBeNull($"{controller.Name} must carry a Plane-C policy gate");
        gate.Policy.Should().Be(IamPermissionKeys.PlatformDefaultsManage);
        controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<AllowAnonymousAttribute>())
            .Should()
            .BeEmpty("no action may opt out of the class-level gate");
    }

    [Fact]
    public async Task An_operator_holding_the_key_is_allowed()
    {
        (PlatformIamAuthorizationHandler handler, ApiTestDbContext db) = BuildSaas();
        await SeedPrincipalWithPermissionsAsync(db, IamPermissionKeys.PlatformDefaultsManage);
        AuthorizationHandlerContext context = Context();

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(IamPermissionKeys.FeatureFlagWrite)]
    [InlineData(IamPermissionKeys.ContentAuthor)]
    [InlineData(IamPermissionKeys.TenantRead)]
    [InlineData(IamPermissionKeys.AuditRead)]
    public async Task An_operator_without_the_key_is_refused(string ownedKey)
    {
        (PlatformIamAuthorizationHandler handler, ApiTestDbContext db) = BuildSaas();
        await SeedPrincipalWithPermissionsAsync(db, ownedKey);
        AuthorizationHandlerContext context = Context();

        await handler.HandleAsync(context);

        context
            .HasSucceeded.Should()
            .BeFalse($"{ownedKey} must not imply {IamPermissionKeys.PlatformDefaultsManage}");
    }

    [Fact]
    public async Task A_signed_in_user_with_no_platform_grant_is_refused()
    {
        (PlatformIamAuthorizationHandler handler, ApiTestDbContext _) = BuildSaas();
        AuthorizationHandlerContext context = Context();

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

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

    private static AuthorizationHandlerContext Context()
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, OperatorUser.ToString()),
            new(ClaimTypes.Role, PlatformIamAuthorizationHandler.PlatformPrincipalRole),
        ];
        ClaimsPrincipal principal = new(new ClaimsIdentity(claims, "TestAuth"));
        return new(
            [new PlatformIamRequirement(IamPermissionKeys.PlatformDefaultsManage)],
            principal,
            resource: null
        );
    }

    private static async Task SeedPrincipalWithPermissionsAsync(
        ApiTestDbContext db,
        params string[] keys
    )
    {
        IamPrincipal principal = new()
        {
            PrincipalType = IamPrincipalType.Employee,
            UserId = OperatorUser,
            Name = "platform-defaults-operator",
            IsActive = true,
        };
        IamRole role = new() { Name = "role-under-test", IsSystem = true };
        db.IamPrincipals.Add(principal);
        db.IamRoles.Add(role);
        foreach (string key in keys)
        {
            IamPermission permission = new() { Key = key, Category = IamCategory.Content };
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
}
