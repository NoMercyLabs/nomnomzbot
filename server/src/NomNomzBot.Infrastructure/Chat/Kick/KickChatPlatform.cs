// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Kick;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Chat.Kick;

/// <summary>
/// The Kick half of the chat seam (BUILD slice 3b-2c): sends and moderation ride the STREAMER's own
/// vaulted Kick token (the self-host bot-identity pattern — the bot chats as the streamer until a
/// dedicated identity exists). Kick has NATIVE replies (<c>reply_to_message_id</c>) — a reply threads
/// for real, unlike YouTube's degrade-to-send. Timeouts convert the seam's seconds to Kick's MINUTES
/// (ceiling, clamped 1–10080); unban is direct by (broadcaster, user) — no ban-id ledger needed. A
/// tenant without a usable token fails honestly (logged, no API call), surfacing the connect/reauth
/// need instead of pretending.
/// </summary>
public sealed class KickChatPlatform : IChatPlatform
{
    private const int MaxTimeoutMinutes = 10080;

    private readonly IKickAccessTokenProvider _tokens;
    private readonly IKickApiClient _client;
    private readonly ILogger<KickChatPlatform> _logger;

    public KickChatPlatform(
        IKickAccessTokenProvider tokens,
        IKickApiClient client,
        ILogger<KickChatPlatform> logger
    )
    {
        _tokens = tokens;
        _client = client;
        _logger = logger;
    }

    public string Provider => AuthEnums.Platform.Kick;

    public async Task<bool> SendMessageAsync(
        Guid broadcasterId,
        string message,
        CancellationToken cancellationToken = default
    ) => await SendCoreAsync(broadcasterId, message, replyToMessageId: null, cancellationToken);

    public async Task SendReplyAsync(
        Guid broadcasterId,
        string replyToMessageId,
        string message,
        CancellationToken cancellationToken = default
    ) => await SendCoreAsync(broadcasterId, message, replyToMessageId, cancellationToken);

    public Task TimeoutUserAsync(
        Guid broadcasterId,
        string userId,
        int durationSeconds,
        string? reason = null,
        CancellationToken cancellationToken = default
    ) =>
        ModerateAsync(
            broadcasterId,
            userId,
            "timeout",
            (access, target, ct) =>
                _client.TimeoutUserAsync(
                    access.AccessToken,
                    access.BroadcasterUserId,
                    target,
                    ToKickMinutes(durationSeconds),
                    reason,
                    ct
                ),
            cancellationToken
        );

    public Task BanUserAsync(
        Guid broadcasterId,
        string userId,
        string? reason = null,
        CancellationToken cancellationToken = default
    ) =>
        ModerateAsync(
            broadcasterId,
            userId,
            "ban",
            (access, target, ct) =>
                _client.BanUserAsync(
                    access.AccessToken,
                    access.BroadcasterUserId,
                    target,
                    reason,
                    ct
                ),
            cancellationToken
        );

    public Task UnbanUserAsync(
        Guid broadcasterId,
        string userId,
        CancellationToken cancellationToken = default
    ) =>
        ModerateAsync(
            broadcasterId,
            userId,
            "unban",
            (access, target, ct) =>
                _client.UnbanUserAsync(access.AccessToken, access.BroadcasterUserId, target, ct),
            cancellationToken
        );

    public async Task DeleteMessageAsync(
        Guid broadcasterId,
        string messageId,
        CancellationToken cancellationToken = default
    )
    {
        KickAccess? access = await _tokens.GetAsync(broadcasterId, cancellationToken);
        if (access is null)
        {
            LogNoToken("delete message", broadcasterId);
            return;
        }

        Result deleted = await _client.DeleteMessageAsync(
            access.AccessToken,
            messageId,
            cancellationToken
        );
        if (deleted.IsFailure)
            _logger.LogWarning(
                "Kick message delete failed for {BroadcasterId}: {Error} ({Code})",
                broadcasterId,
                deleted.ErrorMessage,
                deleted.ErrorCode
            );
    }

    private async Task<bool> SendCoreAsync(
        Guid broadcasterId,
        string message,
        string? replyToMessageId,
        CancellationToken ct
    )
    {
        KickAccess? access = await _tokens.GetAsync(broadcasterId, ct);
        if (access is null)
        {
            LogNoToken("send", broadcasterId);
            return false;
        }

        Result<string> sent = await _client.SendMessageAsync(
            access.AccessToken,
            access.BroadcasterUserId,
            message,
            replyToMessageId,
            ct
        );
        if (sent.IsFailure)
            _logger.LogWarning(
                "Kick send failed for {BroadcasterId}: {Error} ({Code})",
                broadcasterId,
                sent.ErrorMessage,
                sent.ErrorCode
            );
        return sent.IsSuccess;
    }

    private async Task ModerateAsync(
        Guid broadcasterId,
        string userId,
        string verb,
        Func<KickAccess, long, CancellationToken, Task<Result>> operation,
        CancellationToken ct
    )
    {
        if (
            !long.TryParse(
                userId,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long target
            )
        )
        {
            _logger.LogWarning(
                "Kick {Verb} skipped for {BroadcasterId}: '{UserId}' is not a numeric Kick user id",
                verb,
                broadcasterId,
                userId
            );
            return;
        }

        KickAccess? access = await _tokens.GetAsync(broadcasterId, ct);
        if (access is null)
        {
            LogNoToken(verb, broadcasterId);
            return;
        }

        Result result = await operation(access, target, ct);
        if (result.IsFailure)
            _logger.LogWarning(
                "Kick {Verb} failed for {UserId} on {BroadcasterId}: {Error} ({Code})",
                verb,
                userId,
                broadcasterId,
                result.ErrorMessage,
                result.ErrorCode
            );
    }

    /// <summary>Seam seconds → Kick minutes: ceiling so a 30s timeout is 1 minute (never 0 = permanent
    /// at some platforms), clamped to Kick's 1–10080 range.</summary>
    internal static int ToKickMinutes(int durationSeconds) =>
        Math.Clamp((int)Math.Ceiling(durationSeconds / 60.0), 1, MaxTimeoutMinutes);

    private void LogNoToken(string verb, Guid broadcasterId) =>
        _logger.LogWarning(
            "Kick {Verb} failed for {BroadcasterId}: no usable Kick token (connect/reauth needed)",
            verb,
            broadcasterId
        );
}
