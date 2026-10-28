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

using NomNomzBot.Application.Assets.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Infrastructure.Platform;

namespace NomNomzBot.Infrastructure.Assets;

/// <summary>
/// Self-host implementation of <see cref="IChannelAssetStore"/> — the asset twin of
/// <c>DiskSoundClipStore</c>. Blobs are persisted under <c>NOMNOMZ_DATA_DIR/assets/{broadcasterId}/</c>;
/// the storage key is the relative path <c>{broadcasterId}/{uniqueFileName}</c>.
/// </summary>
internal sealed class DiskChannelAssetStore : IChannelAssetStore
{
    private readonly string _root = SelfHostDataPaths.AssetsDirectory;

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

        await using FileStream fs = File.Create(fullPath);
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
            return Task.FromResult(Result<System.IO.Stream>.Failure("Asset file not found."));

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
}
