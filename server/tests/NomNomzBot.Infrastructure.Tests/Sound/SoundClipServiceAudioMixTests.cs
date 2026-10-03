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
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Sound;
using NomNomzBot.Infrastructure.Tests.Platform.Pipeline;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Sound;

/// <summary>
/// The master volume is applied once, where a clip is resolved for playback, so every route that plays a clip
/// (play_sound step, chat trigger, reward alert, preview) sends the balance the bot stores.
/// </summary>
public sealed class SoundClipServiceAudioMixTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192c000-0000-7000-8000-00000000f001");

    private static (SoundClipService Service, PipelineOptionsTestDbContext Db) Build()
    {
        PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        ISoundClipStore store = Substitute.For<ISoundClipStore>();
        store
            .GetPlaybackUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Result<string>.Success($"/stream/{call.Arg<string>()}"));
        SoundClipService service = new(
            db,
            store,
            Substitute.For<ISoundClipOverlayNotifier>(),
            Substitute.For<IChannelRegistry>(),
            Substitute.For<IResourceQuotaService>(),
            Substitute.For<IPipelineStepReferenceScanner>(),
            Substitute.For<IOverlayPresenceRegistry>(),
            new ChannelAudioMixService(db)
        );
        db.SoundClips.Add(
            new()
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Broadcaster,
                Name = "airhorn",
                DisplayName = "Airhorn",
                StorageKey = "airhorn.mp3",
                MimeType = "audio/mpeg",
                DurationMs = 1000,
                SizeBytes = 2048,
                DefaultVolume = 80,
                IsEnabled = true,
                CreatedByUserId = Guid.NewGuid(),
            }
        );
        db.SaveChanges();
        return (service, db);
    }

    [Fact]
    public async Task Master_50_halves_the_clip_default_volume()
    {
        (SoundClipService service, PipelineOptionsTestDbContext db) = Build();
        await using PipelineOptionsTestDbContext _ = db;
        await new ChannelAudioMixService(db).UpdateAsync(Broadcaster, new(50, 100));

        Result<SoundPlaybackDto> playback = await service.ResolveForPlaybackAsync(
            Broadcaster,
            "airhorn",
            null
        );

        playback.Value.Volume.Should().Be(40);
    }

    [Fact]
    public async Task Master_50_halves_an_explicit_volume_override()
    {
        (SoundClipService service, PipelineOptionsTestDbContext db) = Build();
        await using PipelineOptionsTestDbContext _ = db;
        await new ChannelAudioMixService(db).UpdateAsync(Broadcaster, new(50, 100));

        Result<SoundPlaybackDto> playback = await service.ResolveForPlaybackAsync(
            Broadcaster,
            "airhorn",
            100
        );

        playback.Value.Volume.Should().Be(50);
    }

    [Fact]
    public async Task With_no_mix_row_the_clip_volume_is_unchanged()
    {
        (SoundClipService service, PipelineOptionsTestDbContext db) = Build();
        await using PipelineOptionsTestDbContext _ = db;

        Result<SoundPlaybackDto> playback = await service.ResolveForPlaybackAsync(
            Broadcaster,
            "airhorn",
            null
        );

        playback.Value.Volume.Should().Be(80);
    }
}
