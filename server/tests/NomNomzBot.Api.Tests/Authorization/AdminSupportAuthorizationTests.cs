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
using Microsoft.Extensions.Options;
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
/// S-ADMIN-7a: the cross-tenant support desk is a PRIVILEGED, audited plane of its own. Proves the whole
/// gate chain the route actually runs — the controller carries <c>user:support:view</c> as its policy, the
/// dynamic provider synthesizes that name into a Plane-C requirement, and the REAL
/// <see cref="PlatformIamService"/> (SaaS, so no self-host short-circuit) DENIES an operator who holds the
/// rest of the support bundle but not this key. Holding <c>user:impersonate</c> is not a substitute either:
/// seeing a subject's state and acting AS them are deliberately separate grants.
/// </summary>
public sealed class AdminSupportAuthorizationTests
{
    private static readonly Guid OperatorUser = Guid.Parse("0199a000-0000-7000-8000-000000000c01");

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

    private static async Task SeedPrincipalWithPermissionsAsync(
        ApiTestDbContext db,
        params string[] keys
    )
    {
        IamPrincipal principal = new()
        {
            PrincipalType = IamPrincipalType.Employee,
            UserId = OperatorUser,
            Name = "support-operator",
            IsActive = true,
        };
        IamRole role = new() { Name = "role-under-test", IsSystem = true };
        db.IamPrincipals.Add(principal);
        db.IamRoles.Add(role);
        foreach (string key in keys)
        {
            IamPermission permission = new() { Key = key, Category = IamCategory.Iam };
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

    private static string PolicyOf(Type controllerType, string methodName)
    {
        MethodInfo method =
            controllerType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingMethodException(controllerType.Name, methodName);
        AuthorizeAttribute? attribute = method
            .GetCustomAttributes<AuthorizeAttribute>()
            .FirstOrDefault(a => !string.IsNullOrEmpty(a.Policy));
        attribute
            .Should()
            .NotBeNull($"{controllerType.Name}.{methodName} must carry a Plane-C policy gate");
        return attribute.Policy!;
    }

    [Theory]
    [InlineData(nameof(AdminSupportController.SearchPeople))]
    [InlineData(nameof(AdminSupportController.GetPerson))]
    public void Both_support_desk_actions_are_gated_on_the_support_view_key(string methodName)
    {
        PolicyOf(typeof(AdminSupportController), methodName)
            .Should()
            .Be(IamPermissionKeys.UserSupportView);
    }

    [Fact]
    public async Task The_policy_name_resolves_to_a_plane_c_requirement()
    {
        AuthorizationPolicy? policy = await new ActionAuthorizationPolicyProvider(
            Options.Create(new AuthorizationOptions())
        ).GetPolicyAsync(IamPermissionKeys.UserSupportView);

        policy.Should().NotBeNull();
        policy
            .Requirements.OfType<PlatformIamRequirement>()
            .Single()
            .PermissionKey.Should()
            .Be(IamPermissionKeys.UserSupportView);
    }

    [Fact]
    public async Task An_operator_holding_the_support_view_key_is_allowed()
    {
        (PlatformIamAuthorizationHandler handler, ApiTestDbContext db) = BuildSaas();
        await SeedPrincipalWithPermissionsAsync(db, IamPermissionKeys.UserSupportView);
        AuthorizationHandlerContext context = Context(IamPermissionKeys.UserSupportView);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(IamPermissionKeys.TenantRead)]
    [InlineData(IamPermissionKeys.TenantAccess)]
    [InlineData(IamPermissionKeys.AuditRead)]
    [InlineData(IamPermissionKeys.UserImpersonate)]
    public async Task An_operator_without_the_support_view_key_is_denied(string ownedKey)
    {
        (PlatformIamAuthorizationHandler handler, ApiTestDbContext db) = BuildSaas();
        await SeedPrincipalWithPermissionsAsync(db, ownedKey);
        AuthorizationHandlerContext context = Context(IamPermissionKeys.UserSupportView);

        await handler.HandleAsync(context);

        context
            .HasSucceeded.Should()
            .BeFalse($"{ownedKey} must not imply {IamPermissionKeys.UserSupportView}");
    }
}
