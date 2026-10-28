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

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Infrastructure.Games;

namespace NomNomzBot.Infrastructure.Tests.Games;

/// <summary>Every live-game overlay widget is served by a seed provider registered under its own natural key.</summary>
public sealed class LiveGameSeedRegistrationTests
{
    [Fact]
    public void The_crash_overlay_has_a_live_game_seed_provider_registered_under_its_key()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
                    ["Jwt:Secret"] = "test-secret-key-at-least-32-characters-long!!",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Database=seed_registration_test;Username=test;Password=test",
                }
            )
            .Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddApplication();
        services.AddInfrastructure(configuration);

        ServiceCollection factoryHost = new();
        factoryHost.AddSingleton(new LiveGameFrameStore());
        factoryHost.AddSingleton<TimeProvider>(new FakeTimeProvider());
        ServiceProvider provider = factoryHost.BuildServiceProvider();

        List<string> gameSeedKeys =
        [
            .. services
                .Where(d =>
                    d.ServiceType == typeof(IWidgetSeedProvider)
                    && d.ImplementationFactory is not null
                )
                .Select(d => d.ImplementationFactory!(provider))
                .OfType<IWidgetSeedProvider>()
                .Where(p => p.GetType().Name == nameof(LiveGameSeedProvider))
                .Select(p => p.NaturalKey),
        ];

        gameSeedKeys.Should().Contain("crash");
    }
}
