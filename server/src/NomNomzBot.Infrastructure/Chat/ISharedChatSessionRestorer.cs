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
/// Brings the memory-only <see cref="ISharedChatSessionTracker"/> back after a restart. The tracker is fed by
/// <c>channel.shared_chat.begin/update/end</c>, and a session that began before the restart never announces
/// itself again — so the first shared ban after a restart would see "no session" and be dropped. This asks
/// Twitch (Get Shared Chat Session) instead, once, the first time a channel's session is needed.
/// </summary>
public interface ISharedChatSessionRestorer
{
    /// <summary>
    /// The channel's active session: the tracked one when there is one, otherwise the one Twitch reports (which
    /// is then tracked, together with every other local channel that takes part in it). Null when the channel is
    /// in no session or Twitch cannot be asked — this never throws.
    /// </summary>
    Task<SharedChatSessionInfo?> EnsureActiveSessionAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Tracks <paramref name="session"/> for every LOCAL channel that takes part in it and is not already
    /// tracked in a session. A tracked channel is never overwritten — a newer live fact wins.
    /// </summary>
    Task AdoptParticipantsAsync(SharedChatSessionInfo session, CancellationToken ct = default);
}
