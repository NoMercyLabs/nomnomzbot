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

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Federation.Entities;

/// <summary>
/// A versioned public key a peer signs its events with (federation-oidc.md §1, schema D.2). GLOBAL. A peer may
/// rotate keys — only <see cref="IsActive"/> keys within their validity window verify inbound signatures.
/// </summary>
public class FederationPeerKey : BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid PeerId { get; set; }

    /// <summary>The PEM/base64 public key.</summary>
    public string PublicKey { get; set; } = null!;

    /// <summary>The signing algorithm (<see cref="Enums.FederationKeyAlgorithm"/>).</summary>
    public string Algorithm { get; set; } = null!;

    /// <summary>The key version identifier, carried in each signed envelope.</summary>
    public string KeyId { get; set; } = null!;

    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public bool IsActive { get; set; }
}
