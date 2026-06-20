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

namespace NomNomzBot.Domain.Identity.Entities;

/// <summary>
/// A live login on one device (schema A.3) — the parent of a rotating refresh-token chain. One row per
/// device session; revoking it severs every refresh token under it. <c>BroadcasterId</c> is the tenant the
/// session is acting in (null until a channel is resolved), so it is scoped explicitly by the session
/// service rather than via the non-nullable <c>ITenantScoped</c> filter.
/// </summary>
public class AuthSession : BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    // Tenant (channel) the session acts in; null when no channel is resolved yet.
    public Guid? BroadcasterId { get; set; }

    [MaxLength(20)]
    public string ClientType { get; set; } = null!;

    // AES-256-GCM-sealed source IP (schema A.3, [PII-shred]); null when not captured.
    [MaxLength(255)]
    public string? IpAddressCipher { get; set; }

    [MaxLength(512)]
    public string? UserAgent { get; set; }

    public DateTime LastSeenAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    [ForeignKey(nameof(UserId))]
    public virtual User User { get; set; } = null!;

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel? Channel { get; set; }

    public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}
