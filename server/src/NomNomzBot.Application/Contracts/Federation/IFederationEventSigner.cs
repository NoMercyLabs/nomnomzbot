// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.Federation;

/// <summary>
/// Per-message federation signatures (federation-oidc.md §3.3), rsa-sha256 over the canonical (sorted-key, UTF-8)
/// JSON of the envelope. This instance signs and verifies rsa-sha256 only; verification fails closed on an unknown
/// key, an inactive/expired key, or any non-rsa-sha256 algorithm.
/// </summary>
public interface IFederationEventSigner
{
    /// <summary>Signs the envelope with this instance's active private signing key. Returns the signature + KeyId.</summary>
    Task<Result<FederationSignature>> SignAsync(
        FederationEventEnvelope envelope,
        CancellationToken cancellationToken = default
    );

    /// <summary>Verifies a peer envelope's signature against the peer's matching active key. No DB write.</summary>
    Task<Result> VerifyAsync(
        Guid peerId,
        FederationEventEnvelope envelope,
        FederationSignature signature,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Supplies this instance's active rsa-sha256 signing key (the issuer's RS256 private key material). Backed by
/// configuration until the OIDC issuer key vault lands.
/// </summary>
public interface IFederationSigningKeyProvider
{
    /// <summary>The instance's active private signing key (PEM) + its KeyId, or failure when not configured.</summary>
    Result<FederationSigningKey> GetActiveSigningKey();
}

/// <summary>An instance signing key: its KeyId and PEM-encoded private key.</summary>
public sealed record FederationSigningKey(string KeyId, string PrivateKeyPem);
