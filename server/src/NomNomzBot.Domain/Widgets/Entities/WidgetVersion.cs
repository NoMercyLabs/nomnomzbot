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

namespace NomNomzBot.Domain.Widgets.Entities;

/// <summary>
/// An immutable, compiled version of a <see cref="Widget"/> (schema §P.7, APPEND-ONLY). Compile-on-save records the
/// authored <see cref="SourceCode"/>, the esbuild <see cref="CompiledBundle"/> + <see cref="ContentHash"/> (the
/// cache-bust key), and the build outcome; a correction is a NEW version, never an edit (tamper-evident history).
/// A failed build is a persisted <c>error</c> row, not a discard. One row per <c>(widget, version)</c>.
/// </summary>
public class WidgetVersion : ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid WidgetId { get; set; }
    public Guid BroadcasterId { get; set; }

    public int VersionNumber { get; set; }

    public string? SourceCode { get; set; }
    public string? CompiledBundle { get; set; }

    /// <summary><c>pending</c> | <c>success</c> | <c>error</c>.</summary>
    public string BuildStatus { get; set; } = "pending";
    public string? BuildError { get; set; }
    public string? BuildLog { get; set; }

    /// <summary>sha256 of the compiled bundle (64 hex) — the overlay cache-bust key; null until a successful build.</summary>
    public string? ContentHash { get; set; }

    public DateTime? CompiledAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
