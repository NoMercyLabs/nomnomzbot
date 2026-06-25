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
