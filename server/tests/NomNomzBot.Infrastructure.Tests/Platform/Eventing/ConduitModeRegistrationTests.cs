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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing;

/// <summary>
/// Conduit mode is opt-in. Without the coordinator registered the EventSub host never tries a conduit and the
/// per-owner WebSocket sessions carry every topic — the configuration that kept chat alive after the first
/// conduit deploy left the bot deaf.
/// </summary>
public sealed class ConduitModeRegistrationTests
{
    private static ServiceProvider BuildProvider(string? conduitsEnabled)
    {
        Dictionary<string, string?> values = new()
        {
            ["Deployment:Mode"] = "SelfHostFull",
            ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
            ["Jwt:Secret"] = "test-secret-key-at-least-32-characters-long!!",
            ["ConnectionStrings:DefaultConnection"] =
                "Host=localhost;Database=conduit_mode_test;Username=test;Password=test",
        };
        if (conduitsEnabled is not null)
            values["EventSub:Conduits:Enabled"] = conduitsEnabled;

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddApplication();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void Without_the_opt_in_no_conduit_coordinator_exists(string? setting)
    {
        using ServiceProvider provider = BuildProvider(setting);

        provider.GetService<IEventSubConduitShardCoordinator>().Should().BeNull();
    }

    [Fact]
    public void The_opt_in_registers_the_conduit_coordinator()
    {
        using ServiceProvider provider = BuildProvider("true");

        provider.GetService<IEventSubConduitShardCoordinator>().Should().NotBeNull();
    }
}
