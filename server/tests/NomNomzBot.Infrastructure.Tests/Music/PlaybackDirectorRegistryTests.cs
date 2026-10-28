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
using NomNomzBot.Application;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Music.Playback;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// The input sink gives each channel one director over the real engines of the factory: an input starts
/// the head through the engine the item names, and a channel's engines are built once.
/// </summary>
public sealed class PlaybackDirectorRegistryTests
{
    private static readonly Guid Other = Guid.Parse("0192c000-0000-7000-8000-00000000d002");

    private readonly SongRequestQueueStore _store = new();
    private readonly FakeEngineFactory _engines = new();
    private readonly RecordingBus _bus = new();
    private readonly PlaybackDirectorRegistry _registry;

    public PlaybackDirectorRegistryTests() =>
        _registry = new(_store, _engines, _bus, TimeProvider.System);

    private void Request(Guid channel, string uri) =>
        _store
            .GetOrCreate(channel.ToString())
            .Enqueue("alice", new(uri, "Song", "Artist", null, 200_000, "alice", 0, null, "C-1"));

    [Fact]
    public async Task A_request_added_starts_the_head_through_the_engine_the_row_names()
    {
        Request(Channel, "spotify:track:s1");

        await _registry.SendAsync(Channel, new RequestAdded());

        FakeEngine spotify = _engines.EngineOf(Channel, EngineKind.Spotify);
        spotify.Calls.Should().ContainSingle(call => call.Name == "Start");
        _engines.EngineOf(Channel, EngineKind.YouTube).Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Engines_are_built_once_per_channel_and_not_shared_between_channels()
    {
        await _registry.SendAsync(Channel, new OwnerPause());
        await _registry.SendAsync(Channel, new OwnerPause());
        await _registry.SendAsync(Other, new OwnerPause());

        _engines.CreateCount.Should().Be(2);
        _engines
            .EngineOf(Channel, EngineKind.Spotify)
            .Should()
            .NotBeSameAs(_engines.EngineOf(Other, EngineKind.Spotify));
    }

    [Fact]
    public async Task A_report_through_the_report_sink_reaches_the_same_director()
    {
        Request(Channel, "spotify:track:s1");
        await _registry.SendAsync(Channel, new RequestAdded());
        FakeEngine spotify = _engines.EngineOf(Channel, EngineKind.Spotify);
        int starts = spotify.Calls.Count(call => call.Name == "Start");

        await _registry.ReportAsync(
            Channel,
            new EngineReport(EngineKind.Spotify, "spotify:track:s1", true, 1_000, 200_000)
        );

        _engines.CreateCount.Should().Be(1);
        spotify.Calls.Count(call => call.Name == "Start").Should().Be(starts);
    }

    [Fact]
    public async Task StateOf_reflects_Blocked_after_a_refused_start_and_Idle_for_an_unknown_channel()
    {
        IPlaybackStateReader reader = _registry;
        await _registry.SendAsync(Channel, new OwnerPause());
        _engines.EngineOf(Channel, EngineKind.Spotify).StartResult = EngineStartResult.Refused(
            "device gone"
        );
        Request(Channel, "spotify:track:s1");

        await _registry.SendAsync(Channel, new RequestAdded());

        reader.StateOf(Channel).Should().Be(PlaybackState.Blocked("device gone"));
        reader.StateOf(Other).Should().Be(PlaybackState.Idle);
        _engines.CreateCount.Should().Be(1);
    }

    [Fact]
    public void The_real_container_resolves_the_sinks_and_the_factory_without_a_scope_error()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
                    ["Jwt:Secret"] = "test-secret-key-at-least-32-characters-long!!",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Database=playback_sink_test;Username=test;Password=test",
                }
            )
            .Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddApplication();
        services.AddInfrastructure(configuration);

        using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );

        IPlaybackInputSink input = provider.GetRequiredService<IPlaybackInputSink>();
        IPlaybackReportSink report = provider.GetRequiredService<IPlaybackReportSink>();
        IPlaybackEngineFactory factory = provider.GetRequiredService<IPlaybackEngineFactory>();

        input.Should().BeSameAs(report);
        factory
            .Create(Channel)
            .Select(engine => engine.Kind)
            .Should()
            .Equal(EngineKind.Spotify, EngineKind.YouTube);
    }
}
