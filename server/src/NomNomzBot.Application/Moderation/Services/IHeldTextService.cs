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

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>
/// Answers one safety question for text the bot is about to say aloud: does the moderation queue still hold this
/// viewer's text, or did a moderator deny it? A redemption event and an AutoMod hold event are two separate
/// events and either can arrive first, so the check waits a short, bounded time for a matching row.
/// </summary>
public interface IHeldTextService
{
    /// <summary>
    /// True when a queue row of this channel matches the viewer and the text (same Twitch user id, same text
    /// after trimming and collapsing whitespace, created within two minutes of now) and that row is not
    /// approved: pending, denied, actioned and expired rows all hold the text back. False when the matching row
    /// is approved, or when no row appears within the wait.
    /// </summary>
    Task<bool> IsTextHeldAsync(
        Guid broadcasterId,
        string twitchUserId,
        string text,
        CancellationToken ct = default,
        DateTimeOffset? occurredAt = null
    );
}
