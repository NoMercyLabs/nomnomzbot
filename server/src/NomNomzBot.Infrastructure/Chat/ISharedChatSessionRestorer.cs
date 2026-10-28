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
