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
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Domain.Music.Exceptions;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Music.Playback;
using NomNomzBot.Infrastructure.Platform.Security;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// The engine factory is a singleton, the providers and the YouTube dispatcher are scoped (they hold the
/// DbContext). So an engine opens one scope per command and disposes it after, and the factory holds no
/// scoped service between commands. The container here validates scopes, as the real one does.
/// </summary>
public sealed class PlaybackEngineScopingTests
{
    private static readonly Guid Channel = PlaybackDirectorTestKit.Channel;

    /// <summary>What the scoped doubles saw, shared by every scope.</summary>
    private sealed class Tally
    {
        public int ScopesOpened;
        public int ScopesDisposed;
        public bool SpotifyConnected = true;
        public bool YouTubePlayerOpen = true;
        public List<IMusicProvider> Spotify { get; } = [];
        public List<IMusicProvider> YouTube { get; } = [];
        public List<IYouTubePlayerDispatcher> Dispatchers { get; } = [];
    }

    private sealed class ScopeProbe : IDisposable
    {
        private readonly Tally _tally;

        public ScopeProbe(Tally tally)
        {
            _tally = tally;
            tally.ScopesOpened++;
        }

        public void Dispose() => _tally.ScopesDisposed++;
    }

    private readonly Tally _tally = new();
    private readonly IReadOnlyList<IPlaybackEngine> _engines;

    public PlaybackEngineScopingTests()
    {
        ServiceCollection services = new();
        services.AddSingleton(_tally);
        services.AddScoped<ScopeProbe>();
        services.AddSingleton(Substitute.For<IYouTubePlayerStateStore>());
        services.AddSingleton<IOutboundSanctionAccessor, OutboundSanctionAccessor>();
        services.AddScoped(SpotifyProvider);
        services.AddScoped(YouTubeProvider);
        services.AddScoped(Dispatcher);
        services.AddSingleton<IPlaybackEngineFactory, PlaybackEngineFactory>();

        ServiceProvider root = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );
        _engines = root.GetRequiredService<IPlaybackEngineFactory>().Create(Channel);
    }

    private IMusicProvider SpotifyProvider(IServiceProvider scope)
    {
        scope.GetRequiredService<ScopeProbe>();
        IMusicProvider provider = Substitute.For<
            IMusicProvider,
            IMusicProviderStartTrack,
            IMusicProviderConnectionGate
        >();
        provider.Provider.Returns("spotify");
        provider.Capabilities.Returns(MusicProviderCapabilities.Seek);
        ((IMusicProviderConnectionGate)provider)
            .IsConnectedAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(_ => _tally.SpotifyConnected);
        ((IMusicProviderStartTrack)provider)
            .StartTrackAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _tally.Spotify.Add(provider);

        return provider;
    }

    private IMusicProvider YouTubeProvider(IServiceProvider scope)
    {
        scope.GetRequiredService<ScopeProbe>();
        IMusicProvider provider = Substitute.For<
            IMusicProvider,
            IMusicProviderPlayNow,
            IMusicProviderPlayerGate
        >();
        provider.Provider.Returns("youtube");
        ((IMusicProviderPlayerGate)provider)
            .HasPlayerAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(_ => _tally.YouTubePlayerOpen);
        _tally.YouTube.Add(provider);

        return provider;
    }

    private IYouTubePlayerDispatcher Dispatcher(IServiceProvider scope)
    {
        scope.GetRequiredService<ScopeProbe>();
        IYouTubePlayerDispatcher dispatcher = Substitute.For<IYouTubePlayerDispatcher>();
        _tally.Dispatchers.Add(dispatcher);

        return dispatcher;
    }

    private IPlaybackEngine Engine(EngineKind kind) => _engines.Single(e => e.Kind == kind);

    [Fact]
    public void A_singleton_factory_over_scoped_providers_builds_and_opens_no_scope_by_itself()
    {
        _engines
            .Select(e => e.Kind)
            .Should()
            .BeEquivalentTo([EngineKind.Spotify, EngineKind.YouTube]);
        _tally.ScopesOpened.Should().Be(0);
        _tally.Spotify.Should().BeEmpty();
        _tally.Dispatchers.Should().BeEmpty();
    }

    [Fact]
    public async Task Each_spotify_command_runs_in_its_own_scope_and_disposes_it()
    {
        await Engine(EngineKind.Spotify).PauseAsync();
        await Engine(EngineKind.Spotify).PauseAsync();

        _tally.ScopesOpened.Should().Be(2);
        _tally.ScopesDisposed.Should().Be(2);
        _tally.Spotify.Should().HaveCount(2);
        _tally.Spotify[0].Should().NotBeSameAs(_tally.Spotify[1]);
        foreach (IMusicProvider provider in _tally.Spotify)
            await provider.Received(1).PauseAsync(Channel, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_spotify_start_goes_through_the_provider_of_its_own_scope()
    {
        PlaybackItem item = PlaybackDirectorTestKit.Item("a1");

        EngineStartResult result = await Engine(EngineKind.Spotify).StartAsync(item);

        result.Should().Be(EngineStartResult.Ok);
        _tally.ScopesDisposed.Should().Be(_tally.ScopesOpened).And.Be(1);
        await ((IMusicProviderStartTrack)_tally.Spotify.Single())
            .Received(1)
            .StartTrackAsync(Channel, item.Uri, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_youtube_stop_goes_through_the_dispatcher_of_its_own_scope()
    {
        await Engine(EngineKind.YouTube).StopAsync();

        _tally.ScopesDisposed.Should().Be(_tally.ScopesOpened).And.Be(1);
        await _tally
            .Dispatchers.Single()
            .Received(1)
            .StopAsync(Channel, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_blocked_command_still_disposes_its_scope_and_sends_nothing()
    {
        _tally.SpotifyConnected = false;

        Func<Task> pause = () => Engine(EngineKind.Spotify).PauseAsync();

        (await pause.Should().ThrowAsync<PlaybackEngineBlockedException>())
            .Which.Reason.Should()
            .Be(EngineBlockReason.NotConnected);
        _tally.ScopesDisposed.Should().Be(_tally.ScopesOpened).And.Be(1);
        await _tally.Spotify.Single().DidNotReceiveWithAnyArgs().PauseAsync(default);
    }

    [Fact]
    public async Task A_refused_start_disposes_its_scope_when_the_player_is_missing()
    {
        _tally.YouTubePlayerOpen = false;

        EngineStartResult result = await Engine(EngineKind.YouTube)
            .StartAsync(PlaybackDirectorTestKit.Item("y1", EngineKind.YouTube));

        result.Block.Should().Be(EngineBlockReason.NoOutput);
        _tally.ScopesDisposed.Should().Be(_tally.ScopesOpened).And.Be(1);
    }

    [Fact]
    public async Task A_provider_control_runs_in_a_scope_that_is_disposed_with_it()
    {
        EngineControlResult result = await (
            (IPlaybackEngineProviderControls)Engine(EngineKind.Spotify)
        ).SetShuffleAsync(true);

        result.Should().Be(EngineControlResult.Done);
        _tally.ScopesDisposed.Should().Be(_tally.ScopesOpened).And.Be(1);
        await _tally
            .Spotify.Single()
            .Received(1)
            .SetShuffleAsync(Channel, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Capabilities_are_read_inside_a_scope_that_is_disposed_after()
    {
        MusicProviderCapabilities capabilities = (
            (IPlaybackEngineProviderControls)Engine(EngineKind.Spotify)
        ).Capabilities;

        capabilities.Should().Be(MusicProviderCapabilities.Seek);
        _tally.ScopesDisposed.Should().Be(_tally.ScopesOpened).And.Be(1);
    }
}
