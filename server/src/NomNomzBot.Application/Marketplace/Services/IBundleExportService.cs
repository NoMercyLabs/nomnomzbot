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
using NomNomzBot.Application.Contracts.Marketplace;

namespace NomNomzBot.Application.Marketplace.Services;

/// <summary>
/// Builds portable bundle ZIPs from a channel's own content (marketplace.md §3). Works fully offline (D3);
/// every entity is mapped through the D2 field allowlist, so secrets/PII never leave the instance.
/// </summary>
public interface IBundleExportService
{
    /// <summary>
    /// Export the requested entities into a ZIP (manifest + per-type entries + sound assets), secrets
    /// stripped (D2). A command that executes a pipeline pulls that pipeline into the bundle automatically,
    /// so the dependency graph is always complete.
    /// </summary>
    Task<Result<Stream>> ExportAsync(
        Guid broadcasterId,
        ExportRequest request,
        CancellationToken ct = default
    );
}

/// <summary>The export order: which entities to bundle, and the bundle's author metadata.</summary>
public sealed record ExportRequest(IReadOnlyList<ExportItemRef> Items, BundleMetadata Metadata);

/// <summary>One entity to export. <see cref="Type"/> is a <see cref="BundleFormat"/> item type.</summary>
public sealed record ExportItemRef(string Type, Guid Id);
