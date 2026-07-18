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

    /// <summary>
    /// The multi-file project source (dev-platform.md §4): raw JSON of a <c>path → content</c> map, e.g.
    /// <c>{"index.tsx":"…","lib/util.ts":"…"}</c>. A single-file authoring save is just a one-entry map. Null only on
    /// legacy rows the backfill has not touched; the build reads the project from here + <see cref="ManifestJson"/>.
    /// </summary>
    public string? FilesJson { get; set; }

    /// <summary>
    /// Raw JSON of the project manifest — <c>{ entry, kind, framework, dependencies[] }</c> (dev-platform.md §4.2).
    /// The <c>entry</c> path names the module esbuild bundles from; <c>dependencies</c> is allowlist-checked.
    /// </summary>
    public string? ManifestJson { get; set; }

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
