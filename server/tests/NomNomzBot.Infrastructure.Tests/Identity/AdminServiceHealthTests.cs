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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the admin health surface reports REAL probe outcomes (the registered health checks + the bot
/// token-readiness gate), never a canned "healthy" list: a failing database check and a dead bot token both
/// surface with their real statuses and degrade the overall verdict.
/// </summary>
public sealed class AdminServiceHealthTests
{
    private static AdminService Build(HealthStatus databaseStatus, bool botReady)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services
            .AddHealthChecks()
            .AddCheck(
                "database",
                () => new HealthCheckResult(databaseStatus, "probe result"),
                tags: ["db"]
            );
        ServiceProvider provider = services.BuildServiceProvider();

        IPlatformBotReadinessGate gate = Substitute.For<IPlatformBotReadinessGate>();
        gate.IsPlatformBotConfiguredAsync(Arg.Any<CancellationToken>()).Returns(botReady);

        return new AdminService(
            AuthTestBuilder.NewContext(),
            TimeProvider.System,
            provider.GetRequiredService<HealthCheckService>(),
            gate
        );
    }

    [Fact]
    public async Task Health_surfaces_a_failing_database_probe_and_degrades_the_overall_verdict()
    {
        AdminService sut = Build(HealthStatus.Unhealthy, botReady: true);

        Result<AdminSystemDto> result = await sut.GetSystemHealthAsync();

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Services.Single(s => s.Name == "database").Status.Should().Be("unhealthy");
        result.Value.Overall.Should().Be("unhealthy", "a failing probe must not report healthy");
    }

    [Fact]
    public async Task Health_reports_the_bot_degraded_when_its_token_does_not_resolve()
    {
        AdminService sut = Build(HealthStatus.Healthy, botReady: false);

        Result<AdminSystemDto> result = await sut.GetSystemHealthAsync();

        result.Value.Services.Single(s => s.Name == "bot").Status.Should().Be("degraded");
        result.Value.Overall.Should().Be("degraded");
    }

    [Fact]
    public async Task Health_is_healthy_when_every_real_probe_passes()
    {
        AdminService sut = Build(HealthStatus.Healthy, botReady: true);

        Result<AdminSystemDto> result = await sut.GetSystemHealthAsync();

        result.Value.Overall.Should().Be("healthy");
        result.Value.Services.Select(s => s.Name).Should().Contain(["api", "database", "bot"]);
        result.Value.Services.Should().OnlyContain(s => s.Status == "healthy");
    }
}
