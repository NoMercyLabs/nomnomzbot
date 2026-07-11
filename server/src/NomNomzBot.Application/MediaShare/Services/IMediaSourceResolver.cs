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
