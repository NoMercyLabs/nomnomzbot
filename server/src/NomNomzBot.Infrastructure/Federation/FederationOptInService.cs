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
/// Per-channel federation opt-in (federation-oidc.md §3.4). Default-deny: nothing propagates until an enabled
/// opt-in exists. <see cref="IsActionPermittedAsync"/> additionally requires the peer to be trusted, so a
/// disabled peer can never act even on a stale opt-in row.
/// </summary>
public sealed class FederationOptInService(
    IApplicationDbContext db,
    IEventBus eventBus,
    TimeProvider clock
) : IFederationOptInService
{
    public async Task<Result<IReadOnlyList<ChannelFederationOptInDto>>> ListAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<ChannelFederationOptIn> rows = await db
            .ChannelFederationOptIns.Where(o =>
                o.BroadcasterId == broadcasterId && o.DeletedAt == null
            )
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<ChannelFederationOptInDto>>([.. rows.Select(ToDto)]);
    }

    public async Task<Result<ChannelFederationOptInDto>> UpsertAsync(
        Guid broadcasterId,
        UpsertChannelFederationOptInRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default
    )
    {
        ChannelFederationOptIn? optIn = await db.ChannelFederationOptIns.FirstOrDefaultAsync(
            o =>
                o.BroadcasterId == broadcasterId
                && o.PeerId == request.PeerId
                && o.OptInType == request.OptInType
                && o.DeletedAt == null,
            cancellationToken
        );
        if (optIn is null)
        {
            optIn = new ChannelFederationOptIn
            {
                BroadcasterId = broadcasterId,
                PeerId = request.PeerId,
                OptInType = request.OptInType,
            };
            db.ChannelFederationOptIns.Add(optIn);
        }
        optIn.Direction = request.Direction;
        optIn.IsEnabled = request.IsEnabled;
        optIn.EnabledByUserId = actingUserId;
        await db.SaveChangesAsync(cancellationToken);

        await PublishChangeAsync(optIn, cancellationToken);
        return Result.Success(ToDto(optIn));
    }

    public async Task<Result> DisableAsync(
        Guid broadcasterId,
        Guid optInId,
        Guid actingUserId,
        CancellationToken cancellationToken = default
    )
    {
        ChannelFederationOptIn? optIn = await db.ChannelFederationOptIns.FirstOrDefaultAsync(
            o => o.Id == optInId && o.BroadcasterId == broadcasterId && o.DeletedAt == null,
            cancellationToken
        );
        if (optIn is null)
            return Result.Failure("Opt-in not found.", "NOT_FOUND");

        optIn.IsEnabled = false;
        optIn.EnabledByUserId = actingUserId;
        optIn.DeletedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);

        await PublishChangeAsync(optIn, cancellationToken);
        return Result.Success();
    }

    public async Task<Result<bool>> IsActionPermittedAsync(
        Guid broadcasterId,
        Guid peerId,
        string optInType,
        string direction,
        CancellationToken cancellationToken = default
    )
    {
        bool peerTrusted = await db.FederationPeers.AnyAsync(
            p =>
                p.Id == peerId
                && p.TrustState == FederationTrustState.Trusted
                && p.DeletedAt == null,
            cancellationToken
        );
        if (!peerTrusted)
            return Result.Success(false); // default-deny: an untrusted peer never acts

        bool permitted = await db.ChannelFederationOptIns.AnyAsync(
            o =>
                o.BroadcasterId == broadcasterId
                && o.OptInType == optInType
                && o.IsEnabled
                && o.DeletedAt == null
                && (o.PeerId == peerId || o.PeerId == null) // null = any trusted peer
                && (o.Direction == direction || o.Direction == FederationDirection.Both),
            cancellationToken
        );
        return Result.Success(permitted);
    }

    private Task PublishChangeAsync(ChannelFederationOptIn optIn, CancellationToken ct) =>
        eventBus.PublishAsync(
            new ChannelFederationOptInChangedEvent
            {
                BroadcasterId = optIn.BroadcasterId,
                OptInBroadcasterId = optIn.BroadcasterId,
                PeerId = optIn.PeerId,
                OptInType = optIn.OptInType,
                Direction = optIn.Direction,
                IsEnabled = optIn.IsEnabled,
            },
            ct
        );

    private static ChannelFederationOptInDto ToDto(ChannelFederationOptIn o) =>
        new(o.Id, o.BroadcasterId, o.PeerId, o.OptInType, o.Direction, o.IsEnabled);
}
