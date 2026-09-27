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
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Middleware;

namespace NomNomzBot.Api.Tests.Middleware;

/// <summary>
/// The admin console's cross-tenant pages (EventSub health, webhook deliveries, the job queue, usage, error
/// budgets, tenants, billing) read through services that filter by explicit channel ids and expect NO ambient
/// tenant. Every controller serving them must carry <see cref="PlatformPlaneAttribute"/>, or the operator's
/// active channel header silently narrows each page to one channel.
/// </summary>
public sealed class PlatformPlaneCoverageTests
{
    [Theory]
    [InlineData(typeof(AdminController))]
    [InlineData(typeof(PlatformAdminController))]
    [InlineData(typeof(AdminBillingController))]
    public void Cross_tenant_admin_controller_is_marked_platform_plane(Type controllerType)
    {
        controllerType
            .GetCustomAttribute<PlatformPlaneAttribute>()
            .Should()
            .NotBeNull(
                $"{controllerType.Name} serves cross-tenant admin reads and must not resolve an ambient tenant"
            );
    }
}
