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
using NomNomzBot.Application.Supporters.Dtos;

namespace NomNomzBot.Application.Supporters.Services;

/// <summary>
/// Manages a broadcaster's supporter connections + browses their recorded events (supporter-events.md §5).
/// Connection writes are Broadcaster-gated (a payout/identity-bearing money source); the secret is AEAD-sealed
/// and never returned.
/// </summary>
public interface ISupporterConnectionService
{
    Task<Result<IReadOnlyList<SupporterConnectionDto>>> ListAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    Task<Result<SupporterConnectionDto>> UpsertAsync(
        Guid broadcasterId,
        Guid actorUserId,
        UpsertSupporterConnectionRequest request,
        CancellationToken ct = default
    );

    Task<Result> DeleteAsync(
        Guid broadcasterId,
        Guid actorUserId,
        string sourceKey,
        CancellationToken ct = default
    );

    Task<Result<PagedList<SupporterEventDto>>> ListEventsAsync(
        Guid broadcasterId,
        SupporterEventQuery query,
        CancellationToken ct = default
    );
}
