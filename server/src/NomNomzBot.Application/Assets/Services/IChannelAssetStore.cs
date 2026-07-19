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
        System.IO.Stream content,
        string mimeType,
        CancellationToken ct = default
    );

    /// <summary>Opens the asset stream for reading (the public serving route and bundle export).</summary>
    Task<Result<System.IO.Stream>> OpenAsync(string storageKey, CancellationToken ct = default);

    /// <summary>Permanently removes the blob. Call only after the DB row is soft-deleted or re-pointed.</summary>
    Task<Result> DeleteAsync(string storageKey, CancellationToken ct = default);
}
