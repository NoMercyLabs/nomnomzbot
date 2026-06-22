// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Federation;
using NomNomzBot.Application.DTOs.Federation;
using NomNomzBot.Domain.Federation.Entities;
using NomNomzBot.Domain.Federation.Enums;
using NomNomzBot.Domain.Federation.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Federation;

/// <summary>
/// The global federation trust directory (federation-oidc.md §3.1). Default-deny: peers register pending. A peer
/// cannot be promoted to trusted without an active <c>rsa-sha256</c> key (an ed25519-only peer is unverifiable by
/// this build). Revoke deactivates every key so in-flight signatures stop verifying.
/// </summary>
public sealed class FederationPeerService(
    IApplicationDbContext db,
    IEventBus eventBus,
    TimeProvider clock
) : IFederationPeerService
{
    public async Task<Result<PagedList<FederationPeerDto>>> ListPeersAsync(
        PaginationParams pagination,
        string? trustStateFilter,
        CancellationToken cancellationToken = default
    )
    {
        IQueryable<FederationPeer> query = db.FederationPeers.Where(p => p.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(trustStateFilter))
            query = query.Where(p => p.TrustState == trustStateFilter);

        int total = await query.CountAsync(cancellationToken);
        List<FederationPeer> peers = await query
            .OrderByDescending(p => p.FirstSeenAt)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(cancellationToken);

        List<Guid> ids = [.. peers.Select(p => p.Id)];
        List<FederationPeerKey> keys = await db
            .FederationPeerKeys.Where(k => ids.Contains(k.PeerId) && k.IsActive)
            .ToListAsync(cancellationToken);
        ILookup<Guid, FederationPeerKey> keysByPeer = keys.ToLookup(k => k.PeerId);

        return Result.Success(
            new PagedList<FederationPeerDto>(
                [.. peers.Select(p => ToDto(p, keysByPeer[p.Id]))],
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    public async Task<Result<FederationPeerDto>> GetPeerAsync(
        Guid peerId,
        CancellationToken cancellationToken = default
    )
    {
        FederationPeer? peer = await db.FederationPeers.FirstOrDefaultAsync(
            p => p.Id == peerId && p.DeletedAt == null,
            cancellationToken
        );
        if (peer is null)
            return Result.Failure<FederationPeerDto>("Peer not found.", "NOT_FOUND");
        return Result.Success(await ToDtoWithKeysAsync(peer, cancellationToken));
    }

    public async Task<Result<FederationPeerDto>> RegisterPeerAsync(
        RegisterFederationPeerRequest request,
        CancellationToken cancellationToken = default
    )
    {
        FederationPeer? existing = await db.FederationPeers.FirstOrDefaultAsync(
            p => p.InstanceId == request.InstanceId && p.DeletedAt == null,
            cancellationToken
        );
        if (existing is not null) // idempotent on InstanceId
            return Result.Success(await ToDtoWithKeysAsync(existing, cancellationToken));

        DateTime now = clock.GetUtcNow().UtcDateTime;
        FederationPeer peer = new()
        {
            InstanceId = request.InstanceId,
            DisplayName = request.DisplayName,
            BaseUrl = request.BaseUrl,
            DeploymentMode = request.DeploymentMode,
            TrustState = FederationTrustState.Pending,
            FirstSeenAt = now,
        };
        db.FederationPeers.Add(peer);
        db.FederationPeerKeys.Add(
            new FederationPeerKey
            {
                PeerId = peer.Id,
                PublicKey = request.PublicKey,
                Algorithm = request.Algorithm,
                KeyId = request.KeyId,
                ValidFrom = now,
                IsActive = true,
            }
        );
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(await ToDtoWithKeysAsync(peer, cancellationToken));
    }

    public async Task<Result<FederationPeerDto>> TrustPeerAsync(
        Guid peerId,
        Guid actingUserId,
        CancellationToken cancellationToken = default
    )
    {
        FederationPeer? peer = await db.FederationPeers.FirstOrDefaultAsync(
            p => p.Id == peerId && p.DeletedAt == null,
            cancellationToken
        );
        if (peer is null)
            return Result.Failure<FederationPeerDto>("Peer not found.", "NOT_FOUND");
        if (peer.TrustState == FederationTrustState.Trusted)
            return Result.Success(await ToDtoWithKeysAsync(peer, cancellationToken)); // no-op

        bool hasUsableKey = await db.FederationPeerKeys.AnyAsync(
            k =>
                k.PeerId == peerId && k.IsActive && k.Algorithm == FederationKeyAlgorithm.RsaSha256,
            cancellationToken
        );
        if (!hasUsableKey)
            return Result.Failure<FederationPeerDto>(
                "Peer has no active rsa-sha256 key; it cannot be trusted until one is registered.",
                "VALIDATION_FAILED"
            );

        peer.TrustState = FederationTrustState.Trusted;
        peer.LastHandshakeAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);

        await eventBus.PublishAsync(
            new FederationPeerTrustedEvent
            {
                BroadcasterId = Guid.Empty, // directory-level
                PeerId = peer.Id,
                InstanceId = peer.InstanceId,
                DeploymentMode = peer.DeploymentMode,
                BaseUrl = peer.BaseUrl,
            },
            cancellationToken
        );
        return Result.Success(await ToDtoWithKeysAsync(peer, cancellationToken));
    }

    public async Task<Result> RevokePeerAsync(
        Guid peerId,
        RevokeFederationPeerRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default
    )
    {
        FederationPeer? peer = await db.FederationPeers.FirstOrDefaultAsync(
            p => p.Id == peerId && p.DeletedAt == null,
            cancellationToken
        );
        if (peer is null)
            return Result.Failure("Peer not found.", "NOT_FOUND");

        peer.TrustState = request.Blocked
            ? FederationTrustState.Blocked
            : FederationTrustState.Revoked;
        List<FederationPeerKey> keys = await db
            .FederationPeerKeys.Where(k => k.PeerId == peerId && k.IsActive)
            .ToListAsync(cancellationToken);
        foreach (FederationPeerKey key in keys)
            key.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);

        await eventBus.PublishAsync(
            new FederationPeerRevokedEvent
            {
                BroadcasterId = Guid.Empty,
                PeerId = peer.Id,
                InstanceId = peer.InstanceId,
                Reason = request.Reason,
                Blocked = request.Blocked,
            },
            cancellationToken
        );
        return Result.Success();
    }

    public async Task<Result<FederationPeerKeyDto>> AddPeerKeyAsync(
        Guid peerId,
        AddFederationPeerKeyRequest request,
        CancellationToken cancellationToken = default
    )
    {
        bool peerExists = await db.FederationPeers.AnyAsync(
            p => p.Id == peerId && p.DeletedAt == null,
            cancellationToken
        );
        if (!peerExists)
            return Result.Failure<FederationPeerKeyDto>("Peer not found.", "NOT_FOUND");
        bool duplicate = await db.FederationPeerKeys.AnyAsync(
            k => k.PeerId == peerId && k.KeyId == request.KeyId,
            cancellationToken
        );
        if (duplicate)
            return Result.Failure<FederationPeerKeyDto>(
                "A key with this KeyId already exists for the peer.",
                "ALREADY_EXISTS"
            );

        FederationPeerKey key = new()
        {
            PeerId = peerId,
            PublicKey = request.PublicKey,
            Algorithm = request.Algorithm,
            KeyId = request.KeyId,
            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo,
            IsActive = true,
        };
        db.FederationPeerKeys.Add(key);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(ToKeyDto(key));
    }

    public async Task<Result> DeactivatePeerKeyAsync(
        Guid peerId,
        string keyId,
        CancellationToken cancellationToken = default
    )
    {
        FederationPeerKey? key = await db.FederationPeerKeys.FirstOrDefaultAsync(
            k => k.PeerId == peerId && k.KeyId == keyId,
            cancellationToken
        );
        if (key is null)
            return Result.Failure("Peer key not found.", "NOT_FOUND");
        key.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<FederationPeerDto> ToDtoWithKeysAsync(
        FederationPeer peer,
        CancellationToken ct
    )
    {
        List<FederationPeerKey> keys = await db
            .FederationPeerKeys.Where(k => k.PeerId == peer.Id && k.IsActive)
            .ToListAsync(ct);
        return ToDto(peer, keys);
    }

    private static FederationPeerDto ToDto(
        FederationPeer peer,
        IEnumerable<FederationPeerKey> keys
    ) =>
        new(
            peer.Id,
            peer.InstanceId,
            peer.DisplayName,
            peer.BaseUrl,
            peer.DeploymentMode,
            peer.TrustState,
            peer.FirstSeenAt,
            peer.LastHandshakeAt,
            [.. keys.Select(ToKeyDto)]
        );

    private static FederationPeerKeyDto ToKeyDto(FederationPeerKey k) =>
        new(k.Id, k.PeerId, k.KeyId, k.Algorithm, k.PublicKey, k.ValidFrom, k.ValidTo, k.IsActive);
}
