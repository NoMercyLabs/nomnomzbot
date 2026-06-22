// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
