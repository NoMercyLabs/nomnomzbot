// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
