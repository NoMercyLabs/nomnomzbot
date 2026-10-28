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

using Microsoft.AspNetCore.Http.Connections;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Identifiers;

namespace NomNomzBot.Api.Extensions;

/// <summary>
/// The realtime surface every host of the API shares: the SignalR options and JSON protocol, and the hub
/// routes. <c>Program.cs</c> and the API test host both call these.
/// </summary>
public static class ApiSignalRExtensions
{
    public static IServiceCollection AddApiSignalR(
        this IServiceCollection services,
        bool enableDetailedErrors
    )
    {
        services
            .AddSignalR(options =>
            {
                options.EnableDetailedErrors = enableDetailedErrors;
                options.MaximumReceiveMessageSize = 128 * 1024;
                options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
                options.HandshakeTimeout = TimeSpan.FromSeconds(15);
                options.StatefulReconnectBufferSize = 100_000;
            })
            .AddJsonProtocol(options =>
                // Hub JSON must speak the same owned-id form as REST: the same converters, from the same list.
                OwnedIdJson.AddConverters(options.PayloadSerializerOptions)
            )
            // A widget runs its actions over this one socket (widget-sdk.md §8). One at a time, a widget that
            // synthesizes several speech lines would wait for each before the next even starts.
            .AddHubOptions<OverlayHub>(options => options.MaximumParallelInvocationsPerClient = 4);

        return services;
    }

    /// <summary>
    /// <c>AllowStatefulReconnects = true</c> is the real per-endpoint switch (there is no
    /// <c>WithStatefulReconnect()</c> extension in ASP.NET Core 10.0): <c>AddSignalR</c>'s global
    /// <c>StatefulReconnectBufferSize</c> configures the feature's buffer, but the connection dispatcher for
    /// each hub endpoint still defaults the option to false.
    /// </summary>
    public static IEndpointRouteBuilder MapApiHubs(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<DashboardHub>("/hubs/dashboard", EnableStatefulReconnect);
        endpoints.MapHub<OverlayHub>("/hubs/overlay", EnableStatefulReconnect);
        endpoints.MapHub<OBSRelayHub>("/hubs/obs", EnableStatefulReconnect);
        endpoints.MapHub<AdminHub>("/hubs/admin", EnableStatefulReconnect);
        return endpoints;
    }

    private static void EnableStatefulReconnect(HttpConnectionDispatcherOptions options) =>
        options.AllowStatefulReconnects = true;
}
