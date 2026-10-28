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
