// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Commands.Entities;

/// <summary>
/// Durable write-through cooldown state for a command: per-user or global.
/// No soft-delete — rows are TTL-swept when <see cref="ExpiresAt"/> passes.
/// Schema: G.3 (commands-pipelines.md §1).
/// </summary>
public class CommandCooldownState : BaseEntity, ITenantScoped
{
    public long Id { get; set; }

    public Guid CommandId { get; set; }

    public Guid BroadcasterId { get; set; }

    /// <summary>Null = global cooldown for this command; non-null = per-user cooldown.</summary>
    public Guid? UserId { get; set; }

    public DateTime LastInvokedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    [ForeignKey(nameof(CommandId))]
    public virtual Command Command { get; set; } = null!;
}
