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

namespace NomNomzBot.Application.Marketplace.Services;

/// <summary>
/// Client of the separate, NoMercy-hosted marketplace service (marketplace.md §4) — browse, download,
/// publish, and submission status. Browse/download are anonymous; publish and submission status carry the
/// channel's vaulted publisher token. Unconfigured or unreachable never throws to callers: every transport
/// problem surfaces as a typed <c>MARKETPLACE_UNAVAILABLE</c> failure.
/// </summary>
public interface IMarketplaceClient
{
    /// <summary>Search the approved listings (<c>GET /v1/items</c>). Anonymous.</summary>
    Task<Result<PagedList<MarketplaceItemDto>>> SearchAsync(
        MarketplaceQuery query,
        PaginationParams? pagination = null,
        CancellationToken ct = default
    );

    /// <summary>Read one listing (<c>GET /v1/items/{id}</c>). Anonymous.</summary>
    Task<Result<MarketplaceItemDto>> GetItemAsync(string itemId, CancellationToken ct = default);

    /// <summary>
    /// Download an item's bundle ZIP (<c>GET /v1/items/{id}/download</c>), bounded by the 20 MB bundle cap.
    /// The stream then feeds <c>IBundleImportService.ImportAsync</c>. Anonymous.
    /// </summary>
    Task<Result<Stream>> DownloadAsync(string itemId, CancellationToken ct = default);

    /// <summary>
    /// Publish a bundle ZIP into the vetting queue (<c>POST /v1/publish</c>, multipart). Sends the channel's
    /// publisher token; no token stored → <c>MARKETPLACE_NO_PUBLISHER_TOKEN</c>. Returns the submission
    /// (initially <c>pending</c>, D5).
    /// </summary>
    Task<Result<PublishSubmissionDto>> PublishAsync(
        Guid broadcasterId,
        Stream zip,
        PublishMetadata metadata,
        CancellationToken ct = default
    );

    /// <summary>Poll a submission's vetting status (<c>GET /v1/submissions/{id}</c>). Publisher token.</summary>
    Task<Result<PublishSubmissionDto>> GetSubmissionAsync(
        Guid broadcasterId,
        string submissionId,
        CancellationToken ct = default
    );
}

/// <summary>The browse filters (marketplace.md §4): free-text search, item type, and tags.</summary>
public sealed record MarketplaceQuery(
    string? Search = null,
    string? Type = null,
    IReadOnlyList<string>? Tags = null
);

/// <summary>One approved marketplace listing as the catalog serves it (marketplace.md §3).</summary>
public sealed record MarketplaceItemDto(
    string ItemId,
    string Name,
    string Author,
    string Version,
    string Summary,
    string Type,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Capabilities,
    double Rating,
    long Installs
);

/// <summary>The listing metadata a publisher supplies alongside the bundle ZIP (marketplace.md §4).</summary>
public sealed record PublishMetadata(
    string Name,
    string Version,
    string? Summary,
    IReadOnlyList<string>? Tags
);

/// <summary>A publish submission in the vetting queue: <c>pending</c> | <c>approved</c> | <c>rejected</c> (+ reason).</summary>
public sealed record PublishSubmissionDto(string SubmissionId, string Status, string? ReviewNote);
