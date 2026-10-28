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

using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// S-BYOC-spotify-a2 — commit 1d74b4fb made <see cref="IChannelCredentialsResolver"/> a REQUIRED
/// constructor parameter of <see cref="SpotifyMusicProvider"/> (it was previously optional/defaulted —
/// a silent-failure hazard: an unpopulated resolver would quietly fall back to app-level Spotify
/// credentials, so a BYOC streamer's connection would authorize fine and then have song requests
/// silently stop working on refresh). Compile-time enforcement (no default parameter) only proves the
/// SHAPE is right; this test proves the REAL composition root actually wires it, by building the exact
/// same DI graph <c>Program.cs</c> builds (<c>AddApplication()</c> + <c>AddInfrastructure(configuration)</c>,
/// <c>ValidateOnBuild</c> + <c>ValidateScopes</c> — same pattern as
/// <see cref="Platform.AssemblyScanDiscoveryTests"/>), resolving <see cref="SpotifyMusicProvider"/> through
/// its registered <see cref="IMusicProvider"/> marker interface exactly as the assembly scan does, and
/// reading the private field by reflection to assert it holds the container's own singleton
/// <see cref="IChannelCredentialsResolver"/> instance — not null, not a default, not a copy.
/// </summary>
public sealed class SpotifyMusicProviderCompositionTests
{
    private static ServiceProvider BuildProvider()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
                    ["Jwt:Secret"] = "test-secret-key-at-least-32-characters-long!!",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Database=spotify_composition_test;Username=test;Password=test",
                }
            )
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddApplication();
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );
    }

    [Fact]
    public void RealContainer_resolves_SpotifyMusicProvider_with_the_container_own_ChannelCredentialsResolver()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();

        // Same resolution path production and the §4 assembly-scan discovery test use: through the
        // IMusicProvider marker, never `new`-ing the concrete type — proves the CONTAINER's wiring, not a
        // hand-built instance.
        SpotifyMusicProvider spotify = scope
            .ServiceProvider.GetServices<IMusicProvider>()
            .OfType<SpotifyMusicProvider>()
            .Should()
            .ContainSingle("the assembly scan must register exactly one SpotifyMusicProvider")
            .Subject;

        IChannelCredentialsResolver containerResolver =
            provider.GetRequiredService<IChannelCredentialsResolver>();

        FieldInfo? field = typeof(SpotifyMusicProvider).GetField(
            "_channelCredentials",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        field
            .Should()
            .NotBeNull("SpotifyMusicProvider must keep its resolver in this backing field");

        object? wiredResolver = field.GetValue(spotify);

        wiredResolver
            .Should()
            .NotBeNull(
                "the resolver is a required constructor parameter — the provider must never be "
                    + "constructible, by the real container or anyone else, without one"
            );
        wiredResolver
            .Should()
            .BeSameAs(
                containerResolver,
                "the container registers IChannelCredentialsResolver as a singleton (S-BYOC-spotify-a) "
                    + "— SpotifyMusicProvider must receive that exact instance, not a default or a copy"
            );
    }
}
