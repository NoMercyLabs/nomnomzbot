// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Marketplace.Events;

/// <summary>
/// A content bundle was installed on a channel (marketplace.md §2) — fired after the import created the
/// entities and recorded the <c>InstalledBundle</c> row. <see cref="Capabilities"/> is the D4 capability
/// summary (what the bundle's content can do), so consumers can surface it without re-inspecting the ZIP.
/// </summary>
public sealed class BundleInstalledEvent : DomainEventBase
{
    public required Guid InstalledBundleId { get; init; }
    public required string Name { get; init; }

    /// <summary><c>local</c> | <c>marketplace</c>.</summary>
    public required string Source { get; init; }

    public required IReadOnlyList<string> Capabilities { get; init; }
}
