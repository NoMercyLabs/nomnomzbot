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

    /// <summary>
    /// Every local channel that accepts <paramref name="optInType"/> from this peer — an enabled <c>accept|both</c>
    /// opt-in matching <c>(peerId OR any-trusted)</c> — for the inbound broadcast fan-out (federation-oidc.md §3.7).
    /// Returns empty (never a failure) when the peer is untrusted or no channel opted in. No writes.
    /// </summary>
    Task<Result<IReadOnlyList<Guid>>> ListAcceptingBroadcasterIdsAsync(
        Guid peerId,
        string optInType,
        CancellationToken cancellationToken = default
    );
}
