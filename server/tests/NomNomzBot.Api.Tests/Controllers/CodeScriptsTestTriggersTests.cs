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
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Platform.Services;
using NomNomzBot.Infrastructure;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The editor's trigger picker needs the list of samples a script test run can fire. The list is the real
/// registrations (one per alert handler), sorted, gated like the other code-script routes.
/// </summary>
public sealed class CodeScriptsTestTriggersTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private static (CodeScriptsController Controller, int Registered) Build(bool featureEnabled)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
                    ["Jwt:Secret"] = "test-secret-key-at-least-32-characters-long!!",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Database=test_triggers;Username=test;Password=test",
                }
            )
            .Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = false, ValidateScopes = false }
        );
        IServiceScope scope = provider.CreateScope();

        IFeatureService features = Substitute.For<IFeatureService>();
        features
            .IsFeatureEnabledAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(featureEnabled);
        ICurrentTenantService tenant = Substitute.For<ICurrentTenantService>();
        tenant.BroadcasterId.Returns(Guid.NewGuid());

        CodeScriptsController controller = new(
            Substitute.For<ICodeScriptService>(),
            Substitute.For<IScriptTestRunService>(),
            scope.ServiceProvider.GetRequiredService<ITriggerSampleCatalog>(),
            features,
            tenant
        );
        return (controller, scope.ServiceProvider.GetServices<ITriggerSampleSource>().Count());
    }

    [Fact]
    public async Task The_list_holds_every_registered_sample_sorted_with_a_follow_carrying_user()
    {
        (CodeScriptsController controller, int registered) = Build(featureEnabled: true);

        IActionResult result = await controller.ListTestTriggers(default);

        IReadOnlyList<TestTriggerDto> triggers = result
            .Should()
            .BeOfType<OkObjectResult>()
            .Which.Value.Should()
            .BeOfType<StatusResponseDto<IReadOnlyList<TestTriggerDto>>>()
            .Which.Data!;
        registered.Should().BeGreaterThan(20);
        triggers.Should().HaveCount(registered);
        triggers.Select(t => t.ResponseKey).Should().BeInAscendingOrder(StringComparer.Ordinal);
        TestTriggerDto follow = triggers.Single(t => t.Id == "FollowEvent");
        follow.ResponseKey.Should().Be("channel.follow");
        follow.Variables["user"].Should().Be(follow.UserDisplayName);
        follow.Variables["followed_at"].Should().Be(Now.ToString("O"));
    }

    [Fact]
    public async Task A_channel_without_custom_code_gets_no_sample_list()
    {
        (CodeScriptsController controller, _) = Build(featureEnabled: false);

        IActionResult result = await controller.ListTestTriggers(default);

        result.Should().NotBeOfType<OkObjectResult>();
    }
}
