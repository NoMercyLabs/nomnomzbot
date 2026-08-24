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
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using NomNomzBot.Api.HealthChecks;

namespace NomNomzBot.Api.Tests.HealthChecks;

/// <summary>
/// Caddy-took-us-offline fix: a degraded background/optional subsystem (EventSub) must never remove this
/// instance from a reverse proxy's pool via <c>/health/ready</c> — readiness means "can serve HTTP requests
/// correctly" (a usable database), not "every optional integration is fully healthy". This wires the exact
/// production check/tag/endpoint shapes <c>Program.cs</c> builds (a "db" check tagged <c>ready</c>, and an
/// "eventsub" check NOT tagged <c>ready</c>) over a real <see cref="TestServer"/>, so the assertions exercise
/// the actual HTTP status codes an orchestrator or reverse proxy would observe.
/// </summary>
public sealed class ReadinessTagScopeIntegrationTests : IAsyncDisposable
{
    private IHost? _host;
    private static bool s_dbHealthy = true;
    private static HealthStatus s_eventSubStatus = HealthStatus.Healthy;

    private async Task<HttpClient> StartAsync()
    {
        IHostBuilder builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services
                        .AddHealthChecks()
                        // Mirrors Program.cs: db check IS tagged ready (a genuine serving prerequisite).
                        .AddCheck(
                            "db",
                            () =>
                                s_dbHealthy
                                    ? HealthCheckResult.Healthy()
                                    : HealthCheckResult.Unhealthy("database unreachable"),
                            tags: ["db", "ready"]
                        )
                        // Mirrors the fix: eventsub is reported but NOT tagged ready — a background
                        // integration's degradation must stay visible without pulling this instance out
                        // of the load-balancer pool.
                        .AddCheck(
                            "eventsub",
                            () => new HealthCheckResult(s_eventSubStatus, "eventsub status"),
                            tags: ["eventsub"]
                        );
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapHealthChecks(
                            "/health/ready",
                            new()
                            {
                                Predicate = check => check.Tags.Contains("ready"),
                                ResultStatusCodes = new Dictionary<HealthStatus, int>(
                                    ReadinessStatusCodeMap.Value
                                ),
                            }
                        );
                        endpoints.MapHealthChecks(
                            "/health",
                            new()
                            {
                                ResultStatusCodes =
                                {
                                    [HealthStatus.Healthy] = StatusCodes.Status200OK,
                                    [HealthStatus.Degraded] = StatusCodes.Status200OK,
                                    [HealthStatus.Unhealthy] =
                                        StatusCodes.Status503ServiceUnavailable,
                                },
                                ResponseWriter = async (context, report) =>
                                {
                                    context.Response.ContentType = "application/json";
                                    await context.Response.WriteAsJsonAsync(
                                        new
                                        {
                                            status = report.Status.ToString().ToLowerInvariant(),
                                            checks = report.Entries.Select(e => new
                                            {
                                                name = e.Key,
                                                status = e
                                                    .Value.Status.ToString()
                                                    .ToLowerInvariant(),
                                            }),
                                        }
                                    );
                                },
                            }
                        );
                    });
                });
        });

        IHost host = await builder.StartAsync();
        _host = host;
        return host.GetTestClient();
    }

    [Fact]
    public async Task DegradedEventSub_LeavesReadyHealthy()
    {
        s_dbHealthy = true;
        s_eventSubStatus = HealthStatus.Degraded;
        HttpClient client = await StartAsync();

        HttpResponseMessage ready = await client.GetAsync("/health/ready");

        ready
            .StatusCode.Should()
            .Be(
                System.Net.HttpStatusCode.OK,
                "a degraded background integration must not remove the instance from the proxy pool"
            );
    }

    [Fact]
    public async Task UnhealthyEventSub_LeavesReadyHealthy()
    {
        s_dbHealthy = true;
        s_eventSubStatus = HealthStatus.Unhealthy;
        HttpClient client = await StartAsync();

        HttpResponseMessage ready = await client.GetAsync("/health/ready");

        ready.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task DegradedEventSub_StillSurfacesOnDetailHealthEndpoint()
    {
        s_dbHealthy = true;
        s_eventSubStatus = HealthStatus.Degraded;
        HttpClient client = await StartAsync();

        HttpResponseMessage health = await client.GetAsync("/health");
        string body = await health.Content.ReadAsStringAsync();

        body.Should()
            .Contain(
                "\"eventsub\"",
                "the degradation must stay visible on the detail endpoint even though it no longer affects readiness"
            );
        body.Should().Contain("\"degraded\"");
    }

    [Fact]
    public async Task UnavailableDatabase_FailsReady()
    {
        s_dbHealthy = false;
        s_eventSubStatus = HealthStatus.Healthy;
        HttpClient client = await StartAsync();

        HttpResponseMessage ready = await client.GetAsync("/health/ready");

        ready
            .StatusCode.Should()
            .Be(
                System.Net.HttpStatusCode.ServiceUnavailable,
                "the database is a genuine serving prerequisite and must still fail readiness"
            );
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }
    }
}
