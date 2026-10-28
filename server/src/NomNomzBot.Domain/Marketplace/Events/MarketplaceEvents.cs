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

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Marketplace.Events;

/// <summary>
/// A content bundle was installed on a channel (marketplace.md §2) — fired after the import created the
/// entities and recorded the <c>InstalledBundle</c> row. <see cref="Capabilities"/> is the D4 capability
/// summary (what the bundle's content can do), so consumers can surface it without re-inspecting the ZIP.
/// </summary>
public sealed class BundleInstalledEvent : DomainEventBase
{
    /// <summary>The id of the installed bundle.</summary>
    public required Guid InstalledBundleId { get; init; }

    /// <summary>The name of the bundle.</summary>
    public required string Name { get; init; }

    /// <summary><c>local</c> | <c>marketplace</c>.</summary>
    public required string Source { get; init; }

    /// <summary>The capabilities the bundle needs, as a list of names.</summary>
    public required IReadOnlyList<string> Capabilities { get; init; }
}
