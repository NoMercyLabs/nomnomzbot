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
/// A channel's explicit opt-in to share/accept a federation flow (federation-oidc.md §1, schema D.3). Tenant-scoped
/// and default-deny: a channel shares/accepts nothing until an enabled row exists. A null <see cref="PeerId"/>
/// means "any trusted peer". Soft-deletable.
/// </summary>
public class ChannelFederationOptIn : SoftDeletableEntity, ITenantScoped
{
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
