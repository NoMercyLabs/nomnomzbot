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
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Identity.Entities;

/// <summary>
/// The persisted per-tenant / per-subject DEK registry row (schema Q.1) — DEK metadata, never a raw key.
/// Other tables (<c>IntegrationToken.EncryptionKeyId</c>, <c>Users.SubjectKeyId</c>, …) FK to <see cref="Id"/>;
/// destroying a row crypto-shreds every ciphertext sealed under it (the GDPR O(1) erasure linchpin).
///
/// Global/tenant-mixed: <see cref="BroadcasterId"/> is null for <c>subject</c>/<c>platform</c>-scope keys, so
/// this entity deliberately does NOT implement <see cref="ITenantScoped"/> — its rows must be readable across
/// tenants (a viewer's subject DEK is not owned by one channel), and it is never soft-deleted (destroy is the
/// <see cref="Status"/> flip to <c>destroyed</c> plus nulling <see cref="WrappedKeyMaterial"/>, not a row delete).
/// </summary>
public class CryptoKey : BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary><c>tenant</c> | <c>subject</c> | <c>platform</c> (schema [VC:enum]).</summary>
    [MaxLength(20)]
    public string KeyScope { get; set; } = null!;

    /// <summary>Owning tenant for <c>tenant</c>-scope keys; null for <c>subject</c>/<c>platform</c>.</summary>
    public Guid? BroadcasterId { get; set; }

    /// <summary>Hashed user id for <c>subject</c>-scope DEKs (SHA-256 hex of <c>provider:subject</c>).</summary>
    [MaxLength(64)]
    public string? SubjectIdHash { get; set; }

    /// <summary>DEK ciphertext wrapped by the KEK (envelope); base64. Null once crypto-shredded.</summary>
    public string? WrappedKeyMaterial { get; set; }

    /// <summary>KMS/key-vault KEK id (SaaS) or local-AES ref (self-host). Null once crypto-shredded.</summary>
    [MaxLength(255)]
    public string? KekReference { get; set; }

    /// <summary><c>local_aes</c> | <c>kms_envelope</c> (→ <c>DeploymentProfile.TokenVault</c>).</summary>
    [MaxLength(20)]
    public string Provider { get; set; } = null!;

    /// <summary>AEAD algorithm, e.g. <c>AES-256-GCM</c>.</summary>
    [MaxLength(30)]
    public string Algorithm { get; set; } = null!;

    /// <summary><c>active</c> | <c>rotating</c> | <c>destroyed</c>. Destroy = crypto-shred (O(1)).</summary>
    [MaxLength(20)]
    public string Status { get; set; } = null!;

    public DateTime? DestroyedAt { get; set; }

    /// <summary>The erasure request that destroyed this key (audit linkage).</summary>
    public Guid? ErasureRequestId { get; set; }

    /// <summary>
    /// Monotonic DEK version (default <c>1</c>, incremented on rotation). Bound into the AEAD AAD
    /// (<c>CipherAad.KeyVersion</c>) so ciphertext cannot be replayed under a rotated key.
    /// </summary>
    public int KeyVersion { get; set; } = 1;

    /// <summary>Predecessor DEK id on rotation; null for an original key.</summary>
    public Guid? RotatedFromKeyId { get; set; }
}
