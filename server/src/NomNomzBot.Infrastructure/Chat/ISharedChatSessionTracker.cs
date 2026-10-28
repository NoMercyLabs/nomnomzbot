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

/// <summary>A channel's currently-active Twitch shared-chat session (from EventSub begin/update/end).</summary>
public sealed record SharedChatSessionInfo(
    string SessionId,
    string HostBroadcasterId,
    IReadOnlyList<string> ParticipantTwitchIds
);

/// <summary>
/// Tracks, per tenant channel, the ACTIVE Twitch shared-chat session — the precondition the shared-ban
/// trust web verifies before an inbound ban applies (moderation.md §3.5). Fed by the
/// <c>channel.shared_chat.begin/update/end</c> EventSub events; empty when a channel is not in a session.
/// In-memory by design: shared-chat is a live-stream fact that Twitch re-announces on reconnect.
/// </summary>
public interface ISharedChatSessionTracker
{
    /// <summary>The channel's active session, or null when it is not in one.</summary>
    SharedChatSessionInfo? GetActiveSession(Guid broadcasterId);

    /// <summary>Every LOCAL tenant channel currently tracked in <paramref name="sessionId"/>.</summary>
    IReadOnlyList<Guid> GetChannelsInSession(string sessionId);

    void SetSession(Guid broadcasterId, SharedChatSessionInfo session);

    void ClearSession(Guid broadcasterId, string sessionId);
}
