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

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Chat.Services;

/// <summary>
/// Reads the chat of any Twitch channel for one signed-in viewer, on the viewer's own token (chat-client.md
/// "Any Twitch channel"). The lines go to that viewer's connection only and never enter the bot pipeline.
/// </summary>
public interface IExternalChatRelay
{
    /// <summary>Starts reading <paramref name="twitchBroadcasterId"/> for this connection.</summary>
    Task<Result> JoinAsync(
        Guid viewerUserId,
        string connectionId,
        string twitchBroadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Stops reading one channel for this connection. The last channel closes the session.</summary>
    Task LeaveAsync(string connectionId, string twitchBroadcasterId);

    /// <summary>Drops every channel of a closed connection.</summary>
    Task DisconnectAsync(string connectionId);
}
