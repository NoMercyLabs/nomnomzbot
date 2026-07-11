// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.YouTube;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Chat.YouTube;

/// <summary>
/// The YouTube half of the chat seam (BUILD slice 3 / item 6's "bot replies on YouTube"): sends ride
/// <c>liveChatMessages.insert</c> on the streamer's own OAuth token against the ACTIVE live chat from
/// <see cref="IYouTubeLiveChatSessionRegistry"/> — offline, there is no chat to write into, so a send
/// honestly returns <c>false</c>. YouTube live chat has no reply threading; a reply degrades to a plain
/// send (the message text itself usually carries the @mention). Moderation members are logged no-ops
/// for now — the Live Chat API's ban/delete surface is a follow-up (auto-mod is Twitch-gated anyway).
/// </summary>
public sealed class YouTubeChatPlatform : IChatPlatform
{
    private readonly IYouTubeLiveChatSessionRegistry _sessions;
    private readonly IYouTubeAccessTokenProvider _tokens;
    private readonly IYouTubeLiveChatClient _client;
    private readonly ILogger<YouTubeChatPlatform> _logger;

    public YouTubeChatPlatform(
        IYouTubeLiveChatSessionRegistry sessions,
        IYouTubeAccessTokenProvider tokens,
        IYouTubeLiveChatClient client,
        ILogger<YouTubeChatPlatform> logger
    )
    {
        _sessions = sessions;
        _tokens = tokens;
        _client = client;
        _logger = logger;
    }

    public string Provider => AuthEnums.Platform.YouTube;

    public async Task<bool> SendMessageAsync(
        Guid broadcasterId,
        string message,
        CancellationToken cancellationToken = default
    )
    {
        YouTubeLiveChatSession? session = _sessions.Get(broadcasterId);
        if (session is null)
        {
            _logger.LogDebug(
                "YouTube send skipped for {BroadcasterId}: channel is not live",
                broadcasterId
            );
            return false;
        }

        string? accessToken = await _tokens.GetAccessTokenAsync(
            session.PrimaryBroadcasterId,
            cancellationToken
        );
        if (accessToken is null)
        {
            _logger.LogWarning(
                "YouTube send failed for {BroadcasterId}: no usable token on primary channel {Primary}",
                broadcasterId,
                session.PrimaryBroadcasterId
            );
            return false;
        }

        Result sent = await _client.SendMessageAsync(
            accessToken,
            session.LiveChatId,
            message,
            cancellationToken
        );
        if (sent.IsFailure)
            _logger.LogWarning(
                "YouTube send failed for {BroadcasterId}: {Error} ({Code})",
                broadcasterId,
                sent.ErrorMessage,
                sent.ErrorCode
            );
        return sent.IsSuccess;
    }

    public Task SendReplyAsync(
        Guid broadcasterId,
        string replyToMessageId,
        string message,
        CancellationToken cancellationToken = default
    ) =>
        // No reply threading on the Live Chat API — the plain send IS the reply.
        SendMessageAsync(broadcasterId, message, cancellationToken);

    public Task TimeoutUserAsync(
        Guid broadcasterId,
        string userId,
        int durationSeconds,
        string? reason = null,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogWarning(
            "YouTube moderation (timeout) is not wired yet — no action taken for {UserId} on {BroadcasterId}",
            userId,
            broadcasterId
        );
        return Task.CompletedTask;
    }

    public Task BanUserAsync(
        Guid broadcasterId,
        string userId,
        string? reason = null,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogWarning(
            "YouTube moderation (ban) is not wired yet — no action taken for {UserId} on {BroadcasterId}",
            userId,
            broadcasterId
        );
        return Task.CompletedTask;
    }

    public Task UnbanUserAsync(
        Guid broadcasterId,
        string userId,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogWarning(
            "YouTube moderation (unban) is not wired yet — no action taken for {UserId} on {BroadcasterId}",
            userId,
            broadcasterId
        );
        return Task.CompletedTask;
    }

    public Task DeleteMessageAsync(
        Guid broadcasterId,
        string messageId,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogWarning(
            "YouTube moderation (delete message) is not wired yet — no action taken for {MessageId} on {BroadcasterId}",
            messageId,
            broadcasterId
        );
        return Task.CompletedTask;
    }
}
