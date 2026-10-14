// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Chat.Services;

/// <summary>
/// Gives a chatter without a chat color of their own the palette color the channel's chatters used least
/// in the last 14 days of streaming. A color the chatter's line carries always wins.
/// </summary>
public interface IChatColorAssignmentService
{
    /// <summary>
    /// The color to show for the line: <paramref name="lineColor"/> when it is set, otherwise the chatter's
    /// stored assignment (made now when they have none).
    /// </summary>
    Task<string?> ResolveAsync(
        Guid broadcasterId,
        string provider,
        string userId,
        string? lineColor,
        CancellationToken ct = default
    );
}
