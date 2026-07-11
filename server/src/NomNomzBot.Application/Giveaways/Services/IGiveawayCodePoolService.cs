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
using NomNomzBot.Application.Giveaways.Dtos;

namespace NomNomzBot.Application.Giveaways.Services;

/// <summary>
/// Secret-safe prize-code pools (giveaways.md §3.2, D6): codes are AEAD-encrypted on intake, reads are
/// always MASKED, and the only plaintext paths are the winner's whisper and the broadcaster-gated
/// reveal for a failed whisper.
/// </summary>
public interface IGiveawayCodePoolService
{
    Task<Result<CodePoolDto>> CreatePoolAsync(
        Guid broadcasterId,
        CreateCodePoolRequest request,
        CancellationToken ct = default
    );

    /// <summary>Bulk intake — AEAD-encrypts each code (D6); plaintext is never persisted or echoed.</summary>
    Task<Result<CodePoolDto>> AddCodesAsync(
        Guid broadcasterId,
        Guid poolId,
        AddCodesRequest request,
        CancellationToken ct = default
    );

    Task<Result<PagedList<CodePoolDto>>> ListPoolsAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    );

    /// <summary>Pool detail with codes MASKED (label + status), never plaintext.</summary>
    Task<Result<CodePoolDetailDto>> GetPoolAsync(
        Guid broadcasterId,
        Guid poolId,
        CancellationToken ct = default
    );

    Task<Result> DeletePoolAsync(Guid broadcasterId, Guid poolId, CancellationToken ct = default);

    /// <summary>Broadcaster-only fallback when the winner's whisper failed (D6): decrypts the assigned
    /// code for manual relay — the one read path that ever returns plaintext.</summary>
    Task<Result<string>> RevealAssignedCodeAsync(
        Guid broadcasterId,
        Guid winnerId,
        CancellationToken ct = default
    );
}
