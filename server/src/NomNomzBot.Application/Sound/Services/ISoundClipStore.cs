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

namespace NomNomzBot.Application.Sound.Services;

/// <summary>
/// Deployment-profile blob store for durable broadcaster-uploaded sound clips (spec §3). On self-host this is
/// a local disk directory; on SaaS it delegates to the object store. The overlay fetches clips via a tokened
/// playback URL returned by <see cref="GetPlaybackUrlAsync"/>.
/// </summary>
public interface ISoundClipStore
{
    /// <summary>Persists the clip stream and returns the opaque <c>StorageKey</c> used on later lookups.</summary>
    Task<Result<string>> PutAsync(
        Guid broadcasterId,
        string fileName,
        System.IO.Stream content,
        string mimeType,
        CancellationToken ct = default
    );

    /// <summary>Opens the clip stream for reading (e.g. to serve a direct download).</summary>
    Task<Result<System.IO.Stream>> OpenAsync(string storageKey, CancellationToken ct = default);

    /// <summary>Permanently removes the blob. Call only after the DB row is soft-deleted.</summary>
    Task<Result> DeleteAsync(string storageKey, CancellationToken ct = default);

    /// <summary>
    /// Returns a URL the overlay can fetch to play the clip. On self-host this is a local API URL with a
    /// short-lived HMAC token; on SaaS it can be a pre-signed object-store URL.
    /// </summary>
    Task<Result<string>> GetPlaybackUrlAsync(string storageKey, CancellationToken ct = default);
}
