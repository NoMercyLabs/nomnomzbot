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
/// Reads the chat of channels a signed-in moderator moderates but the bot did not onboard, on the moderator's
/// own token (user:read:chat), so the moderator can act on them from the dashboard. The lines enter the bot
/// pipeline, which stays silent for channels that did not onboard.
/// </summary>
public interface IModeratorChatReaders
{
    /// <summary>
    /// Brings the moderator's readers in line with Twitch's list of channels they moderate: new channels are
    /// subscribed, channels the moderator lost are released, and a channel nobody can read gets a status.
    /// </summary>
    Task SyncUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The state of every channel the readers hold or could not hold.</summary>
    IReadOnlyList<ModeratorChatReaderStatus> Snapshot();
}

/// <summary>Why a moderated channel is, or is not, being read.</summary>
public enum ModeratorChatReaderState
{
    /// <summary>The moderator's reader holds the channel's eight chat-read subscriptions.</summary>
    Reading,

    /// <summary>The moderator has no usable Twitch connection.</summary>
    NoToken,

    /// <summary>The moderator's connection lacks the user:read:chat scope.</summary>
    MissingScope,

    /// <summary>Twitch no longer lists the moderator as a moderator of the channel.</summary>
    Unmodded,

    /// <summary>The moderator's session is full: Twitch allows 300 subscription cost per session.</summary>
    OverCap,
}

/// <summary>One moderated channel and why it is, or is not, being read.</summary>
public sealed record ModeratorChatReaderStatus(
    string TwitchBroadcasterId,
    ModeratorChatReaderState State,
    Guid? ModeratorUserId,
    string? Reason
);
