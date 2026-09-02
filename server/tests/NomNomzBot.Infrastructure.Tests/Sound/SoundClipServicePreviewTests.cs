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
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Sound.Entities;
using NomNomzBot.Infrastructure.Sound;
using NomNomzBot.Infrastructure.Tests.Platform.Pipeline;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Sound;

/// <summary>
/// S104-PREVIEW-ON-OVERLAY: proves <c>SoundClipService.PreviewAsync</c> — the backend half of the sound
/// library's "Preview on overlay" action — actually dispatches a real overlay event through
/// <see cref="ISoundClipOverlayNotifier"/> (the same abstraction <c>OverlayNotifierAdapter</c> uses to push
/// over SignalR), rather than merely returning success with no downstream effect.
/// </summary>
public sealed class SoundClipServicePreviewTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192c000-0000-7000-8000-00000000c001");

    private static (
        SoundClipService Service,
        PipelineOptionsTestDbContext Db,
        ISoundClipOverlayNotifier Overlay
    ) Build()
    {
        PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        ISoundClipOverlayNotifier overlay = Substitute.For<ISoundClipOverlayNotifier>();
        IResourceQuotaService quota = Substitute.For<IResourceQuotaService>();

        SoundClipService service = new(
            db,
            new FakeSoundClipStore(),
            overlay,
            Substitute.For<IChannelRegistry>(),
            quota,
            Substitute.For<IPipelineStepReferenceScanner>()
        );
        return (service, db, overlay);
    }

    private static SoundClip NewClip(Guid id) =>
        new()
        {
            Id = id,
            BroadcasterId = Broadcaster,
            Name = "airhorn",
            DisplayName = "Airhorn",
            StorageKey = $"{Broadcaster:N}/airhorn.mp3",
            MimeType = "audio/mpeg",
            DurationMs = 1200,
            SizeBytes = 4096,
            DefaultVolume = 65,
            IsEnabled = true,
            CreatedByUserId = Guid.NewGuid(),
        };

    [Fact]
    public async Task PreviewAsync_pushes_the_resolved_clip_to_the_overlay_notifier()
    {
        (
            SoundClipService service,
            PipelineOptionsTestDbContext db,
            ISoundClipOverlayNotifier overlay
        ) = Build();
        Guid clipId = Guid.NewGuid();
        db.SoundClips.Add(NewClip(clipId));
        await db.SaveChangesAsync();

        Result preview = await service.PreviewAsync(Broadcaster, clipId);

        preview.IsSuccess.Should().BeTrue(preview.ErrorMessage);
        // This is the real overlay dispatch mechanism (SoundClipOverlayNotifierAdapter → OverlayHub SignalR
        // push in the API layer) — not a no-op. Assert the exact playback the overlay was told to play.
        await overlay
            .Received(1)
            .PlaySoundAsync(
                Broadcaster,
                Arg.Is<SoundPlaybackDto>(dto =>
                    dto.ClipId == clipId && dto.Volume == 65 && dto.DurationMs == 1200
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PreviewAsync_for_a_disabled_clip_fails_and_never_touches_the_overlay()
    {
        (
            SoundClipService service,
            PipelineOptionsTestDbContext db,
            ISoundClipOverlayNotifier overlay
        ) = Build();
        Guid clipId = Guid.NewGuid();
        SoundClip disabled = NewClip(clipId);
        disabled.IsEnabled = false;
        db.SoundClips.Add(disabled);
        await db.SaveChangesAsync();

        Result preview = await service.PreviewAsync(Broadcaster, clipId);

        preview.IsSuccess.Should().BeFalse();
        preview.ErrorCode.Should().Be("NOT_FOUND");
        await overlay
            .DidNotReceive()
            .PlaySoundAsync(
                Arg.Any<Guid>(),
                Arg.Any<SoundPlaybackDto>(),
                Arg.Any<CancellationToken>()
            );
    }

    /// <summary>A minimal <see cref="ISoundClipStore"/> that resolves any storage key to a stream URL.</summary>
    private sealed class FakeSoundClipStore : ISoundClipStore
    {
        public Task<Result<string>> PutAsync(
            Guid broadcasterId,
            string fileName,
            System.IO.Stream content,
            string mimeType,
            CancellationToken ct = default
        ) => Task.FromResult(Result<string>.Success($"{broadcasterId:N}/{fileName}"));

        public Task<Result<System.IO.Stream>> OpenAsync(
            string storageKey,
            CancellationToken ct = default
        ) => Task.FromResult(Result<System.IO.Stream>.Success(new MemoryStream()));

        public Task<Result> DeleteAsync(string storageKey, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());

        public Task<Result<string>> GetPlaybackUrlAsync(
            string storageKey,
            CancellationToken ct = default
        ) => Task.FromResult(Result<string>.Success($"/api/v1/sound-clips/stream/{storageKey}"));
    }
}
