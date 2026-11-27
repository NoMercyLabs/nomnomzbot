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

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Overlays.Services;

namespace NomNomzBot.Infrastructure.Overlays.Images;

/// <summary>
/// The order of work for one image request: check the source and width, answer from the disk cache when the
/// (source, bucket) pair is there, otherwise fetch, resize, and store.
/// </summary>
public sealed class OverlayImageOptimizer(
    IHttpClientFactory httpClients,
    OverlayImageDiskCache cache,
    ILogger<OverlayImageOptimizer> logger
) : IOverlayImageOptimizer
{
    private readonly OverlayImageSourceFetcher _fetcher = new(httpClients);

    public async Task<Result<OptimizedImage>> OptimizeAsync(
        string source,
        int width,
        string? ownOrigin,
        CancellationToken ct
    )
    {
        if (width <= 0)
            return Refuse(
                IOverlayImageOptimizer.ErrorCodes.InvalidWidth,
                "w must be a positive width."
            );

        Uri? uri = OverlayImageSourcePolicy.Allow(source, ownOrigin);
        if (uri is null)
            return Refuse(
                IOverlayImageOptimizer.ErrorCodes.SourceNotAllowed,
                "That image source is not allowed."
            );

        int bucket = IOverlayImageOptimizer.SnapWidth(width);
        OptimizedImage? cached = cache.TryGet(uri.AbsoluteUri, bucket);
        if (cached is not null)
            return Result.Success(cached);

        Result<byte[]> fetched = await _fetcher.FetchAsync(uri, ownOrigin, ct);
        if (fetched.IsFailure)
        {
            logger.LogInformation(
                "Overlay image {Source} not served: {Code} {Message}",
                uri.Host,
                fetched.ErrorCode,
                fetched.ErrorMessage
            );
            return Result.Failure<OptimizedImage>(fetched.ErrorMessage, fetched.ErrorCode);
        }

        Result<byte[]> webp = await Task.Run(
            () => OverlayImageEncoder.ToWebp(fetched.Value, bucket),
            ct
        );
        return webp.IsFailure
            ? Result.Failure<OptimizedImage>(webp.ErrorMessage, webp.ErrorCode)
            : Result.Success(cache.Put(uri.AbsoluteUri, bucket, webp.Value));
    }

    private static Result<OptimizedImage> Refuse(string code, string message) =>
        Result.Failure<OptimizedImage>(message, code);
}
