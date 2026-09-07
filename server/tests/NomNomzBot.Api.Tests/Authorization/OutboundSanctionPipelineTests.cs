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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Infrastructure.Platform.Security;

namespace NomNomzBot.Api.Tests.Authorization;

/// <summary>
/// The sanction chain through a REAL host: routing, the MVC filter pipeline and DI, not a hand-built
/// <c>ActionExecutingContext</c>. This is the altitude that matters, because the gate fails CLOSED — if the
/// filter does not actually run in a hosted pipeline, the result is not a leak but an outage, and a
/// unit test of the filter in isolation cannot tell the difference.
/// </summary>
public sealed class OutboundSanctionPipelineTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton<
                            IOutboundSanctionAccessor,
                            OutboundSanctionAccessor
                        >();
                        services.AddSingleton<SanctionProbe>();
                        // Every action key resolves to a policy that succeeds: this test is about the
                        // sanction the filter produces, not about Gate-2's own decision.
                        services.AddSingleton<IAuthorizationPolicyProvider, AlwaysAllowPolicies>();
                        services.AddAuthorization();
                        services
                            .AddControllers(options =>
                                options.Filters.Add<OutboundSanctionFilter>()
                            )
                            .AddApplicationPart(typeof(SanctionProbeController).Assembly);
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthorization();
                        app.UseEndpoints(e => e.MapControllers());
                    })
            )
            .StartAsync();

        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task A_gated_endpoint_reaches_its_action_with_its_own_action_key_as_the_sanction()
    {
        HttpResponseMessage response = await _client.PostAsync("/probe/gated", null);

        response.EnsureSuccessStatusCode();
        SanctionProbe probe = _host.Services.GetRequiredService<SanctionProbe>();
        probe.Seen.Should().NotBeNull("otherwise every dashboard write is refused in production");
        probe.Seen!.Basis.Should().Be(OutboundSanctionBasis.UserAction);
        probe.Seen.Detail.Should().Be("probe:write");
    }

    [Fact]
    public async Task An_ungated_endpoint_reaches_its_action_with_no_sanction_at_all()
    {
        HttpResponseMessage response = await _client.PostAsync("/probe/ungated", null);

        response.EnsureSuccessStatusCode();
        _host
            .Services.GetRequiredService<SanctionProbe>()
            .Seen.Should()
            .BeNull(
                "an endpoint that proved nothing must not be able to change a third party's state"
            );
    }

    /// <summary>Records the sanction in force at the moment an action body runs.</summary>
    public sealed class SanctionProbe
    {
        public OutboundSanction? Seen { get; set; }
    }

    private sealed class AlwaysAllowPolicies : IAuthorizationPolicyProvider
    {
        private static readonly AuthorizationPolicy Allow = new AuthorizationPolicyBuilder()
            .RequireAssertion(_ => true)
            .Build();

        public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => Task.FromResult(Allow);

        public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() =>
            Task.FromResult<AuthorizationPolicy?>(null);

        public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) =>
            Task.FromResult<AuthorizationPolicy?>(Allow);
    }
}

[ApiController]
[Route("probe")]
public sealed class SanctionProbeController(
    IOutboundSanctionAccessor sanctions,
    OutboundSanctionPipelineTests.SanctionProbe probe
) : ControllerBase
{
    [RequireAction("probe:write")]
    [HttpPost("gated")]
    public IActionResult Gated()
    {
        probe.Seen = sanctions.Current;
        return NoContent();
    }

    [HttpPost("ungated")]
    public IActionResult Ungated()
    {
        probe.Seen = sanctions.Current;
        return NoContent();
    }
}
