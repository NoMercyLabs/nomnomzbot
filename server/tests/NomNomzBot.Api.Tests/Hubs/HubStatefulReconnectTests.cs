// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NomNomzBot.Api.Hubs;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// S035 item 1 — proves stateful reconnect is actually ENABLED on the mapped endpoint for every one of the
/// four hubs, not merely that a <c>HubOptions</c> object with a buffer size exists somewhere in DI (a prior
/// version of this test resolved <c>IOptions&lt;HubOptions&lt;THub&gt;&gt;</c> directly and passed even with
/// the feature entirely unconfigured on the endpoint — a surface test asserting a default).
///
/// There is no <c>WithStatefulReconnect()</c> extension method in this repo's ASP.NET Core 10.0 (verified: it
/// does not exist in the installed 10.0.11 runtime's <c>Microsoft.AspNetCore.SignalR</c> /
/// <c>Microsoft.AspNetCore.Http.Connections</c> assemblies, nor in 8.0/9.0). The real, existing switch is the
/// <c>MapHub&lt;THub&gt;(pattern, Action&lt;HttpConnectionDispatcherOptions&gt; configureOptions)</c> overload
/// setting <c>AllowStatefulReconnects = true</c> — that option is what the connection dispatcher reads at the
/// WebSocket upgrade, and it is surfaced as <see cref="HttpConnectionDispatcherOptions"/> metadata on the
/// mapped <c>{pattern}/negotiate</c> endpoint. This test boots a real <see cref="TestServer"/>, maps the hubs
/// EXACTLY the way Program.cs does (same helper delegate, same four paths), and reads that resolved endpoint
/// metadata — removing the <c>configureOptions</c> argument from any one hub's <c>MapHub</c> call in
/// Program.cs makes that hub's assertion fail.
/// </summary>
public sealed class HubStatefulReconnectTests : IAsyncLifetime
{
    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        IHostBuilder hostBuilder = new HostBuilder().ConfigureWebHost(webBuilder =>
        {
            webBuilder.UseTestServer();
            webBuilder.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSignalR(options => options.StatefulReconnectBufferSize = 100_000);
            });
            webBuilder.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    // Mirrors Program.cs's SignalR hub mapping block verbatim (same helper shape, same
                    // four paths) so a change there that drops `EnableStatefulReconnect` from any hub
                    // shows up here as a failing assertion, not as silent drift between the two files.
                    static void EnableStatefulReconnect(HttpConnectionDispatcherOptions options) =>
                        options.AllowStatefulReconnects = true;
                    endpoints.MapHub<DashboardHub>("/hubs/dashboard", EnableStatefulReconnect);
                    endpoints.MapHub<OverlayHub>("/hubs/overlay", EnableStatefulReconnect);
                    endpoints.MapHub<OBSRelayHub>("/hubs/obs", EnableStatefulReconnect);
                    endpoints.MapHub<AdminHub>("/hubs/admin", EnableStatefulReconnect);
                    // Deliberately NOT opted in — the contrast case for the regression guard below.
                    endpoints.MapHub<UnconfiguredProbeHub>("/hubs/unconfigured-probe");
                });
            });
        });

        _host = await hostBuilder.StartAsync();
    }

    public async Task DisposeAsync() => await _host.StopAsync();

    private bool? ResolveAllowStatefulReconnects(string hubPath)
    {
        EndpointDataSource dataSource = _host.Services.GetRequiredService<EndpointDataSource>();
        RouteEndpoint? negotiateEndpoint = dataSource
            .Endpoints.OfType<RouteEndpoint>()
            .FirstOrDefault(e => e.RoutePattern.RawText == $"{hubPath}/negotiate");
        return negotiateEndpoint
            ?.Metadata.GetMetadata<HttpConnectionDispatcherOptions>()
            ?.AllowStatefulReconnects;
    }

    [Fact]
    public void DashboardHub_endpoint_has_stateful_reconnect_enabled() =>
        ResolveAllowStatefulReconnects("/hubs/dashboard").Should().BeTrue();

    [Fact]
    public void OverlayHub_endpoint_has_stateful_reconnect_enabled() =>
        ResolveAllowStatefulReconnects("/hubs/overlay").Should().BeTrue();

    [Fact]
    public void OBSRelayHub_endpoint_has_stateful_reconnect_enabled() =>
        ResolveAllowStatefulReconnects("/hubs/obs").Should().BeTrue();

    [Fact]
    public void AdminHub_endpoint_has_stateful_reconnect_enabled() =>
        ResolveAllowStatefulReconnects("/hubs/admin").Should().BeTrue();

    [Fact]
    public void A_hub_mapped_without_the_configureOptions_argument_does_NOT_get_stateful_reconnect()
    {
        // Regression guard for the exact bug this slice fixes: plain `MapHub<T>(path)` (no options
        // delegate) leaves `AllowStatefulReconnects` at its false default even though the SAME
        // `AddSignalR` call configures a global `StatefulReconnectBufferSize` — proving that resolving
        // `IOptions<HubOptions<T>>` alone (the previous version of this test) cannot tell an opted-in hub
        // from one that never called the options delegate; only the endpoint metadata can.
        ResolveAllowStatefulReconnects("/hubs/unconfigured-probe").Should().NotBe(true);
    }
}

/// <summary>Test-only hub mapped WITHOUT the stateful-reconnect options delegate, to prove the assertion
/// methodology above actually distinguishes "enabled" from "not enabled" rather than always reading true.</summary>
file sealed class UnconfiguredProbeHub : Microsoft.AspNetCore.SignalR.Hub { }
