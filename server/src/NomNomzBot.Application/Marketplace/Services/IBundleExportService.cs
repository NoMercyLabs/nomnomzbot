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
