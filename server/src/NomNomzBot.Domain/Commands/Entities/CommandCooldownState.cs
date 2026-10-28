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
