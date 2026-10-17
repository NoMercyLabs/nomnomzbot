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
