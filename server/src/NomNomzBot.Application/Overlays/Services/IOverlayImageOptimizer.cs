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

namespace NomNomzBot.Application.Overlays.Services;

/// <summary>
/// Turns any allow-listed remote image into a WebP of a bucketed width, so an overlay never loads a source
/// image larger than it draws. The result is a file in the server's image cache, keyed by the source URL and the
/// width bucket.
/// </summary>
public interface IOverlayImageOptimizer
{
    /// <summary>The widths an image is resized to; a requested width snaps up to the next one (over 1024 = 1024).</summary>
    public static readonly IReadOnlyList<int> WidthBuckets = [32, 64, 128, 256, 512, 1024];

    /// <summary>
    /// Fetches (or finds in the cache) <paramref name="source"/> as WebP at the bucket for <paramref name="width"/>.
    /// <paramref name="ownOrigin"/> is the server's configured public origin, which is allowed as a source host.
    /// Refusals are failures: <see cref="ErrorCodes.SourceNotAllowed"/>, <see cref="ErrorCodes.TooLarge"/>,
    /// <see cref="ErrorCodes.Unsupported"/> and <see cref="ErrorCodes.InvalidWidth"/> (the caller's fault), and
    /// <see cref="ErrorCodes.FetchFailed"/> (the source host's).
    /// </summary>
    Task<Result<OptimizedImage>> OptimizeAsync(
        string source,
        int width,
        string? ownOrigin,
        CancellationToken ct
    );

    public static int SnapWidth(int width)
    {
        foreach (int bucket in WidthBuckets)
        {
            if (width <= bucket)
                return bucket;
        }

        return WidthBuckets[^1];
    }

    public static class ErrorCodes
    {
        public const string SourceNotAllowed = "IMAGE_SOURCE_NOT_ALLOWED";
        public const string TooLarge = "IMAGE_TOO_LARGE";
        public const string Unsupported = "IMAGE_UNSUPPORTED";
        public const string InvalidWidth = "IMAGE_INVALID_WIDTH";
        public const string FetchFailed = "IMAGE_FETCH_FAILED";
    }
}

/// <summary>A cached WebP: its file, its length, and the ETag derived from the source and bucket.</summary>
public sealed record OptimizedImage(string FilePath, long Length, string ETag);
