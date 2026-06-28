// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.IO;
using Microsoft.AspNetCore.Http;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Infrastructure.Platform;

namespace NomNomzBot.Infrastructure.Sound;

/// <summary>
/// Self-host implementation of <see cref="ISoundClipStore"/>. Clips are persisted under
/// <c>NOMNOMZ_DATA_DIR/sound-clips/{broadcasterId}/</c>. The storage key is the relative path
/// <c>{broadcasterId}/{uniqueFileName}</c>, which doubles as the path fragment for the playback URL.
/// </summary>
internal sealed class DiskSoundClipStore : ISoundClipStore
{
    private readonly string _root = SelfHostDataPaths.SoundClipsDirectory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DiskSoundClipStore(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<Result<string>> PutAsync(
        Guid broadcasterId,
        string fileName,
        System.IO.Stream content,
        string mimeType,
        CancellationToken ct = default
    )
    {
        string channelDir = Path.Combine(_root, broadcasterId.ToString("N"));
        Directory.CreateDirectory(channelDir);

        string ext = Path.GetExtension(fileName);
        string uniqueName = $"{Guid.NewGuid():N}{ext}";
        string fullPath = Path.Combine(channelDir, uniqueName);
        string storageKey = $"{broadcasterId:N}/{uniqueName}";

        await using System.IO.FileStream fs = File.Create(fullPath);
        await content.CopyToAsync(fs, ct);

        return Result<string>.Success(storageKey);
    }

    public Task<Result<System.IO.Stream>> OpenAsync(
        string storageKey,
        CancellationToken ct = default
    )
    {
        string fullPath = Path.Combine(_root, storageKey.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
            return Task.FromResult(Result<System.IO.Stream>.Failure("Clip file not found."));

        System.IO.Stream stream = File.OpenRead(fullPath);
        return Task.FromResult(Result<System.IO.Stream>.Success(stream));
    }

    public Task<Result> DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        string fullPath = Path.Combine(_root, storageKey.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath))
            File.Delete(fullPath);

        return Task.FromResult(Result.Success());
    }

    public Task<Result<string>> GetPlaybackUrlAsync(
        string storageKey,
        CancellationToken ct = default
    )
    {
        // Build an absolute URL to the sound-clips serve endpoint using the current request's base URL.
        // The endpoint validates the storage key and streams the file — no per-request token is issued
        // on self-host (the overlay is always behind the broadcaster's own network).
        HttpContext? ctx = _httpContextAccessor.HttpContext;
        string baseUrl = ctx is not null
            ? $"{ctx.Request.Scheme}://{ctx.Request.Host}"
            : "http://localhost:5080";

        string url = $"{baseUrl}/api/v1/sound-clips/stream/{Uri.EscapeDataString(storageKey)}";
        return Task.FromResult(Result<string>.Success(url));
    }
}
