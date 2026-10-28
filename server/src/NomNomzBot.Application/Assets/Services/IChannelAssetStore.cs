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

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Assets.Services;

/// <summary>
/// Deployment-profile blob store for durable broadcaster-uploaded media assets — the same store shape as
/// <c>ISoundClipStore</c> (disk directory on self-host, object store on SaaS), kept separate so the two
/// libraries can move independently. Overlays fetch assets through the stable public serving route, not
/// through this store directly.
/// </summary>
public interface IChannelAssetStore
{
    /// <summary>Persists the asset stream and returns the opaque <c>StorageKey</c> used on later lookups.</summary>
    Task<Result<string>> PutAsync(
        Guid broadcasterId,
        string fileName,
        Stream content,
        string mimeType,
        CancellationToken ct = default
    );

    /// <summary>Opens the asset stream for reading (the public serving route and bundle export).</summary>
    Task<Result<Stream>> OpenAsync(string storageKey, CancellationToken ct = default);

    /// <summary>Permanently removes the blob. Call only after the DB row is soft-deleted or re-pointed.</summary>
    Task<Result> DeleteAsync(string storageKey, CancellationToken ct = default);
}
