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
        Stream content,
        string mimeType,
        CancellationToken ct = default
    );

    /// <summary>
    /// Persists an audio file the platform owns (a <c>sound_clip</c> template's source) in the store's platform
    /// area, apart from every channel's clips, and returns its <c>StorageKey</c>. Open, delete and playback URLs
    /// work on that key exactly as on a channel clip's key.
    /// </summary>
    Task<Result<string>> PutPlatformAssetAsync(
        string fileName,
        Stream content,
        string mimeType,
        CancellationToken ct = default
    );

    /// <summary>Opens the clip stream for reading (e.g. to serve a direct download).</summary>
    Task<Result<Stream>> OpenAsync(string storageKey, CancellationToken ct = default);

    /// <summary>Permanently removes the blob. Call only after the DB row is soft-deleted.</summary>
    Task<Result> DeleteAsync(string storageKey, CancellationToken ct = default);

    /// <summary>
    /// Returns a URL the overlay can fetch to play the clip. On self-host this is a local API URL with a
    /// short-lived HMAC token; on SaaS it can be a pre-signed object-store URL.
    /// </summary>
    Task<Result<string>> GetPlaybackUrlAsync(string storageKey, CancellationToken ct = default);
}
