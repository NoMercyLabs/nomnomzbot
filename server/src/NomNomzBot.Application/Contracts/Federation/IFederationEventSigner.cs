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
