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
/// A channel's explicit opt-in to share/accept a federation flow (federation-oidc.md §1, schema D.3). Tenant-scoped
/// and default-deny: a channel shares/accepts nothing until an enabled row exists. A null <see cref="PeerId"/>
/// means "any trusted peer". Soft-deletable.
/// </summary>
public class ChannelFederationOptIn : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }

    /// <summary>The specific peer this opt-in applies to, or null for any trusted peer.</summary>
    public Guid? PeerId { get; set; }

    /// <summary>What is shared/accepted (<see cref="FederationOptInType"/>).</summary>
    public string OptInType { get; set; } = null!;

    /// <summary>The flow direction (<see cref="FederationDirection"/>).</summary>
    public string Direction { get; set; } = FederationDirection.Both;

    public bool IsEnabled { get; set; }
    public Guid? EnabledByUserId { get; set; }
}
