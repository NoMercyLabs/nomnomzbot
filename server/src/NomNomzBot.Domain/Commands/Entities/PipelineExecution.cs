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

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Commands.Entities;

/// <summary>
/// Append-only telemetry record for a pipeline run. PII is excluded from
/// <see cref="StepLogsJson"/>; rows are TTL-purged. Schema: H.4 (commands-pipelines.md §1).
/// </summary>
public class PipelineExecution : BaseEntity, ITenantScoped
{
    public long Id { get; set; }

    /// <summary>Null for a builtin/inline run that has no backing Pipeline row (see TriggerKind).</summary>
    public Guid? PipelineId { get; set; }

    public Guid BroadcasterId { get; set; }

    public Guid? TriggeredByUserId { get; set; }

    [MaxLength(30)]
    public string TriggerKind { get; set; } = null!;

    /// <summary>Run outcome: success | failed | timeout | denied.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = null!;

    /// <summary>How many host calls the run made, summed over the <c>HostCalls</c> of every step log.</summary>
    public int HostCallCount { get; set; }

    public int DurationMs { get; set; }

    [MaxLength(1000)]
    public string? ErrorMessage { get; set; }

    /// <summary>What kind of failure ended the run; null on success. Set where the failure happens.</summary>
    public RunFailureKind? FailureKind { get; set; }

    /// <summary>The stable name of the limit that stopped the run (for example <c>script.wall_clock_ms</c>);
    /// null unless a limit or a time budget stopped it.</summary>
    [MaxLength(60)]
    public string? LimitKey { get; set; }

    /// <summary>The value of <see cref="LimitKey"/> in force, in the unit the key names.</summary>
    public long? LimitValue { get; set; }

    /// <summary>
    /// Bounded, PII-excluded step execution logs (JSON array). TTL-purged; null until persisted
    /// by the engine.
    /// </summary>
    public string? StepLogsJson { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    [ForeignKey(nameof(PipelineId))]
    public virtual Pipeline? Pipeline { get; set; }
}
