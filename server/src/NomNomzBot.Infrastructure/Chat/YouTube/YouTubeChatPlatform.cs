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
/// send (the message text itself usually carries the @mention). Moderation: timeout = TEMPORARY
/// live-chat ban, ban = permanent (<c>liveChat/bans</c>), delete = <c>liveChat/messages</c> delete;
/// unban = <c>liveChatBans.delete</c> with the ledgered ban id (every ban the bot issues records its
/// insert-returned id in <see cref="IYouTubeLiveChatBanLedger"/>; a viewer the bot never banned is an
/// honest logged no-op).
/// </summary>
public sealed class YouTubeChatPlatform : IChatPlatform
{
    private readonly IYouTubeLiveChatSessionRegistry _sessions;
    private readonly IYouTubeAccessTokenProvider _tokens;
    private readonly IYouTubeLiveChatClient _client;
    private readonly IYouTubeLiveChatBanLedger _bans;
    private readonly ILogger<YouTubeChatPlatform> _logger;

    public YouTubeChatPlatform(
        IYouTubeLiveChatSessionRegistry sessions,
        IYouTubeAccessTokenProvider tokens,
        IYouTubeLiveChatClient client,
        IYouTubeLiveChatBanLedger bans,
        ILogger<YouTubeChatPlatform> logger
    )
    {
        _sessions = sessions;
        _tokens = tokens;
        _client = client;
        _bans = bans;
        _logger = logger;
    }

    public string Provider => AuthEnums.Platform.YouTube;

    public async Task<bool> SendMessageAsync(
        Guid broadcasterId,
        string message,
        CancellationToken cancellationToken = default
    )
    {
        (YouTubeLiveChatSession Session, string Token)? auth = await ResolveWriteAuthAsync(
            broadcasterId,
            "send",
            cancellationToken
        );
        if (auth is null)
            return false;

        Result sent = await _client.SendMessageAsync(
            auth.Value.Token,
            auth.Value.Session.LiveChatId,
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
    ) =>
        // The Twitch-timeout analogue: a TEMPORARY live-chat ban for the given duration.
        BanCoreAsync(broadcasterId, userId, durationSeconds, "timeout", cancellationToken);

    public Task BanUserAsync(
        Guid broadcasterId,
        string userId,
        string? reason = null,
        CancellationToken cancellationToken = default
    ) => BanCoreAsync(broadcasterId, userId, durationSeconds: null, "ban", cancellationToken);

    public async Task UnbanUserAsync(
        Guid broadcasterId,
        string userId,
        CancellationToken cancellationToken = default
    )
    {
        // The ledger holds the insert-returned ban id (the only key liveChatBans.delete accepts). No record
        // = the bot never banned this viewer (or already unbanned them) — an honest logged no-op. Consuming
        // before the delete is safe either way: a NOT_FOUND means YouTube no longer has the ban.
        YouTubeConsumedBan? ban = await _bans.ConsumeLatestAsync(
            broadcasterId,
            userId,
            cancellationToken
        );
        if (ban is null)
        {
            _logger.LogWarning(
                "YouTube unban skipped for {UserId} on {BroadcasterId}: no recorded ban to lift",
                userId,
                broadcasterId
            );
            return;
        }

        // An unban must work while OFFLINE too (a permanent ban outlives the session), so it does not go
        // through ResolveWriteAuthAsync: the ledger recorded which PRIMARY channel's token issued the ban,
        // and that token alone is enough — no active live chat required.
        string? accessToken = await _tokens.GetAccessTokenAsync(
            ban.PrimaryBroadcasterId,
            cancellationToken
        );
        if (accessToken is null)
        {
            _logger.LogWarning(
                "YouTube unban failed for {UserId} on {BroadcasterId}: no usable token on primary channel {Primary}",
                userId,
                broadcasterId,
                ban.PrimaryBroadcasterId
            );
            return;
        }

        Result unbanned = await _client.UnbanUserAsync(accessToken, ban.BanId, cancellationToken);
        if (unbanned.IsFailure && unbanned.ErrorCode != "NOT_FOUND")
            _logger.LogWarning(
                "YouTube unban failed for {UserId} on {BroadcasterId}: {Error} ({Code})",
                userId,
                broadcasterId,
                unbanned.ErrorMessage,
                unbanned.ErrorCode
            );
    }

    public async Task DeleteMessageAsync(
        Guid broadcasterId,
        string messageId,
        CancellationToken cancellationToken = default
    )
    {
        (YouTubeLiveChatSession Session, string Token)? auth = await ResolveWriteAuthAsync(
            broadcasterId,
            "delete message",
            cancellationToken
        );
        if (auth is null)
            return;

        Result deleted = await _client.DeleteMessageAsync(
            auth.Value.Token,
            messageId,
            cancellationToken
        );
        if (deleted.IsFailure)
            _logger.LogWarning(
                "YouTube message delete failed for {BroadcasterId}: {Error} ({Code})",
                broadcasterId,
                deleted.ErrorMessage,
                deleted.ErrorCode
            );
    }

    private async Task BanCoreAsync(
        Guid broadcasterId,
        string bannedChannelId,
        int? durationSeconds,
        string verb,
        CancellationToken ct
    )
    {
        (YouTubeLiveChatSession Session, string Token)? auth = await ResolveWriteAuthAsync(
            broadcasterId,
            verb,
            ct
        );
        if (auth is null)
            return;

        Result<string> banned = await _client.BanUserAsync(
            auth.Value.Token,
            auth.Value.Session.LiveChatId,
            bannedChannelId,
            durationSeconds,
            ct
        );
        if (banned.IsFailure)
        {
            _logger.LogWarning(
                "YouTube {Verb} failed for {UserId} on {BroadcasterId}: {Error} ({Code})",
                verb,
                bannedChannelId,
                broadcasterId,
                banned.ErrorMessage,
                banned.ErrorCode
            );
            return;
        }

        // Ledger the insert-returned ban id — the only key a later unban (liveChatBans.delete) accepts.
        await _bans.RecordAsync(
            broadcasterId,
            auth.Value.Session.PrimaryBroadcasterId,
            auth.Value.Session.LiveChatId,
            bannedChannelId,
            banned.Value,
            durationSeconds,
            ct
        );
    }

    /// <summary>The write-auth pair every moderation call needs: the ACTIVE session + a usable token —
    /// null (with a log) when the channel is offline or the token cannot be resolved.</summary>
    private async Task<(YouTubeLiveChatSession Session, string Token)?> ResolveWriteAuthAsync(
        Guid broadcasterId,
        string verb,
        CancellationToken ct
    )
    {
        YouTubeLiveChatSession? session = _sessions.Get(broadcasterId);
        if (session is null)
        {
            _logger.LogDebug(
                "YouTube {Verb} skipped for {BroadcasterId}: channel is not live",
                verb,
                broadcasterId
            );
            return null;
        }

        string? accessToken = await _tokens.GetAccessTokenAsync(session.PrimaryBroadcasterId, ct);
        if (accessToken is null)
        {
            _logger.LogWarning(
                "YouTube {Verb} failed for {BroadcasterId}: no usable token on primary channel {Primary}",
                verb,
                broadcasterId,
                session.PrimaryBroadcasterId
            );
            return null;
        }

        return (session, accessToken);
    }
}
