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

using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Identifiers;
using NomNomzBot.Api.RateLimiting;

namespace NomNomzBot.Api.Extensions;

/// <summary>
/// The MVC surface every host of the API shares: controllers, the owned-id wire form, route constraints and
/// API versioning. <c>Program.cs</c> and the API test host both call it, so a test runs the same JSON options
/// and binders the deployed API does.
/// </summary>
public static class ApiControllerExtensions
{
    /// <summary>
    /// Owned identifiers are UUIDv7 Guids in storage but ULID strings on the wire (Identifiers/): the
    /// <see cref="UlidGuidJsonConverter"/> encodes/decodes every Guid in request and response bodies, and the
    /// model-binder provider (inserted ahead of the built-in simple-type binder) decodes a ULID-or-Guid
    /// route/query Guid. The guid route constraint is swapped so <c>{id:guid}</c> also matches a 26-char ULID.
    /// </summary>
    public static IMvcBuilder AddApiControllers(this IServiceCollection services)
    {
        IMvcBuilder mvc = services
            .AddControllers(options =>
            {
                options.ModelBinderProviders.Insert(0, new UlidGuidModelBinderProvider());
                options.Conventions.Add(new RateLimitReadTierConvention());
                // Gate-2 pass -> the sanction the Helix transport demands for any outbound write.
                options.Filters.Add<OutboundSanctionFilter>();
            })
            .AddJsonOptions(o => o.UseApiJsonConventions());

        // The built-in {x:guid} inline constraint would 404 a 26-char ULID before model binding.
        services.Configure<RouteOptions>(options =>
            options.ConstraintMap["guid"] = typeof(UlidOrGuidRouteConstraint)
        );

        services
            .AddApiVersioning(options =>
            {
                options.DefaultApiVersion = new(1, 0);
                options.AssumeDefaultVersionWhenUnspecified = true;
                options.ReportApiVersions = true;
            })
            .AddMvc();

        return mvc;
    }
}
