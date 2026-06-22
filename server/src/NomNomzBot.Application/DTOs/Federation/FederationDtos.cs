// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.DTOs.Federation;

// Trust directory (federation-oidc.md §4).

public sealed record FederationPeerDto(
    Guid Id,
    string InstanceId,
    string? DisplayName,
    string? BaseUrl,
    string DeploymentMode,
    string TrustState,
    DateTime FirstSeenAt,
    DateTime? LastHandshakeAt,
    IReadOnlyList<FederationPeerKeyDto> ActiveKeys
);

public sealed record FederationPeerKeyDto(
    Guid Id,
    Guid PeerId,
    string KeyId,
    string Algorithm,
    string PublicKey,
    DateTime ValidFrom,
    DateTime? ValidTo,
    bool IsActive
);

public sealed record RegisterFederationPeerRequest(
    string InstanceId,
    string? DisplayName,
    string? BaseUrl,
    string DeploymentMode,
    string PublicKey,
    string KeyId,
    string Algorithm
);

public sealed record RevokeFederationPeerRequest(string Reason, bool Blocked);

public sealed record AddFederationPeerKeyRequest(
    string PublicKey,
    string KeyId,
    string Algorithm,
    DateTime ValidFrom,
    DateTime? ValidTo
);

// Per-channel opt-in (tenant-scoped).

public sealed record ChannelFederationOptInDto(
    Guid Id,
    Guid BroadcasterId,
    Guid? PeerId,
    string OptInType,
    string Direction,
    bool IsEnabled
);

public sealed record UpsertChannelFederationOptInRequest(
    Guid? PeerId,
    string OptInType,
    string Direction,
    bool IsEnabled
);

// Handshake / instance identity.

public sealed record FederationInstanceDescriptorDto(
    string InstanceId,
    string DeploymentMode,
    string BaseUrl,
    string JwksUri,
    string SigningKeyId,
    string SigningPublicKey,
    string SigningAlgorithm
);

public sealed record FederationHandshakeRequest(
    string InstanceId,
    string DeploymentMode,
    string BaseUrl,
    string SigningKeyId,
    string SigningPublicKey,
    string SigningAlgorithm
);
