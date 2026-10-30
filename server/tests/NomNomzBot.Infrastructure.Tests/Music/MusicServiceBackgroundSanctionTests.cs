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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Every remaining <see cref="MusicService"/> provider-write method that a background caller can reach
/// with no HTTP request behind it — a pipeline action (music-automation-controls.md), a chat builtin, or
/// (concretely, <see cref="PlayOnceResumeHandler"/>) a reaction to a poller-published
/// <c>PlaybackStateChangedEvent</c> — needs its own <see cref="OutboundSanction"/> for the same reason
/// <see cref="MusicService.EnqueueResolvedAsync"/> already does: <see cref="OutboundSanctionHandler"/> silently refuses any outbound provider write with no ambient
/// sanction, and none of these entry points inherit one of their own. These tests pin, per method, that the
/// provider write is observed happening under <see cref="OutboundSanctionBasis.ChannelConfiguration"/> with
/// the method's own <c>Detail</c> string — not merely that the call succeeds.
/// </summary>
public sealed class MusicServiceBackgroundSanctionTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-00000000ab02");

    private const MusicProviderCapabilities AllControlCapabilities =
        MusicProviderCapabilities.PlaybackControl
        | MusicProviderCapabilities.Volume
        | MusicProviderCapabilities.Skip
        | MusicProviderCapabilities.Seek
        | MusicProviderCapabilities.Previous
        | MusicProviderCapabilities.Shuffle
        | MusicProviderCapabilities.Repeat
        | MusicProviderCapabilities.TransferDevice
        | MusicProviderCapabilities.NowPlaying
        | MusicProviderCapabilities.Queue;

    private static void SeedConnectedSpotify(MusicTestDbContext db) =>
        db.Services.Add(
            new()
            {
                Id = Guid.NewGuid().ToString(),
                Name = "spotify",
                BroadcasterId = ChannelId,
                Enabled = true,
                AccessToken = "test-access-token",
            }
        );

    private static (
        MusicService Service,
        OutboundSanctionAccessor Sanctions,
        IMusicProvider Provider
    ) BuildSut(IPlaybackInputSink? input = null)
    {
        (MusicService service, OutboundSanctionAccessor sanctions, IMusicProvider provider, _, _) =
            BuildCore(input, wrap: null);
        return (service, sanctions, provider);
    }

    private sealed record OwnerMusicSut(
        MusicService Service,
        OutboundSanctionAccessor Sanctions,
        IMusicProvider Provider,
        SanctionProbeSink Probe,
        DirectorRig Rig
    );

    /// <summary>Records the sanction in force, and the input, at the moment a control reaches the director.</summary>
    private sealed class SanctionProbeSink(
        IPlaybackInputSink inner,
        OutboundSanctionAccessor sanctions
    ) : IPlaybackInputSink
    {
        public OutboundSanction? Observed { get; private set; }

        public PlaybackInput? Sent { get; private set; }

        public Task<PlaybackInputOutcome> SendAsync(
            Guid broadcasterId,
            PlaybackInput input,
            CancellationToken ct = default
        )
        {
            Observed = sanctions.Current;
            Sent = input;
            return inner.SendAsync(broadcasterId, input, ct);
        }
    }

    /// <summary>A service over a real director that already plays the owner's Spotify music, optionally paused.</summary>
    private static async Task<OwnerMusicSut> BuildOwnerMusicAsync(bool paused)
    {
        (
            MusicService service,
            OutboundSanctionAccessor sanctions,
            IMusicProvider provider,
            SanctionProbeSink? probe,
            DirectorRig rig
        ) = BuildCore(
            input: null,
            wrap: (inner, accessor) => new SanctionProbeSink(inner, accessor)
        );
        await rig.Registry.SendAsync(ChannelId, new OutputAttached());
        await rig
            .Engines.EngineOf(ChannelId, EngineKind.Spotify)
            .EmitAsync(
                new EngineReport(EngineKind.Spotify, "spotify:track:x", true, 1_000, 200_000)
            );
        if (paused)
            await rig.Registry.SendAsync(ChannelId, new OwnerPause());

        return new(service, sanctions, provider, probe!, rig);
    }

    private static (
        MusicService Service,
        OutboundSanctionAccessor Sanctions,
        IMusicProvider Provider,
        SanctionProbeSink? Probe,
        DirectorRig Rig
    ) BuildCore(
        IPlaybackInputSink? input,
        Func<IPlaybackInputSink, OutboundSanctionAccessor, SanctionProbeSink>? wrap
    )
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        SeedConnectedSpotify(db);
        db.SaveChanges();

        OutboundSanctionAccessor sanctions = new();
        IMusicProvider provider = Substitute.For<IMusicProvider>();
        provider.Provider.Returns("spotify");
        provider.Capabilities.Returns(AllControlCapabilities);

        SongRequestQueueStore directorStore = DirectorRig.NewStore(out SongRequestQueueStore _);
        DirectorRig rig = DirectorRig.Of(directorStore);
        SanctionProbeSink? probe = wrap?.Invoke(rig.Registry, sanctions);

        MusicService service = new(
            [provider],
            db,
            new RecordingEventBus(),
            new BlockedTrackService(db),
            directorStore,
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore(),
            PermissiveMusicConfigService.Instance,
            Substitute.For<ICurrencyAccountService>(),
            new NowPlayingCache(),
            sanctions,
            Substitute.For<IUserIdentityService>(),
            Substitute.For<IForeignLinkTitleLookup>(),
            input ?? (IPlaybackInputSink?)probe ?? rig.Registry,
            rig.Registry
        );

        return (service, sanctions, provider, probe, rig);
    }

    [Fact]
    public async Task PlayAsync_establishes_a_channel_configuration_sanction_before_the_director_call()
    {
        OwnerMusicSut sut = await BuildOwnerMusicAsync(paused: true);
        sut.Sanctions.Current.Should().BeNull();

        Result result = await sut.Service.PlayAsync(ChannelId.ToString());

        result.IsSuccess.Should().BeTrue();
        sut.Probe.Sent.Should()
            .BeOfType<OwnerResume>("the control reaches the director as the owner's input");
        sut.Probe.Observed.Should()
            .NotBeNull("the director call must carry a sanction it can be traced to");
        sut.Probe.Observed!.Basis.Should().Be(OutboundSanctionBasis.ChannelConfiguration);
        sut.Probe.Observed.Detail.Should().Be("music:playback_control");
        sut.Sanctions.Current.Should().BeNull("the sanction must not leak past the control");
        sut.Rig.Engines.EngineOf(ChannelId, EngineKind.Spotify)
            .Calls.Select(c => c.Name + ":" + c.Argument)
            .Should()
            .EndWith("Resume:");
        await sut.Provider.DidNotReceiveWithAnyArgs().PauseAsync(default, default);
    }

    [Fact]
    public async Task PauseAsync_establishes_a_channel_configuration_sanction_before_the_director_call()
    {
        OwnerMusicSut sut = await BuildOwnerMusicAsync(paused: false);
        sut.Sanctions.Current.Should().BeNull();

        Result result = await sut.Service.PauseAsync(ChannelId.ToString());

        result.IsSuccess.Should().BeTrue();
        sut.Probe.Sent.Should()
            .BeOfType<OwnerPause>("the control reaches the director as the owner's input");
        sut.Probe.Observed.Should()
            .NotBeNull("the director call must carry a sanction it can be traced to");
        sut.Probe.Observed!.Basis.Should().Be(OutboundSanctionBasis.ChannelConfiguration);
        sut.Probe.Observed.Detail.Should().Be("music:playback_control");
        sut.Sanctions.Current.Should().BeNull("the sanction must not leak past the control");
        sut.Rig.Engines.EngineOf(ChannelId, EngineKind.Spotify)
            .Calls.Select(c => c.Name + ":" + c.Argument)
            .Should()
            .EndWith("Pause:");
        await sut.Provider.DidNotReceiveWithAnyArgs().PauseAsync(default, default);
    }

    [Fact]
    public async Task SkipAsync_establishes_a_channel_configuration_sanction_before_the_director_call()
    {
        IPlaybackInputSink director = Substitute.For<IPlaybackInputSink>();
        (MusicService sut, OutboundSanctionAccessor sanctions, IMusicProvider provider) = BuildSut(
            director
        );
        OutboundSanction? observed = null;
        PlaybackInput? sent = null;
        director
            .SendAsync(ChannelId, Arg.Any<PlaybackInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                observed = sanctions.Current;
                sent = call.ArgAt<PlaybackInput>(1);
                return Task.FromResult(PlaybackInputOutcome.Applied);
            });

        sanctions.Current.Should().BeNull();

        Result result = await sut.SkipAsync(ChannelId.ToString(), "4242");

        result.IsSuccess.Should().BeTrue();
        sent.Should().BeOfType<OwnerSkip>("the skip reaches the director as the owner's input");
        observed.Should().NotBeNull("the director call must carry a sanction it can be traced to");
        observed!.Basis.Should().Be(OutboundSanctionBasis.ChannelConfiguration);
        observed.Detail.Should().Be("music:playback_control");
        sanctions.Current.Should().BeNull("the sanction must not leak past the skip");
        await provider.DidNotReceive().SkipAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreviousAsync_establishes_a_channel_configuration_sanction_before_the_director_call()
    {
        OwnerMusicSut sut = await BuildOwnerMusicAsync(paused: false);
        sut.Sanctions.Current.Should().BeNull();

        Result result = await sut.Service.PreviousAsync(ChannelId.ToString());

        result.IsSuccess.Should().BeTrue();
        sut.Probe.Sent.Should()
            .BeOfType<OwnerPrevious>("the control reaches the director as the owner's input");
        sut.Probe.Observed.Should()
            .NotBeNull("the director call must carry a sanction it can be traced to");
        sut.Probe.Observed!.Basis.Should().Be(OutboundSanctionBasis.ChannelConfiguration);
        sut.Probe.Observed.Detail.Should().Be("music:playback_control");
        sut.Sanctions.Current.Should().BeNull("the sanction must not leak past the control");
        sut.Rig.Engines.EngineOf(ChannelId, EngineKind.Spotify)
            .Calls.Select(c => c.Name + ":" + c.Argument)
            .Should()
            .EndWith("Seek:0");
        await sut.Provider.DidNotReceiveWithAnyArgs().PauseAsync(default, default);
    }

    [Fact]
    public async Task SetVolumeAsync_establishes_a_channel_configuration_sanction_before_the_director_call()
    {
        OwnerMusicSut sut = await BuildOwnerMusicAsync(paused: false);
        sut.Sanctions.Current.Should().BeNull();

        Result result = await sut.Service.SetVolumeAsync(ChannelId.ToString(), 50);

        result.IsSuccess.Should().BeTrue();
        sut.Probe.Sent.Should()
            .BeOfType<OwnerVolume>("the control reaches the director as the owner's input");
        sut.Probe.Observed.Should()
            .NotBeNull("the director call must carry a sanction it can be traced to");
        sut.Probe.Observed!.Basis.Should().Be(OutboundSanctionBasis.ChannelConfiguration);
        sut.Probe.Observed.Detail.Should().Be("music:volume_control");
        sut.Sanctions.Current.Should().BeNull("the sanction must not leak past the control");
        sut.Rig.Engines.EngineOf(ChannelId, EngineKind.Spotify)
            .Calls.Select(c => c.Name + ":" + c.Argument)
            .Should()
            .EndWith("SetVolume:50");
        await sut.Provider.DidNotReceiveWithAnyArgs().PauseAsync(default, default);
    }

    [Fact]
    public async Task SeekAsync_establishes_a_channel_configuration_sanction_before_the_director_call()
    {
        OwnerMusicSut sut = await BuildOwnerMusicAsync(paused: false);
        sut.Sanctions.Current.Should().BeNull();

        Result result = await sut.Service.SeekAsync(ChannelId.ToString(), 15_000);

        result.IsSuccess.Should().BeTrue();
        sut.Probe.Sent.Should()
            .BeOfType<OwnerSeek>("the control reaches the director as the owner's input");
        sut.Probe.Observed.Should()
            .NotBeNull("the director call must carry a sanction it can be traced to");
        sut.Probe.Observed!.Basis.Should().Be(OutboundSanctionBasis.ChannelConfiguration);
        sut.Probe.Observed.Detail.Should().Be("music:seek_control");
        sut.Sanctions.Current.Should().BeNull("the sanction must not leak past the control");
        sut.Rig.Engines.EngineOf(ChannelId, EngineKind.Spotify)
            .Calls.Select(c => c.Name + ":" + c.Argument)
            .Should()
            .EndWith("Seek:15000");
        await sut.Provider.DidNotReceiveWithAnyArgs().PauseAsync(default, default);
    }

    [Fact]
    public async Task SetShuffleAsync_establishes_a_channel_configuration_sanction_before_the_provider_call()
    {
        (MusicService sut, OutboundSanctionAccessor sanctions, IMusicProvider provider) =
            BuildSut();
        OutboundSanction? observed = null;
        provider
            .SetShuffleAsync(ChannelId, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                observed = sanctions.Current;
                return Task.CompletedTask;
            });

        sanctions.Current.Should().BeNull();

        Result result = await sut.SetShuffleAsync(ChannelId.ToString(), enabled: true);

        result.IsSuccess.Should().BeTrue();
        observed.Should().NotBeNull("the provider write must carry a sanction it can be traced to");
        observed!.Basis.Should().Be(OutboundSanctionBasis.ChannelConfiguration);
        observed.Detail.Should().Be("music:shuffle_control");
        sanctions.Current.Should().BeNull("the sanction must not leak past the write");
    }

    [Fact]
    public async Task SetRepeatAsync_establishes_a_channel_configuration_sanction_before_the_provider_call()
    {
        (MusicService sut, OutboundSanctionAccessor sanctions, IMusicProvider provider) =
            BuildSut();
        OutboundSanction? observed = null;
        provider
            .SetRepeatAsync(ChannelId, Arg.Any<MusicRepeatMode>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                observed = sanctions.Current;
                return Task.CompletedTask;
            });

        sanctions.Current.Should().BeNull();

        Result result = await sut.SetRepeatAsync(ChannelId.ToString(), "track");

        result.IsSuccess.Should().BeTrue();
        observed.Should().NotBeNull("the provider write must carry a sanction it can be traced to");
        observed!.Basis.Should().Be(OutboundSanctionBasis.ChannelConfiguration);
        observed.Detail.Should().Be("music:repeat_control");
        sanctions.Current.Should().BeNull("the sanction must not leak past the write");
    }

    [Fact]
    public async Task TransferPlaybackAsync_establishes_a_channel_configuration_sanction_before_the_provider_call()
    {
        (MusicService sut, OutboundSanctionAccessor sanctions, IMusicProvider provider) =
            BuildSut();
        OutboundSanction? observed = null;
        provider
            .TransferPlaybackAsync(
                ChannelId,
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                observed = sanctions.Current;
                return Task.CompletedTask;
            });

        sanctions.Current.Should().BeNull();

        Result result = await sut.TransferPlaybackAsync(ChannelId.ToString(), "device-1");

        result.IsSuccess.Should().BeTrue();
        observed.Should().NotBeNull("the provider write must carry a sanction it can be traced to");
        observed!.Basis.Should().Be(OutboundSanctionBasis.ChannelConfiguration);
        observed.Detail.Should().Be("music:device_transfer_control");
        sanctions.Current.Should().BeNull("the sanction must not leak past the write");
    }
}
