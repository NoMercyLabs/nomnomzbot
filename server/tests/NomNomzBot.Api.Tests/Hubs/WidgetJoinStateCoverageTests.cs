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

using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Application;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Infrastructure;
using NomNomzBot.Infrastructure.Content.Widgets;
using NomNomzBot.Infrastructure.Games;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// An overlay ALWAYS gets the channel's current state when it loads (Stoney, 2026-10-26): a browser source that
/// reloads mid-stream must never sit empty until the next event. Every first-party widget either has a seed
/// provider (its state rides on the join), reads its own state on load, or holds no state at all.
/// </summary>
public sealed class WidgetJoinStateCoverageTests
{
    // Pages that read their own state over the SDK data calls the moment they load.
    private static readonly HashSet<string> ReadsOwnState =
    [
        "music_player",
        "now_playing",
        "raid_recap",
    ];

    // Pages that only play a one-time moment (an alert, a sound) or show their saved settings: nothing to restore.
    private static readonly HashSet<string> HoldsNoState =
    [
        "alerts",
        "audio",
        "emote_wall",
        "redemption_alert",
        "tts_caption",
        "stage",
        "countdown_timer",
        "socials",
    ];

    [Fact]
    public void Every_first_party_widget_gets_its_current_state_when_it_loads()
    {
        HashSet<string> seeded = SeedProviderKeys();

        List<string> missing =
        [
            .. FirstPartyWidgetCatalogue
                .All.Select(w => w.Key)
                .Where(key =>
                    !seeded.Contains(key)
                    && !ReadsOwnState.Contains(key)
                    && !HoldsNoState.Contains(key)
                ),
        ];

        missing
            .Should()
            .BeEmpty(
                "a widget that reloads must show the live state at once, so it needs an IWidgetSeedProvider"
            );
    }

    private static HashSet<string> SeedProviderKeys()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
                    ["Jwt:Secret"] = "test-secret-key-at-least-32-characters-long!!",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Database=seed_coverage_test;Username=test;Password=test",
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

        HashSet<string> keys = [];
        foreach (
            ServiceDescriptor descriptor in services.Where(d =>
                d.ServiceType == typeof(IWidgetSeedProvider)
            )
        )
        {
            // A provider's key is a constant, so an instance built without its dependencies can still name it.
            IWidgetSeedProvider seed = descriptor.ImplementationFactory is not null
                ? (IWidgetSeedProvider)descriptor.ImplementationFactory(provider)
                : (IWidgetSeedProvider)
                    RuntimeHelpers.GetUninitializedObject(descriptor.ImplementationType!);
            keys.Add(seed.NaturalKey);
        }

        // The Api registers its own providers (those that render the hub's DTOs) in Program.cs.
        foreach (
            Type type in typeof(OverlayHub)
                .Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(IWidgetSeedProvider).IsAssignableFrom(t))
        )
            keys.Add(((IWidgetSeedProvider)RuntimeHelpers.GetUninitializedObject(type)).NaturalKey);
        return keys;
    }
}
