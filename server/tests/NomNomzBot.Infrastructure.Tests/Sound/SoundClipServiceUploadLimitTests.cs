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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Sound.Entities;
using NomNomzBot.Infrastructure.Sound;
using NomNomzBot.Infrastructure.Tests.Platform.Pipeline;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Sound;

/// <summary>
/// S062a-sound-upload-limits: proves the 10 MB per-clip cap (<c>SoundClipService.MaxSizeBytes</c>) is actually
/// enforced server-side — an oversized upload is refused with a clear <c>SIZE_EXCEEDED</c> error and never
/// reaches the blob store or the database, while an in-limit upload persists and stores normally.
/// </summary>
public sealed class SoundClipServiceUploadLimitTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192c000-0000-7000-8000-00000000b001");
    private static readonly Guid Actor = Guid.Parse("0192c000-0000-7000-8000-00000000b0aa");

    private const int MaxSizeBytes = 10 * 1024 * 1024;

    private static (
        SoundClipService Service,
        PipelineOptionsTestDbContext Db,
        FakeSoundClipStore Store
    ) Build()
    {
        PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        FakeSoundClipStore store = new();
        IResourceQuotaService quota = Substitute.For<IResourceQuotaService>();
        quota
            .GetCurrentCountAsync(
                Broadcaster,
                "sound_clip_storage_bytes",
                Arg.Any<CancellationToken>()
            )
            .Returns(Result<long>.Success(0));
        quota
            .CheckAsync(
                Broadcaster,
                "sound_clip_storage_bytes",
                Arg.Any<long>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo =>
                Result<QuotaCheckDto>.Success(
                    new(
                        true,
                        "sound_clip_storage_bytes",
                        callInfo.ArgAt<long>(2),
                        long.MaxValue,
                        long.MaxValue
                    )
                )
            );

        SoundClipService service = new(
            db,
            store,
            Substitute.For<ISoundClipOverlayNotifier>(),
            Substitute.For<IChannelRegistry>(),
            quota,
            Substitute.For<IPipelineStepReferenceScanner>()
        );
        return (service, db, store);
    }

    /// <summary>Real MP3 magic bytes (<c>ID3</c>) padded to the requested total length.</summary>
    private static byte[] Mp3Bytes(int totalLength)
    {
        byte[] bytes = new byte[totalLength];
        bytes[0] = 0x49; // 'I'
        bytes[1] = 0x44; // 'D'
        bytes[2] = 0x33; // '3'
        return bytes;
    }

    [Fact]
    public async Task Upload_over_the_10mb_size_cap_is_rejected_and_not_persisted()
    {
        (SoundClipService service, PipelineOptionsTestDbContext db, FakeSoundClipStore store) =
            Build();
        byte[] tooBig = Mp3Bytes(MaxSizeBytes + 1);

        Result<SoundClipDto> uploaded = await service.UploadAsync(
            Broadcaster,
            Actor,
            new("too-big", "Too Big", "too-big.mp3", "audio/mpeg", new MemoryStream(tooBig), 80)
        );

        uploaded.IsFailure.Should().BeTrue();
        uploaded.ErrorCode.Should().Be("SIZE_EXCEEDED");
        uploaded.ErrorMessage.Should().Contain("10 MB");

        (await db.SoundClips.CountAsync()).Should().Be(0);
        store.Blobs.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_within_the_10mb_size_cap_succeeds_and_persists_the_clip_and_blob()
    {
        (SoundClipService service, PipelineOptionsTestDbContext db, FakeSoundClipStore store) =
            Build();
        byte[] withinLimit = Mp3Bytes(MaxSizeBytes - 1);

        Result<SoundClipDto> uploaded = await service.UploadAsync(
            Broadcaster,
            Actor,
            new(
                "just-fits",
                "Just Fits",
                "just-fits.mp3",
                "audio/mpeg",
                new MemoryStream(withinLimit),
                80
            )
        );

        uploaded.IsSuccess.Should().BeTrue(uploaded.ErrorMessage);
        uploaded.Value.SizeBytes.Should().Be(withinLimit.Length);

        SoundClip row = await db.SoundClips.SingleAsync();
        row.BroadcasterId.Should().Be(Broadcaster);
        row.Name.Should().Be("just-fits");
        row.SizeBytes.Should().Be(withinLimit.Length);
        store.Blobs.Should().ContainKey(row.StorageKey);
        store.Blobs[row.StorageKey].Should().HaveCount(withinLimit.Length);
    }

    /// <summary>An in-memory <see cref="ISoundClipStore"/> that records blobs by storage key.</summary>
    private sealed class FakeSoundClipStore : ISoundClipStore
    {
        public Dictionary<string, byte[]> Blobs { get; } = [];

        public async Task<Result<string>> PutAsync(
            Guid broadcasterId,
            string fileName,
            System.IO.Stream content,
            string mimeType,
            CancellationToken ct = default
        )
        {
            using MemoryStream ms = new();
            await content.CopyToAsync(ms, ct);
            string key = $"{broadcasterId:N}/{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
            Blobs[key] = ms.ToArray();
            return Result<string>.Success(key);
        }

        public Task<Result<System.IO.Stream>> OpenAsync(
            string storageKey,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                Blobs.TryGetValue(storageKey, out byte[]? bytes)
                    ? Result<System.IO.Stream>.Success(new MemoryStream(bytes))
                    : Result<System.IO.Stream>.Failure("Sound clip file not found.")
            );

        public Task<Result> DeleteAsync(string storageKey, CancellationToken ct = default)
        {
            Blobs.Remove(storageKey);
            return Task.FromResult(Result.Success());
        }

        public Task<Result<string>> GetPlaybackUrlAsync(
            string storageKey,
            CancellationToken ct = default
        ) => Task.FromResult(Result<string>.Success($"/api/v1/sound-clips/stream/{storageKey}"));
    }
}
