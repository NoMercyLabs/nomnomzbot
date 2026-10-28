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

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Api.Controllers;
using NomNomzBot.Api.Extensions;

namespace NomNomzBot.Api.Tests.Hosting;

/// <summary>
/// The endpoints the deployed API serves. Built by the production controller setup
/// (<see cref="ApiControllerExtensions.AddApiControllers"/>, which registers the conventions) and
/// <c>MapControllers</c>, so the metadata on each endpoint is what the request pipeline reads at runtime.
/// </summary>
public static class ApiEndpointSurface
{
    private static readonly Lazy<IReadOnlyList<RouteEndpoint>> Built = new(Build);

    public static IReadOnlyList<RouteEndpoint> All => Built.Value;

    private static IReadOnlyList<RouteEndpoint> Build()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddApiControllers().AddApplicationPart(typeof(BaseController).Assembly);

        WebApplication app = builder.Build();
        app.MapControllers();

        List<RouteEndpoint> endpoints =
        [
            .. new CompositeEndpointDataSource(
                ((IEndpointRouteBuilder)app).DataSources
            ).Endpoints.OfType<RouteEndpoint>(),
        ];
        app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return endpoints;
    }
}
