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
/// The global federation trust directory (federation-oidc.md §3.1). Peers register as <c>pending</c> and are
/// promoted to <c>trusted</c> by an explicit manual step; trust requires a usable <c>rsa-sha256</c> key. Revoke
/// is reversible, block is terminal.
/// </summary>
public interface IFederationPeerService
{
    Task<Result<PagedList<FederationPeerDto>>> ListPeersAsync(
        PaginationParams pagination,
        string? trustStateFilter,
        CancellationToken cancellationToken = default
    );

    Task<Result<FederationPeerDto>> GetPeerAsync(
        Guid peerId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Registers a peer as pending with its initial key. Idempotent on InstanceId (returns the existing peer).</summary>
    Task<Result<FederationPeerDto>> RegisterPeerAsync(
        RegisterFederationPeerRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Promotes a pending peer to trusted; requires an active rsa-sha256 key; emits the trusted event.</summary>
    Task<Result<FederationPeerDto>> TrustPeerAsync(
        Guid peerId,
        Guid actingUserId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Revokes (reversible) or blocks (terminal) a peer; deactivates its keys; emits the revoked event.</summary>
    Task<Result> RevokePeerAsync(
        Guid peerId,
        RevokeFederationPeerRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Adds a rotated public key (new KeyId) for a peer. Unique (PeerId, KeyId).</summary>
    Task<Result<FederationPeerKeyDto>> AddPeerKeyAsync(
        Guid peerId,
        AddFederationPeerKeyRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Retires a peer key (IsActive=false) — rotation or compromise; inbound verification stops using it.</summary>
    Task<Result> DeactivatePeerKeyAsync(
        Guid peerId,
        string keyId,
        CancellationToken cancellationToken = default
    );
}
