// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// The cache-key scheme for the chat-decoration Helix cheermotes (chat-decoration spec §7). Cheermotes are per channel
/// only — the Helix call returns the channel's set including the global cheermotes — so there is no global key.
/// </summary>
internal static class ChatCheermoteCacheKeys
{
    /// <summary>A channel's cheermote set (its own + the global cheermotes Twitch folds in).</summary>
    public static string Channel(Guid broadcasterId) => $"chat:cheermotes:channel:{broadcasterId}";
}
