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
