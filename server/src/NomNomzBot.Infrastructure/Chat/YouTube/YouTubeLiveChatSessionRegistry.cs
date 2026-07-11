// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace NomNomzBot.Infrastructure.Chat.YouTube;

/// <summary>
/// The live YouTube chat session per YouTube tenant channel — written by
/// <see cref="YouTubeLiveChatPollWorker"/> on go-live/offline transitions and read by the send path
/// (<see cref="YouTubeChatPlatform"/>). Carries the ACTIVE <c>liveChatId</c> (there is nothing to send
/// into while offline) and the PRIMARY channel id whose vaulted YouTube token authorizes the call
/// (the OAuth connection lives on the streamer's primary channel, not the provisioned YouTube tenant).
/// </summary>
public interface IYouTubeLiveChatSessionRegistry
{
    void SetLive(Guid tenantId, Guid primaryBroadcasterId, string liveChatId);
    void SetOffline(Guid tenantId);

    /// <summary>The tenant's active session, or null while the channel is not live on YouTube.</summary>
    YouTubeLiveChatSession? Get(Guid tenantId);
}

/// <summary>An active live-chat session: which token to use and which chat to write into.</summary>
public sealed record YouTubeLiveChatSession(Guid PrimaryBroadcasterId, string LiveChatId);

/// <inheritdoc cref="IYouTubeLiveChatSessionRegistry"/>
public sealed class YouTubeLiveChatSessionRegistry : IYouTubeLiveChatSessionRegistry
{
    private readonly ConcurrentDictionary<Guid, YouTubeLiveChatSession> _sessions = new();

    public void SetLive(Guid tenantId, Guid primaryBroadcasterId, string liveChatId) =>
        _sessions[tenantId] = new YouTubeLiveChatSession(primaryBroadcasterId, liveChatId);

    public void SetOffline(Guid tenantId) => _sessions.TryRemove(tenantId, out _);

    public YouTubeLiveChatSession? Get(Guid tenantId) =>
        _sessions.TryGetValue(tenantId, out YouTubeLiveChatSession? session) ? session : null;
}
