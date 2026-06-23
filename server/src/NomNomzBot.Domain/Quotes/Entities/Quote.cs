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
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Quotes.Entities;

/// <summary>
/// A numbered, searchable channel quote (schema G.5). <see cref="Number"/> is per-channel monotonic —
/// allocated via <c>ITenantSequenceAllocator</c> (gap-free) and never reused: a soft-deleted row keeps its
/// number so deleting <c>#2</c> never frees <c>2</c> for a future quote.
/// </summary>
public class Quote : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; }
    public Guid BroadcasterId { get; set; }

    /// <summary>Per-channel monotonic quote number (D1). Stable and never reused.</summary>
    public int Number { get; set; }

    [MaxLength(500)]
    public string Text { get; set; } = null!;

    /// <summary>Who said it.</summary>
    [MaxLength(100)]
    public string? QuotedDisplayName { get; set; }

    /// <summary>Game/category at the time the line was said.</summary>
    [MaxLength(100)]
    public string? ContextGame { get; set; }

    /// <summary>When it was said (defaults to creation time when not supplied).</summary>
    public DateTime? QuotedAt { get; set; }

    /// <summary>The author who added the quote.</summary>
    public Guid? CreatedByUserId { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}
