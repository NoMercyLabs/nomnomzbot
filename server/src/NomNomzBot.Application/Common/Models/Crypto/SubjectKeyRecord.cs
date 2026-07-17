// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Common.Models.Crypto;

/// <summary>
/// One data-encryption-key (DEK) registry entry — the persisted projection of a <c>CryptoKey</c> row
/// (schema Q.1) that the crypto core needs. The full erasure subsystem owns the EF entity and its extra
/// columns; the crypto core reads / writes only these fields through <c>ISubjectKeyStore</c>.
///
/// Crypto-shred is the absence of <see cref="WrappedKeyMaterial"/>: when the wrapped DEK is nulled and
/// <see cref="Status"/> flips to <see cref="SubjectKeyStatus.Destroyed"/>, the DEK can never be
/// reconstructed, so every ciphertext sealed under it is permanently unrecoverable.
/// </summary>
public sealed record SubjectKeyRecord
{
    public required Guid Id { get; init; }
    public required string KeyScope { get; init; } // tenant | subject | platform
    public Guid? BroadcasterId { get; init; }
    public string? SubjectIdHash { get; init; }

    /// <summary>Wrapped DEK ciphertext (base64). Null once crypto-shredded.</summary>
    public string? WrappedKeyMaterial { get; init; }
    public string? KekReference { get; init; }

    public required string Provider { get; init; } // local_aes | kms_envelope
    public required string Algorithm { get; init; } // AES-256-GCM
    public required string KeyVersion { get; init; } // AAD component; monotonic per key lineage
    public required SubjectKeyStatus Status { get; init; }
    public DateTime? DestroyedAt { get; init; }
    public Guid? ErasureRequestId { get; init; }

    /// <summary>Predecessor DEK id when this key was minted by a rotation; null for an original key.</summary>
    public Guid? RotatedFromKeyId { get; init; }
}

/// <summary>Lifecycle of a DEK registry entry. <c>Destroyed</c> is the crypto-shred terminal state.</summary>
public enum SubjectKeyStatus
{
    Active,
    Rotating,
    Destroyed,
}
