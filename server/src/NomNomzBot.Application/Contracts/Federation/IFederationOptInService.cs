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
using NomNomzBot.Application.DTOs.Federation;

namespace NomNomzBot.Application.Contracts.Federation;

/// <summary>
/// Per-channel federation opt-in (federation-oidc.md §3.4), tenant-scoped and default-deny: a channel shares or
/// accepts nothing until it creates an enabled opt-in. <see cref="IsActionPermittedAsync"/> is the pure predicate
/// the bus adapters consult before propagating.
/// </summary>
public interface IFederationOptInService
{
    /// <summary>This channel's opt-ins.</summary>
    Task<Result<IReadOnlyList<ChannelFederationOptInDto>>> ListAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Creates or updates one <c>(channel, peer, type)</c> opt-in (the explicit allow); emits the change event.</summary>
    Task<Result<ChannelFederationOptInDto>> UpsertAsync(
        Guid broadcasterId,
        UpsertChannelFederationOptInRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Disables (soft-deletes) one opt-in; emits the change event with <c>IsEnabled=false</c>.</summary>
    Task<Result> DisableAsync(
        Guid broadcasterId,
        Guid optInId,
        Guid actingUserId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Does this channel currently permit <paramref name="direction"/> of <paramref name="optInType"/> with this
    /// peer? Honors null-peer ("any trusted"), <c>both</c> direction, and requires the peer to be trusted. No writes.
    /// </summary>
    Task<Result<bool>> IsActionPermittedAsync(
        Guid broadcasterId,
        Guid peerId,
        string optInType,
        string direction,
        CancellationToken cancellationToken = default
    );
}
