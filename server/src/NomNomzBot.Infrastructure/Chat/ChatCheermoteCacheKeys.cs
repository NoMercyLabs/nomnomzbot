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
