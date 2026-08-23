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
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NomNomzBot.Api.Controllers;
using Xunit;

namespace NomNomzBot.Api.Tests.Configuration;

/// <summary>
/// Every controller in the API assembly must carry <c>[ApiController]</c> and be covered by an
/// effective rate-limit policy (own or inherited <c>[EnableRateLimiting]</c>, not overridden by
/// <c>[DisableRateLimiting]</c>). A controller that slips through <see cref="BaseController"/>
/// without these attributes ships with no request-size validation and no throttling — S098a.
/// </summary>
public sealed class ControllerSecurityBaselineTests
{
    [Fact]
    public void Every_controller_has_ApiController_and_an_effective_rate_limit_policy()
    {
        List<Type> controllerTypes = typeof(BaseController)
            .Assembly.GetTypes()
            .Where(type =>
                type is { IsClass: true, IsAbstract: false }
                && typeof(ControllerBase).IsAssignableFrom(type)
            )
            .ToList();

        Assert.NotEmpty(controllerTypes);

        List<string> missingApiController = controllerTypes
            .Where(type => !HasAttributeOnTypeOrBase<ApiControllerAttribute>(type))
            .Select(type => type.FullName ?? type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        List<string> missingRateLimit = controllerTypes
            .Where(type => !HasEffectiveRateLimitPolicy(type))
            .Select(type => type.FullName ?? type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        bool allPass = missingApiController.Count == 0 && missingRateLimit.Count == 0;

        Assert.True(
            allPass,
            "Controllers missing [ApiController]: "
                + (
                    missingApiController.Count == 0
                        ? "none"
                        : string.Join(", ", missingApiController)
                )
                + " | Controllers missing an effective rate-limit policy: "
                + (missingRateLimit.Count == 0 ? "none" : string.Join(", ", missingRateLimit))
        );
    }

    private static bool HasAttributeOnTypeOrBase<TAttribute>(Type type)
        where TAttribute : Attribute
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            if (current.IsDefined(typeof(TAttribute), inherit: false))
                return true;
        }

        return false;
    }

    private static bool HasEffectiveRateLimitPolicy(Type type)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            if (current.IsDefined(typeof(DisableRateLimitingAttribute), inherit: false))
                return false;

            if (current.IsDefined(typeof(EnableRateLimitingAttribute), inherit: false))
                return true;
        }

        return false;
    }
}
