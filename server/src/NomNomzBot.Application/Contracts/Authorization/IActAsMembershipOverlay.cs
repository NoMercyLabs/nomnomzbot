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

using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Application.Contracts.Authorization;

/// <summary>
/// The management membership a user's OWN login would hold on a channel but an act-as session must not write.
/// A user's own channel list grants the Moderator role on every onboarded channel they moderate on Twitch (a
/// persisted <c>TwitchBadge</c> row). While an operator acts as that user nothing may be persisted, so the row
/// is computed per request instead — the act-as answer then equals the user's own login without a write.
/// </summary>
public interface IActAsMembershipOverlay
{
    /// <summary>
    /// The role the user's own login would have been granted on <paramref name="broadcasterId"/>, or null when
    /// the current request is not acting as <paramref name="userId"/> or that login would grant nothing. Callers
    /// ask only when the user holds no membership row there.
    /// </summary>
    Task<ManagementRole?> ResolveAsync(
        Guid userId,
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );
}
