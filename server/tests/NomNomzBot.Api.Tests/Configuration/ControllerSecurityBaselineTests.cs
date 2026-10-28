// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.RateLimiting;
using NomNomzBot.Api.Controllers;
using NomNomzBot.Api.Tests.Hosting;

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
        List<Type> controllerTypes =
        [
            .. typeof(BaseController)
                .Assembly.GetTypes()
                .Where(type =>
                    type is { IsClass: true, IsAbstract: false }
                    && typeof(ControllerBase).IsAssignableFrom(type)
                ),
        ];

        Assert.NotEmpty(controllerTypes);

        List<string> missingApiController =
        [
            .. controllerTypes
                .Where(type => !HasAttributeOnTypeOrBase<ApiControllerAttribute>(type))
                .Select(type => type.FullName ?? type.Name)
                .OrderBy(name => name, StringComparer.Ordinal),
        ];

        List<string> missingRateLimit =
        [
            .. ApiEndpointSurface
                .All.Where(endpoint =>
                    endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>() is null
                    || endpoint.Metadata.GetMetadata<DisableRateLimitingAttribute>() is not null
                )
                .Select(endpoint =>
                    endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!.ControllerTypeInfo
                )
                .Distinct()
                .Select(type => type.FullName ?? type.Name)
                .OrderBy(name => name, StringComparer.Ordinal),
        ];

        // A controller with no routed endpoint would pass the rate-limit check above without being checked.
        List<string> unrouted =
        [
            .. controllerTypes
                .Where(type =>
                    !ApiEndpointSurface.All.Any(endpoint =>
                        endpoint
                            .Metadata.GetMetadata<ControllerActionDescriptor>()!
                            .ControllerTypeInfo == type
                    )
                )
                .Select(type => type.FullName ?? type.Name)
                .OrderBy(name => name, StringComparer.Ordinal),
        ];

        bool allPass =
            missingApiController.Count == 0 && missingRateLimit.Count == 0 && unrouted.Count == 0;

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
                + " | Controllers with no routed endpoint: "
                + (unrouted.Count == 0 ? "none" : string.Join(", ", unrouted))
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
}
