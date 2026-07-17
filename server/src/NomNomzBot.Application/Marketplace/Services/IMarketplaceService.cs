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
/// The hosted-marketplace flows that compose the client with the local bundle machinery (marketplace.md
/// §5–§6): install downloads the item's ZIP and runs the ONE import path with the marketplace identity (so
/// re-install updates instead of duplicating), publish inspect-validates the ZIP locally BEFORE uploading
/// into vetting. Both are rate-limited per channel (install 10/h, publish 5/h) with the Retry-After seconds
/// in the failure detail.
/// </summary>
public interface IMarketplaceService
{
    /// <summary>Download the marketplace item and install it under the given conflict policy.</summary>
    Task<Result<InstalledBundleDto>> InstallAsync(
        Guid broadcasterId,
        Guid actorUserId,
        string itemId,
        ImportConflictPolicy policy,
        CancellationToken ct = default
    );

    /// <summary>Inspect the ZIP locally (refuse on issues), then submit it into the vetting queue.</summary>
    Task<Result<PublishSubmissionDto>> PublishAsync(
        Guid broadcasterId,
        Stream zip,
        PublishMetadata metadata,
        CancellationToken ct = default
    );
}
