// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.EventStore.Entities;

/// <summary>
/// Per-projection consume cursor (schema O.3). One row per <c>(ProjectionName, BroadcasterId)</c> tracks how
/// far a read-model has folded the journal, so independent replay/backfill can resume. NOT append-only — it
/// carries <see cref="UpdatedAt"/> and is updated in place as the projection advances. Standalone keyed
/// entity; does not inherit <c>BaseEntity</c> and is not ambient tenant-filtered (the runner manages global
/// and per-tenant checkpoints across tenants by design).
/// </summary>
public class ProjectionCheckpoint
{
    /// <summary>Surrogate key (<c>bigint</c> identity).</summary>
    public long Id { get; set; }

    /// <summary>Stable projection name; matches <c>IProjection.Name</c>. Unique with <see cref="BroadcasterId"/>.</summary>
    public string ProjectionName { get; set; } = null!;

    /// <summary>Tenant scope; <c>null</c> = the global cross-tenant checkpoint for a global projection.</summary>
    public Guid? BroadcasterId { get; set; }

    /// <summary>The last <c>StreamPosition</c> (per-tenant) or <c>Id</c> (global) the projection has applied.</summary>
    public long LastPosition { get; set; }

    /// <summary><c>running</c>|<c>rebuilding</c>|<c>faulted</c>|<c>paused</c>.</summary>
    public string Status { get; set; } = null!;

    /// <summary>The fault detail when <see cref="Status"/> is <c>faulted</c>.</summary>
    public string? LastError { get; set; }

    /// <summary>When the projection last applied an event.</summary>
    public DateTime? LastProcessedAt { get; set; }

    /// <summary>Last checkpoint mutation time.</summary>
    public DateTime UpdatedAt { get; set; }
}
