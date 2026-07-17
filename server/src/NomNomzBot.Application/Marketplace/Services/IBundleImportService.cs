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
/// Validates and installs bundle ZIPs, and manages the channel's installed-bundle ledger
/// (marketplace.md §3). Imports create entities through the owning module services (each runs its own
/// validation), land any custom-code content DISABLED (D4), and record an <c>InstalledBundle</c> row so the
/// install can be uninstalled exactly (D6).
/// </summary>
public interface IBundleImportService
{
    /// <summary>
    /// Parse + validate a ZIP without installing anything: returns the manifest, the D4 capability summary,
    /// and any issues. A bundle with issues is not importable.
    /// </summary>
    Task<Result<BundleInspection>> InspectAsync(
        Guid broadcasterId,
        Stream zip,
        CancellationToken ct = default
    );

    /// <summary>
    /// Install a ZIP: creates the entities (custom code disabled per D4) under the given conflict policy,
    /// records an <c>InstalledBundle</c>, and emits <c>BundleInstalledEvent</c>. A bundle with inspection
    /// issues is rejected and nothing is created.
    /// </summary>
    Task<Result<InstalledBundleDto>> ImportAsync(
        Guid broadcasterId,
        Guid actorUserId,
        Stream zip,
        ImportConflictPolicy policy,
        CancellationToken ct = default
    );

    Task<Result<IReadOnlyList<InstalledBundleDto>>> ListInstalledAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Remove exactly the entities the install created (soft deletes) and retire the ledger row.</summary>
    Task<Result> UninstallAsync(
        Guid broadcasterId,
        Guid installedBundleId,
        Guid actorUserId,
        CancellationToken ct = default
    );
}

/// <summary>The result of inspecting a ZIP: what it is, what it can do (D4), and what is wrong with it.</summary>
public sealed record BundleInspection(
    BundleManifest Manifest,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Issues
);

/// <summary>
/// What to do when a bundle item's name already exists on the channel (marketplace.md D6):
/// <c>Rename</c> (default) installs under a suffixed name, <c>Overwrite</c> replaces the existing entity,
/// <c>Skip</c> leaves the existing entity and installs nothing for that item.
/// </summary>
public enum ImportConflictPolicy
{
    Rename,
    Overwrite,
    Skip,
}

/// <summary>One row of the channel's installed-bundle ledger.</summary>
public sealed record InstalledBundleDto(
    Guid Id,
    string Name,
    string Source,
    string? MarketplaceItemId,
    string Version,
    DateTime InstalledAt
);
