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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Domain.Music.Interfaces;
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
/// <see cref="MusicService.HandOverNextAsync"/> and <see cref="MusicService.EnqueueResolvedAsync"/> already
/// do: <see cref="OutboundSanctionHandler"/> silently refuses any outbound provider write with no ambient
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
    ) BuildSut()
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        SeedConnectedSpotify(db);
        db.SaveChanges();

        OutboundSanctionAccessor sanctions = new();
        IMusicProvider provider = Substitute.For<IMusicProvider>();
        provider.Provider.Returns("spotify");
        provider.Capabilities.Returns(AllControlCapabilities);

        MusicService service = new(
            [provider],
            db,
            new RecordingEventBus(),
            new BlockedTrackService(db),
            new SongRequestQueueStore(),
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore(),
            PermissiveMusicConfigService.Instance,
            Substitute.For<ICurrencyAccountService>(),
            new NowPlayingCache(),
            sanctions
        );

        return (service, sanctions, provider);
    }

    [Fact]
    public async Task PlayAsync_establishes_a_channel_configuration_sanction_before_the_provider_call()
    {
        (MusicService sut, OutboundSanctionAccessor sanctions, IMusicProvider provider) =
            BuildSut();
        OutboundSanction? observed = null;
        provider
            .PlayAsync(ChannelId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                observed = sanctions.Current;
                return Task.CompletedTask;
            });

        sanctions.Current.Should().BeNull();

        Result result = await sut.PlayAsync(ChannelId.ToString());

        result.IsSuccess.Should().BeTrue();
        observed.Should().NotBeNull("the provider write must carry a sanction it can be traced to");
        observed!.Basis.Should().Be(OutboundSanctionBasis.ChannelConfiguration);
        observed.Detail.Should().Be("music:playback_control");
        sanctions.Current.Should().BeNull("the sanction must not leak past the write");
    }

    [Fact]
    public async Task PauseAsync_establishes_a_channel_configuration_sanction_before_the_provider_call()
    {
        // The concretely-known live gap: PlayOnceResumeHandler calls this off a PlaybackStateChangedEvent
        // the poller published on its own schedule — no person present, no HTTP request behind it.
        (MusicService sut, OutboundSanctionAccessor sanctions, IMusicProvider provider) =
            BuildSut();
        OutboundSanction? observed = null;
        provider
            .PauseAsync(ChannelId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                observed = sanctions.Current;
                return Task.CompletedTask;
            });

        sanctions.Current.Should().BeNull();

        Result result = await sut.PauseAsync(ChannelId.ToString());

        result.IsSuccess.Should().BeTrue();
        observed.Should().NotBeNull("the provider write must carry a sanction it can be traced to");
        observed!.Basis.Should().Be(OutboundSanctionBasis.ChannelConfiguration);
        observed.Detail.Should().Be("music:playback_control");
        sanctions.Current.Should().BeNull("the sanction must not leak past the write");
    }

    [Fact]
    public async Task SkipAsync_establishes_a_channel_configuration_sanction_before_the_provider_call()
    {
        (MusicService sut, OutboundSanctionAccessor sanctions, IMusicProvider provider) =
            BuildSut();
        OutboundSanction? observed = null;
        provider
            .SkipAsync(ChannelId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                observed = sanctions.Current;
                return Task.CompletedTask;
            });

        sanctions.Current.Should().BeNull();

        Result result = await sut.SkipAsync(ChannelId.ToString());

        result.IsSuccess.Should().BeTrue();
        observed.Should().NotBeNull("the provider write must carry a sanction it can be traced to");
        observed!.Basis.Should().Be(OutboundSanctionBasis.ChannelConfiguration);
        observed.Detail.Should().Be("music:playback_control");
        sanctions.Current.Should().BeNull("the sanction must not leak past the write");
    }

    [Fact]
    public async Task PreviousAsync_establishes_a_channel_configuration_sanction_before_the_provider_call()
    {
        (MusicService sut, OutboundSanctionAccessor sanctions, IMusicProvider provider) =
            BuildSut();
        OutboundSanction? observed = null;
        provider
            .PreviousAsync(ChannelId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                observed = sanctions.Current;
                return Task.CompletedTask;
            });

        sanctions.Current.Should().BeNull();

        Result result = await sut.PreviousAsync(ChannelId.ToString());

        result.IsSuccess.Should().BeTrue();
        observed.Should().NotBeNull("the provider write must carry a sanction it can be traced to");
        observed!.Basis.Should().Be(OutboundSanctionBasis.ChannelConfiguration);
        observed.Detail.Should().Be("music:playback_control");
        sanctions.Current.Should().BeNull("the sanction must not leak past the write");
    }

    [Fact]
    public async Task SetVolumeAsync_establishes_a_channel_configuration_sanction_before_the_provider_call()
    {
        (MusicService sut, OutboundSanctionAccessor sanctions, IMusicProvider provider) =
            BuildSut();
        OutboundSanction? observed = null;
        provider
            .SetVolumeAsync(ChannelId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                observed = sanctions.Current;
                return Task.CompletedTask;
            });

        sanctions.Current.Should().BeNull();

        Result result = await sut.SetVolumeAsync(ChannelId.ToString(), 50);

        result.IsSuccess.Should().BeTrue();
        observed.Should().NotBeNull("the provider write must carry a sanction it can be traced to");
        observed!.Basis.Should().Be(OutboundSanctionBasis.ChannelConfiguration);
        observed.Detail.Should().Be("music:volume_control");
        sanctions.Current.Should().BeNull("the sanction must not leak past the write");
    }

    [Fact]
    public async Task SeekAsync_establishes_a_channel_configuration_sanction_before_the_provider_call()
    {
        // The concretely-known live gap: PlayOnceResumeHandler calls this off a PlaybackStateChangedEvent
        // the poller published on its own schedule — no person present, no HTTP request behind it.
        (MusicService sut, OutboundSanctionAccessor sanctions, IMusicProvider provider) =
            BuildSut();
        OutboundSanction? observed = null;
        provider
            .SeekAsync(ChannelId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                observed = sanctions.Current;
                return Task.CompletedTask;
            });

        sanctions.Current.Should().BeNull();

        Result result = await sut.SeekAsync(ChannelId.ToString(), 15_000);

        result.IsSuccess.Should().BeTrue();
        observed.Should().NotBeNull("the provider write must carry a sanction it can be traced to");
        observed!.Basis.Should().Be(OutboundSanctionBasis.ChannelConfiguration);
        observed.Detail.Should().Be("music:seek_control");
        sanctions.Current.Should().BeNull("the sanction must not leak past the write");
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
