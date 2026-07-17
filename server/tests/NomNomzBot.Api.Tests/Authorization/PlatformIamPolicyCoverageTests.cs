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
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using NomNomzBot.Api.Controllers;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Domain.Identity;

namespace NomNomzBot.Api.Tests.Authorization;

/// <summary>
/// Regression guard for the Plane-C migration off raw role-strings: every admin-plane action must carry the
/// spec-named IAM permission key as its policy (class- or action-level), and the raw
/// <c>[Authorize(Roles=...)]</c> form must not exist ANYWHERE in the Api assembly (controllers or hubs) —
/// the policy form is what routes through <c>IPlatformIamService</c> and gets audited on SaaS.
/// </summary>
public sealed class PlatformIamPolicyCoverageTests
{
    private static string PolicyOf(Type controllerType, string methodName)
    {
        MethodInfo method =
            controllerType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingMethodException(controllerType.Name, methodName);

        AuthorizeAttribute? attribute = method
            .GetCustomAttributes<AuthorizeAttribute>()
            .FirstOrDefault(a => !string.IsNullOrEmpty(a.Policy));
        attribute ??= controllerType
            .GetCustomAttributes<AuthorizeAttribute>()
            .FirstOrDefault(a => !string.IsNullOrEmpty(a.Policy));

        attribute
            .Should()
            .NotBeNull($"{controllerType.Name}.{methodName} must carry a Plane-C policy gate");
        return attribute!.Policy!;
    }

    [Theory]
    [InlineData(nameof(AdminController.GetAdminStats), IamPermissionKeys.PlatformAnalyticsRead)]
    [InlineData(nameof(AdminController.ListChannels), IamPermissionKeys.TenantRead)]
    [InlineData(nameof(AdminController.ListUsers), IamPermissionKeys.IamManage)]
    [InlineData(nameof(AdminController.GetSystemHealth), IamPermissionKeys.IamManage)]
    [InlineData(nameof(AdminController.GetHealth), IamPermissionKeys.IamManage)]
    [InlineData(nameof(AdminController.GetEvents), IamPermissionKeys.IamManage)]
    public void AdminController_action_carries_the_expected_iam_policy(
        string methodName,
        string expectedKey
    )
    {
        PolicyOf(typeof(AdminController), methodName).Should().Be(expectedKey);
    }

    [Theory]
    [InlineData(nameof(AdminBillingController.ListInvites), IamPermissionKeys.BillingRead)]
    [InlineData(nameof(AdminBillingController.CreateInvite), IamPermissionKeys.IamManage)]
    [InlineData(nameof(AdminBillingController.RevokeInvite), IamPermissionKeys.IamManage)]
    [InlineData(nameof(AdminBillingController.GrantTier), IamPermissionKeys.IamManage)]
    [InlineData(nameof(AdminBillingController.GrantFounder), IamPermissionKeys.IamManage)]
    public void AdminBillingController_action_carries_the_expected_iam_policy(
        string methodName,
        string expectedKey
    )
    {
        PolicyOf(typeof(AdminBillingController), methodName).Should().Be(expectedKey);
    }

    [Theory]
    [InlineData(nameof(FederationController.ListPeers), IamPermissionKeys.AuditRead)]
    [InlineData(nameof(FederationController.GetPeer), IamPermissionKeys.AuditRead)]
    [InlineData(nameof(FederationController.RegisterPeer), IamPermissionKeys.IamManage)]
    [InlineData(nameof(FederationController.TrustPeer), IamPermissionKeys.IamManage)]
    [InlineData(nameof(FederationController.RevokePeer), IamPermissionKeys.IamManage)]
    [InlineData(nameof(FederationController.AddPeerKey), IamPermissionKeys.IamManage)]
    [InlineData(nameof(FederationController.DeactivatePeerKey), IamPermissionKeys.IamManage)]
    public void FederationController_action_carries_the_expected_iam_policy(
        string methodName,
        string expectedKey
    )
    {
        PolicyOf(typeof(FederationController), methodName).Should().Be(expectedKey);
    }

    [Theory]
    [InlineData(nameof(PlatformIamController.ListRoles), IamPermissionKeys.IamManage)]
    [InlineData(nameof(PlatformIamController.ListPrincipals), IamPermissionKeys.IamManage)]
    [InlineData(nameof(PlatformIamController.GetEffectivePermissions), IamPermissionKeys.IamManage)]
    [InlineData(
        nameof(PlatformIamController.CreatePrincipal),
        IamPermissionKeys.IamPrincipalCreate
    )]
    [InlineData(nameof(PlatformIamController.DeactivatePrincipal), IamPermissionKeys.IamManage)]
    [InlineData(nameof(PlatformIamController.ReactivatePrincipal), IamPermissionKeys.IamManage)]
    [InlineData(nameof(PlatformIamController.AssignRole), IamPermissionKeys.IamManage)]
    [InlineData(nameof(PlatformIamController.RevokeAssignment), IamPermissionKeys.IamManage)]
    public void PlatformIamController_action_carries_the_expected_iam_policy(
        string methodName,
        string expectedKey
    )
    {
        PolicyOf(typeof(PlatformIamController), methodName).Should().Be(expectedKey);
    }

    [Theory]
    [InlineData(nameof(PlatformAdminController.ListTenants), IamPermissionKeys.TenantRead)]
    [InlineData(nameof(PlatformAdminController.GetTenant), IamPermissionKeys.TenantRead)]
    [InlineData(nameof(PlatformAdminController.SuspendTenant), IamPermissionKeys.TenantSuspend)]
    [InlineData(nameof(PlatformAdminController.ReinstateTenant), IamPermissionKeys.TenantSuspend)]
    [InlineData(nameof(PlatformAdminController.BeginTenantAccess), IamPermissionKeys.TenantAccess)]
    [InlineData(nameof(PlatformAdminController.EndTenantAccess), IamPermissionKeys.TenantAccess)]
    [InlineData(nameof(PlatformAdminController.SearchAudit), IamPermissionKeys.AuditRead)]
    public void PlatformAdminController_action_carries_the_expected_iam_policy(
        string methodName,
        string expectedKey
    )
    {
        PolicyOf(typeof(PlatformAdminController), methodName).Should().Be(expectedKey);
    }

    [Theory]
    [InlineData(nameof(ComplianceController.RequestErasure), IamPermissionKeys.TenantAccess)]
    [InlineData(nameof(ComplianceController.ListErasureRequests), IamPermissionKeys.AuditRead)]
    public void ComplianceController_action_carries_the_expected_iam_policy(
        string methodName,
        string expectedKey
    )
    {
        PolicyOf(typeof(ComplianceController), methodName).Should().Be(expectedKey);
    }

    [Fact]
    public void FeatureFlagAdminController_is_class_gated_on_featureflag_write()
    {
        PolicyOf(typeof(FeatureFlagAdminController), nameof(FeatureFlagAdminController.List))
            .Should()
            .Be(IamPermissionKeys.FeatureFlagWrite);
    }

    [Fact]
    public void PlatformAnalyticsController_is_class_gated_on_platform_analytics_read()
    {
        PolicyOf(typeof(PlatformAnalyticsController), nameof(PlatformAnalyticsController.GetStats))
            .Should()
            .Be(IamPermissionKeys.PlatformAnalyticsRead);
    }

    [Theory]
    [InlineData(nameof(AuthController.StartBotOAuth), IamPermissionKeys.IamManage)]
    [InlineData(nameof(AuthController.GetBotStatus), IamPermissionKeys.IamManage)]
    [InlineData(nameof(AuthController.DisconnectBot), IamPermissionKeys.IamManage)]
    [InlineData(nameof(AuthController.StartBotDeviceLogin), IamPermissionKeys.IamManage)]
    [InlineData(nameof(AuthController.PollBotDeviceLogin), IamPermissionKeys.IamManage)]
    public void AuthController_platform_bot_action_carries_the_iam_manage_policy(
        string methodName,
        string expectedKey
    )
    {
        PolicyOf(typeof(AuthController), methodName).Should().Be(expectedKey);
    }

    [Fact]
    public void AdminHub_is_gated_on_iam_manage()
    {
        // A hub cannot be invoked through the MVC test harness, so the runtime chain is proven piecewise:
        // hubs are mapped endpoints whose class-level [Authorize] policy gates the connection handshake
        // (framework behavior), the policy name resolves to a PlatformIamRequirement
        // (ActionAuthorizationPolicyProviderTests), and that requirement's enforcement is proven end to end
        // over the real service (PlatformIamAuthorizationHandlerTests). This pins the remaining link: the
        // attribute itself.
        AuthorizeAttribute attribute = typeof(AdminHub)
            .GetCustomAttributes<AuthorizeAttribute>()
            .Single(a => !string.IsNullOrEmpty(a.Policy));

        attribute.Policy.Should().Be(IamPermissionKeys.IamManage);
        attribute.Roles.Should().BeNull();
    }

    [Fact]
    public void No_raw_role_string_gate_remains_anywhere_in_the_api_assembly()
    {
        // Controllers AND hubs, class- and action/method-level. A [Authorize(Roles=...)] gate bypasses the
        // IAM service (no audit, no principal model) — the whole assembly must be off it.
        IReadOnlyList<Type> gateSurfaces =
        [
            .. typeof(BaseController)
                .Assembly.GetTypes()
                .Where(t =>
                    !t.IsAbstract
                    && (
                        typeof(ControllerBase).IsAssignableFrom(t)
                        || typeof(Hub).IsAssignableFrom(t)
                    )
                ),
        ];

        List<string> offenders = [];
        foreach (Type type in gateSurfaces)
        {
            if (
                type.GetCustomAttributes<AuthorizeAttribute>()
                    .Any(a => !string.IsNullOrEmpty(a.Roles))
            )
                offenders.Add(type.Name);
            foreach (
                MethodInfo method in type.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
                )
            )
            {
                if (
                    method
                        .GetCustomAttributes<AuthorizeAttribute>()
                        .Any(a => !string.IsNullOrEmpty(a.Roles))
                )
                    offenders.Add($"{type.Name}.{method.Name}");
            }
        }

        string.Join(Environment.NewLine, offenders)
            .Should()
            .BeEmpty("Plane-C gates on IAM policy keys, never on raw role-strings");
    }
}
