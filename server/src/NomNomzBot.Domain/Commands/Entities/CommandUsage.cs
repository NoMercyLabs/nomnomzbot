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
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Commands.Entities;

/// <summary>
/// Append-only usage record per command invocation. <see cref="ArgsSnapshot"/> is PII-scrubbed
/// at write time. Schema: M.5 (commands-pipelines.md §1).
/// </summary>
public class CommandUsage : BaseEntity, ITenantScoped
{
    public long Id { get; set; }

    public Guid BroadcasterId { get; set; }

    /// <summary>Null for built-in commands not backed by a <see cref="Command"/> row.</summary>
    public Guid? CommandId { get; set; }

    [MaxLength(100)]
    public string CommandNameSnapshot { get; set; } = null!;

    public Guid ViewerProfileId { get; set; }

    public Guid ViewerUserId { get; set; }

    /// <summary>PII-scrubbed snapshot of args (truncated, no viewer identifiers).</summary>
    [MaxLength(500)]
    public string? ArgsSnapshot { get; set; }

    public bool WasSuccessful { get; set; }

    [ForeignKey(nameof(CommandId))]
    public virtual Command? Command { get; set; }
}
