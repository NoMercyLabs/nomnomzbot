// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Commands.Entities;

/// <summary>
/// Append-only telemetry record for a pipeline run. PII is excluded from
/// <see cref="StepLogsJson"/>; rows are TTL-purged. Schema: H.4 (commands-pipelines.md §1).
/// </summary>
public class PipelineExecution : BaseEntity, ITenantScoped
{
    public long Id { get; set; }

    public Guid PipelineId { get; set; }

    public Guid BroadcasterId { get; set; }

    public Guid? TriggeredByUserId { get; set; }

    [MaxLength(30)]
    public string TriggerKind { get; set; } = null!;

    /// <summary>Run outcome: success | failed | timeout | denied.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = null!;

    public int HostCallCount { get; set; }

    public int DurationMs { get; set; }

    [MaxLength(1000)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Bounded, PII-excluded step execution logs (JSON array). TTL-purged; null until persisted
    /// by the engine.
    /// </summary>
    public string? StepLogsJson { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    [ForeignKey(nameof(PipelineId))]
    public virtual Pipeline Pipeline { get; set; } = null!;
}
