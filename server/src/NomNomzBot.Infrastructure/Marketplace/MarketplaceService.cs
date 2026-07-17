// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Marketplace;
using NomNomzBot.Application.Marketplace.Services;

namespace NomNomzBot.Infrastructure.Marketplace;

/// <summary>
/// <see cref="IMarketplaceService"/> — the hosted-marketplace flows over the client + the ONE local import
/// path (marketplace.md §6). Install: rate-gate → download the item's ZIP → <c>ImportAsync</c> with the
/// marketplace identity, so the (BroadcasterId, Source, MarketplaceItemId) unique key turns a re-install
/// into an update. Publish: rate-gate → local inspect (refuse a bundle with issues BEFORE any bytes leave
/// the box) → multipart upload into the vetting queue. Per-channel window buckets: install 10/hour,
/// publish 5/hour; a denial carries the Retry-After seconds in the failure detail.
/// </summary>
public sealed class MarketplaceService : IMarketplaceService
{
    private const int InstallsPerHour = 10;
    private const int PublishesPerHour = 5;
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly IMarketplaceClient _client;
    private readonly IBundleImportService _import;
    private readonly IRateLimiterPartitionStore _rateLimiter;

    public MarketplaceService(
        IMarketplaceClient client,
        IBundleImportService import,
        IRateLimiterPartitionStore rateLimiter
    )
    {
        _client = client;
        _import = import;
        _rateLimiter = rateLimiter;
    }

    public async Task<Result<InstalledBundleDto>> InstallAsync(
        Guid broadcasterId,
        Guid actorUserId,
        string itemId,
        ImportConflictPolicy policy,
        CancellationToken ct = default
    )
    {
        Result limited = await AcquireAsync(broadcasterId, "install", InstallsPerHour, ct);
        if (limited.IsFailure)
            return Result.Failure<InstalledBundleDto>(
                limited.ErrorMessage!,
                limited.ErrorCode!,
                limited.ErrorDetail
            );

        Result<System.IO.Stream> download = await _client.DownloadAsync(itemId, ct);
        if (download.IsFailure)
            return download.ToTyped<InstalledBundleDto>();

        await using System.IO.Stream zip = download.Value;
        return await _import.ImportAsync(
            broadcasterId,
            actorUserId,
            zip,
            policy,
            marketplaceItemId: itemId,
            ct
        );
    }

    public async Task<Result<PublishSubmissionDto>> PublishAsync(
        Guid broadcasterId,
        System.IO.Stream zip,
        PublishMetadata metadata,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(metadata.Name))
            return Result.Failure<PublishSubmissionDto>(
                "The listing needs a name.",
                "VALIDATION_FAILED"
            );
        if (string.IsNullOrWhiteSpace(metadata.Version))
            return Result.Failure<PublishSubmissionDto>(
                "The listing needs a version.",
                "VALIDATION_FAILED"
            );

        Result limited = await AcquireAsync(broadcasterId, "publish", PublishesPerHour, ct);
        if (limited.IsFailure)
            return Result.Failure<PublishSubmissionDto>(
                limited.ErrorMessage!,
                limited.ErrorCode!,
                limited.ErrorDetail
            );

        // Buffer once (under the bundle cap) so the SAME bytes are inspected locally and then uploaded.
        MemoryStream buffer = new();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = await zip.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > BundleFormat.MaxBundleBytes)
                return Result.Failure<PublishSubmissionDto>(
                    $"The bundle is larger than {BundleFormat.MaxBundleBytes / (1024 * 1024)} MB.",
                    "BUNDLE_TOO_LARGE"
                );
            buffer.Write(chunk, 0, read);
        }
        buffer.Position = 0;

        // Inspect-validate locally FIRST (D5): a bundle that fails inspection never leaves the box.
        Result<BundleInspection> inspection = await _import.InspectAsync(broadcasterId, buffer, ct);
        if (inspection.IsFailure)
            return inspection.ToTyped<PublishSubmissionDto>();
        if (inspection.Value.Issues.Count > 0)
            return Result.Failure<PublishSubmissionDto>(
                $"The bundle failed inspection: {string.Join(" | ", inspection.Value.Issues)}",
                "BUNDLE_INVALID"
            );

        buffer.Position = 0;
        return await _client.PublishAsync(broadcasterId, buffer, metadata, ct);
    }

    /// <summary>Per-channel per-operation window bucket; the denial carries the Retry-After seconds in the detail.</summary>
    private async Task<Result> AcquireAsync(
        Guid broadcasterId,
        string operation,
        int permitLimit,
        CancellationToken ct
    )
    {
        RateLimitLease lease = await _rateLimiter.AcquireAsync(
            $"marketplace:{broadcasterId}:{operation}",
            permitLimit,
            Window,
            ct
        );
        if (lease.IsAcquired)
            return Result.Success();
        int retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(lease.RetryAfter.TotalSeconds));
        return Result.Failure(
            $"Rate limit exceeded for '{operation}' — retry in {retryAfterSeconds}s.",
            "RATE_LIMITED",
            retryAfterSeconds.ToString()
        );
    }
}
