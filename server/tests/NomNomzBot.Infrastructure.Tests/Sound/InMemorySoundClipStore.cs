// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Sound.Services;

namespace NomNomzBot.Infrastructure.Tests.Sound;

/// <summary>
/// An in-memory <see cref="ISoundClipStore"/> that records every blob by storage key, with the same key shape
/// as the disk store (<c>{broadcasterId:N}/…</c> for a channel, <c>platform/…</c> for platform audio) — so a
/// test can prove where bytes landed and that they were copied, not shared.
/// </summary>
internal sealed class InMemorySoundClipStore : ISoundClipStore
{
    public Dictionary<string, byte[]> Blobs { get; } = [];

    public Task<Result<string>> PutAsync(
        Guid broadcasterId,
        string fileName,
        System.IO.Stream content,
        string mimeType,
        CancellationToken ct = default
    ) => WriteAsync(broadcasterId.ToString("N"), fileName, content, ct);

    public Task<Result<string>> PutPlatformAssetAsync(
        string fileName,
        System.IO.Stream content,
        string mimeType,
        CancellationToken ct = default
    ) => WriteAsync("platform", fileName, content, ct);

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

    private async Task<Result<string>> WriteAsync(
        string area,
        string fileName,
        System.IO.Stream content,
        CancellationToken ct
    )
    {
        using MemoryStream ms = new();
        await content.CopyToAsync(ms, ct);
        string key = $"{area}/{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
        Blobs[key] = ms.ToArray();
        return Result<string>.Success(key);
    }
}
