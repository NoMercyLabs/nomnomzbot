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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Music.Playback;
using NomNomzBot.Infrastructure.Platform.Security;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// The playback director sends engine commands on its own task, with no user action in flight. Every Spotify
/// write goes out through the REAL <see cref="OutboundSanctionHandler"/>, so a command that claims no basis
/// throws <see cref="UnsanctionedOutboundCallException"/> exactly as it does in production. The lease must
/// claim the channel's configured playback basis, in one place, for all nine engine writes.
/// </summary>
public sealed class PlaybackEngineSanctionTests
{
    private static readonly Guid Channel = PlaybackDirectorTestKit.Channel;

    private sealed class RecordingInner : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];
        public List<OutboundSanction?> Sanctions { get; } = [];
        public required IOutboundSanctionAccessor Accessor { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Calls.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            Sanctions.Add(Accessor.Current);

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));
        }
    }

    private readonly RecordingInner _inner;
    private readonly HttpClient _http;
    private readonly IPlaybackEngine _engine;

    public PlaybackEngineSanctionTests()
    {
        IOutboundSanctionAccessor accessor = new OutboundSanctionAccessor();
        _inner = new() { Accessor = accessor };
        _http = new(
            new OutboundSanctionHandler(accessor, NullLogger<OutboundSanctionHandler>.Instance)
            {
                InnerHandler = _inner,
            }
        )
        {
            BaseAddress = new("https://api.spotify.test/"),
        };

        ServiceCollection services = new();
        services.AddSingleton(accessor);
        services.AddSingleton(Substitute.For<IYouTubePlayerStateStore>());
        services.AddScoped(_ => SpotifyProvider());
        services.AddSingleton<IPlaybackEngineFactory, PlaybackEngineFactory>();

        ServiceProvider root = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );
        _engine = root.GetRequiredService<IPlaybackEngineFactory>()
            .Create(Channel)
            .Single(e => e.Kind == EngineKind.Spotify);
    }

    private Task Put(string path) => _http.PutAsync(path, null);

    private IMusicProvider SpotifyProvider()
    {
        IMusicProvider provider = Substitute.For<
            IMusicProvider,
            IMusicProviderStartTrack,
            IMusicProviderConnectionGate
        >();
        provider.Provider.Returns("spotify");
        ((IMusicProviderConnectionGate)provider)
            .IsConnectedAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(true);
        ((IMusicProviderStartTrack)provider)
            .StartTrackAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await Put("v1/me/player/play");

                return true;
            });
        provider
            .PauseAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(_ => Put("v1/me/player/pause"));
        provider
            .PlayAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(_ => Put("v1/me/player/play"));
        provider
            .SeekAsync(Channel, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => Put("v1/me/player/seek"));
        provider
            .SetVolumeAsync(Channel, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => Put("v1/me/player/volume"));
        provider
            .SetShuffleAsync(Channel, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ => Put("v1/me/player/shuffle"));
        provider
            .SetRepeatAsync(Channel, Arg.Any<MusicRepeatMode>(), Arg.Any<CancellationToken>())
            .Returns(_ => Put("v1/me/player/repeat"));
        provider
            .TransferPlaybackAsync(
                Channel,
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => Put("v1/me/player"));

        return provider;
    }

    private void AssertOneSanctionedCall(string call)
    {
        _inner.Calls.Should().Equal(call);
        _inner.Sanctions.Should().ContainSingle();
        _inner
            .Sanctions[0]
            .Should()
            .Be(OutboundSanction.ChannelConfiguration("music:playback_engine"));
    }

    [Fact]
    public async Task A_director_driven_start_claims_the_playback_basis()
    {
        EngineStartResult result = await _engine.StartAsync(PlaybackDirectorTestKit.Item("a1"));

        result.Should().Be(EngineStartResult.Ok);
        AssertOneSanctionedCall("PUT /v1/me/player/play");
    }

    [Fact]
    public async Task A_director_driven_pause_claims_the_playback_basis()
    {
        await _engine.PauseAsync();

        AssertOneSanctionedCall("PUT /v1/me/player/pause");
    }

    [Fact]
    public async Task A_director_driven_resume_claims_the_playback_basis()
    {
        await _engine.ResumeAsync();

        AssertOneSanctionedCall("PUT /v1/me/player/play");
    }

    [Fact]
    public async Task A_director_driven_stop_claims_the_playback_basis()
    {
        await _engine.StopAsync();

        AssertOneSanctionedCall("PUT /v1/me/player/pause");
    }

    [Fact]
    public async Task A_director_driven_seek_claims_the_playback_basis()
    {
        await _engine.SeekAsync(30_000);

        AssertOneSanctionedCall("PUT /v1/me/player/seek");
    }

    [Fact]
    public async Task A_director_driven_volume_claims_the_playback_basis()
    {
        await _engine.SetVolumeAsync(40);

        AssertOneSanctionedCall("PUT /v1/me/player/volume");
    }

    [Fact]
    public async Task A_director_driven_shuffle_claims_the_playback_basis()
    {
        EngineControlResult result = await (
            (IPlaybackEngineProviderControls)_engine
        ).SetShuffleAsync(true);

        result.Should().Be(EngineControlResult.Done);
        AssertOneSanctionedCall("PUT /v1/me/player/shuffle");
    }

    [Fact]
    public async Task A_director_driven_repeat_claims_the_playback_basis()
    {
        EngineControlResult result = await (
            (IPlaybackEngineProviderControls)_engine
        ).SetRepeatAsync(MusicRepeatMode.Track);

        result.Should().Be(EngineControlResult.Done);
        AssertOneSanctionedCall("PUT /v1/me/player/repeat");
    }

    [Fact]
    public async Task A_director_driven_transfer_claims_the_playback_basis()
    {
        EngineControlResult result = await ((IPlaybackEngineProviderControls)_engine).TransferAsync(
            "device-1",
            true
        );

        result.Should().Be(EngineControlResult.Done);
        AssertOneSanctionedCall("PUT /v1/me/player");
    }
}
