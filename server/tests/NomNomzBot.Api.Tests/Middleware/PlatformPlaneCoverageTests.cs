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
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Middleware;

namespace NomNomzBot.Api.Tests.Middleware;

/// <summary>
/// The admin console's pages act across every tenant: they read through services that filter by explicit
/// channel ids and expect NO ambient tenant. A controller serving them without
/// <see cref="PlatformPlaneAttribute"/> gets the operator's active channel resolved as the tenant, and the
/// query filter then silently narrows its reads (and its writes' lookups) to that one channel. That shipped
/// several times, one controller at a time, so coverage is discovered from the routes rather than listed.
/// </summary>
public sealed class PlatformPlaneCoverageTests
{
    private const string AdminPrefix = "api/v{version:apiVersion}/admin";
    private const string PlatformPrefix = "api/v{version:apiVersion}/platform/";

    private static IReadOnlyList<Type> AdminPlaneControllers() =>
        typeof(AdminController)
            .Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t =>
                t.GetCustomAttribute<RouteAttribute>()?.Template is { } template
                && (
                    template.StartsWith(AdminPrefix, StringComparison.Ordinal)
                    || template.StartsWith(PlatformPrefix, StringComparison.Ordinal)
                )
            )
            .ToList();

    [Fact]
    public void Every_admin_and_platform_routed_controller_is_marked_platform_plane()
    {
        IReadOnlyList<Type> controllers = AdminPlaneControllers();

        controllers
            .Select(t => t.Name)
            .Should()
            .Contain(
                ["AdminController", "PlatformAdminController", "FeatureFlagAdminController"],
                "the discovery must actually find the admin controllers, or the check below proves nothing"
            );
        controllers
            .Where(t => t.GetCustomAttribute<PlatformPlaneAttribute>() is null)
            .Select(t => t.Name)
            .Should()
            .BeEmpty(
                "an admin controller without [PlatformPlane] narrows its cross-tenant reads to one channel"
            );
    }
}
