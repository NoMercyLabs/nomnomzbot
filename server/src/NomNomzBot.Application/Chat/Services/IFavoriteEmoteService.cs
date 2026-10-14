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

namespace NomNomzBot.Application.Chat.Services;

/// <summary>
/// The signed-in user's favorite emotes for the dashboard chat emote picker. Per user, not per channel:
/// every method is scoped to <c>userId</c>, so one user can never read or change another user's favorites.
/// </summary>
public interface IFavoriteEmoteService
{
    /// <summary>Every favorite of <paramref name="userId"/>, newest first.</summary>
    Task<Result<IReadOnlyList<FavoriteEmoteDto>>> ListAsync(
        Guid userId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Idempotent add. A repeat of the same <c>(Provider, EmoteId)</c> keeps one row and refreshes its
    /// code and image snapshot. The 201st distinct favorite fails with <c>VALIDATION_FAILED</c>.
    /// </summary>
    Task<Result<FavoriteEmoteDto>> AddAsync(
        Guid userId,
        SetFavoriteEmoteRequest request,
        CancellationToken ct = default
    );

    /// <summary>Remove one favorite. Removing a favorite that does not exist succeeds (idempotent).</summary>
    Task<Result> RemoveAsync(
        Guid userId,
        string provider,
        string emoteId,
        CancellationToken ct = default
    );
}
