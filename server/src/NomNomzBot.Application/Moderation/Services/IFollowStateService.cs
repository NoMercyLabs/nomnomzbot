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

using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>What is known about one viewer's follow of one channel.</summary>
/// <param name="State">Unknown when the platform could not say; never defaulted to not following.</param>
/// <param name="FollowedAt">When the viewer followed. Set only when <paramref name="State"/> is Following.</param>
public readonly record struct FollowLookup(FollowState State, DateTimeOffset? FollowedAt)
{
    public static FollowLookup Unknown => new(FollowState.Unknown, null);
}

/// <summary>
/// Answers "does this viewer follow this channel, and since when" for the spam-defence trust ladder.
/// Cached, so a busy chat does not call the platform per message.
/// </summary>
public interface IFollowStateService
{
    Task<FollowLookup> ResolveAsync(
        Guid broadcasterId,
        string provider,
        string platformUserId,
        CancellationToken ct = default
    );
}
