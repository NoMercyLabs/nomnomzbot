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
/// Z4 — proves the zero-downtime-deploy drain sequence end to end over REAL HTTP against a REAL ASP.NET Core
/// host (<see cref="TestServer"/>), wiring the exact production pieces (<see cref="ShutdownReadinessHealthCheck"/>,
/// <see cref="ReadinessStatusCodeMap"/>, the <c>/health/ready</c> and <c>/health/live</c> endpoint shapes
/// <c>Program.cs</c> maps) rather than the full boot pipeline (DB migrations/seeding are unrelated to this
/// slice and would only make the test slower and more brittle). Triggering
/// <see cref="IHostApplicationLifetime.StopApplication"/> is the same signal a real deploy's SIGTERM produces.
/// </summary>
public sealed class ShutdownDrainIntegrationTests : IAsyncDisposable
{
    private readonly TaskCompletionSource _releaseInFlightRequest = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private readonly TaskCompletionSource _inFlightRequestStarted = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private IHost? _host;

    private async Task<(IHost Host, HttpClient Client)> StartAsync()
    {
        IHostBuilder builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton<ShutdownReadinessTracker>();
                    services
                        .AddHealthChecks()
                        .AddCheck<ShutdownReadinessHealthCheck>("shutdown", tags: ["ready"]);
                })
                .Configure(app =>
                {
                    app.ApplicationServices.GetRequiredService<ShutdownReadinessTracker>()
                        .Bind(
                            app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>()
                        );

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
                        endpoints.MapGet(
                            "/health/live",
                            () => Results.Ok(new { status = "alive" })
                        );

                        // Mirrors an already-routed, in-flight request: it must run to completion even
                        // though shutdown starts while it is in progress.
                        endpoints.MapGet(
                            "/slow-work",
                            async () =>
                            {
                                _inFlightRequestStarted.TrySetResult();
                                await _releaseInFlightRequest.Task;
                                return Results.Text("done", "text/plain");
                            }
                        );
                    });
                });
        });

        IHost host = await builder.StartAsync();
        return (host, host.GetTestClient());
    }

    [Fact]
    public async Task ReadyIsHealthy_AndLiveIsHealthy_BeforeShutdownBegins()
    {
        (IHost host, HttpClient client) = await StartAsync();
        _host = host;

        HttpResponseMessage ready = await client.GetAsync("/health/ready");
        HttpResponseMessage live = await client.GetAsync("/health/live");

        ready.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        live.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task ApplicationStopping_FailsReadyImmediately_WhileLiveStaysHealthy()
    {
        (IHost host, HttpClient client) = await StartAsync();
        _host = host;

        IHostApplicationLifetime lifetime =
            host.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.StopApplication();

        // ApplicationStopping is synchronous-ish (a CancellationTokenSource cancel) — no extra wait needed
        // before it has propagated to the tracker bound directly on that token.
        HttpResponseMessage ready = await client.GetAsync("/health/ready");
        HttpResponseMessage live = await client.GetAsync("/health/live");

        ready.StatusCode.Should().Be(System.Net.HttpStatusCode.ServiceUnavailable);
        live.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task ARequestAlreadyInFlightWhenShutdownBegins_StillCompletesSuccessfully()
    {
        (IHost host, HttpClient client) = await StartAsync();
        _host = host;

        // Start the "already-routed" request — it blocks until released, modeling in-flight work.
        Task<HttpResponseMessage> inFlight = client.GetAsync("/slow-work");
        await _inFlightRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Shutdown begins WHILE the request is in flight — readiness must fail immediately...
        IHostApplicationLifetime lifetime =
            host.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.StopApplication();
        (await client.GetAsync("/health/ready"))
            .StatusCode.Should()
            .Be(System.Net.HttpStatusCode.ServiceUnavailable);

        // ...but the in-flight request must still be allowed to finish with its real response, not be
        // aborted or cancelled by the readiness flip.
        _releaseInFlightRequest.TrySetResult();
        HttpResponseMessage response = await inFlight.WaitAsync(TimeSpan.FromSeconds(10));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("done");
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            _releaseInFlightRequest.TrySetResult();
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }
    }
}
