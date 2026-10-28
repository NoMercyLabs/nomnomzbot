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

using NomNomzBot.Domain.Federation.Enums;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Federation.Entities;

/// <summary>
/// A known peer instance in the federation trust directory (federation-oidc.md §1, schema D.1). GLOBAL — no
/// <c>BroadcasterId</c> — and default-deny: a peer is <see cref="FederationTrustState.Pending"/> until explicitly
/// trusted. Soft-deletable.
/// </summary>
public class FederationPeer : SoftDeletableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The peer's stable instance identifier (unique).</summary>
    public string InstanceId { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? BaseUrl { get; set; }

    /// <summary>The peer's deployment mode (<see cref="Identity.Enums.AuthEnums.DeploymentMode"/> vocabulary).</summary>
    public string DeploymentMode { get; set; } = null!;

    public string TrustState { get; set; } = FederationTrustState.Pending;
    public DateTime FirstSeenAt { get; set; }
    public DateTime? LastHandshakeAt { get; set; }
}
