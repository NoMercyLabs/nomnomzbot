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

namespace NomNomzBot.Application.MediaShare.Services;

/// <summary>The metadata of a resolved media source (media-share.md D2).</summary>
public sealed record ResolvedMedia(
    string SourceType,
    string MediaRef,
    string? Title,
    int DurationSeconds,
    string? ThumbnailUrl
);

/// <summary>
/// Parses a submission URL into a closed-set source (Twitch clip / YouTube video) and fetches its
/// server-side metadata (media-share.md D2). Isolated so the queue service stays provider-agnostic and
/// testable. Fails <c>SOURCE_NOT_ALLOWED</c> for anything off the allowlist, <c>NOT_FOUND</c> when the
/// clip/video doesn't resolve.
/// </summary>
public interface IMediaSourceResolver
{
    Task<Result<ResolvedMedia>> ResolveAsync(
        string url,
        bool allowTwitchClips,
        bool allowYouTube,
        CancellationToken ct = default
    );
}
