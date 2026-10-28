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

namespace NomNomzBot.Domain.Identity.Entities;

/// <summary>
/// A single, hashed, single-use rotating refresh token (schema A.4). The raw value is returned to the
/// client exactly once; only its SHA-256 hash is stored. Each rotation consumes the current token and
/// issues a successor (with <see cref="PreviousTokenHash"/> set), forming a chain. Presenting an already
/// consumed/revoked token is reuse — the whole session lineage is revoked.
/// </summary>
public class RefreshToken : BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SessionId { get; set; }

    public Guid UserId { get; set; }

    // SHA-256 hex of the raw token (schema A.4) — the raw value is never persisted.
    [MaxLength(64)]
    public string TokenHash { get; set; } = null!;

    [MaxLength(64)]
    public string? PreviousTokenHash { get; set; }

    public DateTime IssuedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? ConsumedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    [MaxLength(30)]
    public string? RevokedReason { get; set; }

    [ForeignKey(nameof(SessionId))]
    public virtual AuthSession Session { get; set; } = null!;

    [ForeignKey(nameof(UserId))]
    public virtual User User { get; set; } = null!;
}
