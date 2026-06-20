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

namespace NomNomzBot.Domain.Integrations.Entities;

/// <summary>
/// One vaulted OAuth secret for a connection (schema E.2) — the access, refresh, or app token. Replaces
/// <c>Service.AccessToken/RefreshToken</c>. <see cref="CipherText"/> is the AES-256-GCM sealed envelope
/// (the DEK that opens it is referenced by <see cref="EncryptionKeyId"/>); destroying that DEK crypto-shreds
/// the token. <c>BroadcasterId</c> is denormalized from the connection for RLS and is null for
/// platform/global tokens.
/// </summary>
public class IntegrationToken : SoftDeletableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ConnectionId { get; set; }

    // Denormalized tenant for RLS; null = platform/global. Matches the owning connection.
    public Guid? BroadcasterId { get; set; }

    [MaxLength(10)]
    public string TokenType { get; set; } = null!;

    // The sealed AEAD envelope ([PII-shred]); never plaintext at rest.
    public string CipherText { get; set; } = null!;

    [MaxLength(64)]
    public string? Nonce { get; set; }

    // The DEK that opens CipherText (FK→CryptoKey registry). Destroying it shreds this token.
    public Guid EncryptionKeyId { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public DateTime? RotatedAt { get; set; }

    [ForeignKey(nameof(ConnectionId))]
    public virtual IntegrationConnection Connection { get; set; } = null!;

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel? Channel { get; set; }
}
