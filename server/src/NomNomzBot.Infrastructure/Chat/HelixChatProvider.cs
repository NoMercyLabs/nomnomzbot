// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.Platform;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// Primary IChatProvider implementation that uses the Twitch Helix API.
/// Sending: POST /helix/chat/messages
/// Moderation: /helix/moderation/*
///
/// This is the EventSub-first path. IRC (TwitchIrcService) is only used as a
/// thin fallback for features not yet available in Helix (e.g. watch streaks).
/// </summary>
public sealed class HelixChatProvider : IChatProvider
{
    private readonly ITwitchApiService _api;
    private readonly IApplicationDbContext _db;
    private readonly TwitchOptions _options;
    private readonly ILogger<HelixChatProvider> _logger;

    // Cached bot user ID — resolved once from DB
    private string? _cachedBotUserId;

    public HelixChatProvider(
        ITwitchApiService api,
        IApplicationDbContext db,
        IOptions<TwitchOptions> options,
        ILogger<HelixChatProvider> logger
    )
    {
        _api = api;
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendMessageAsync(
        string broadcasterId,
        string message,
        CancellationToken cancellationToken = default
    )
    {
        string? botUserId = await GetBotUserIdAsync(cancellationToken);
        if (botUserId is null)
        {
            _logger.LogWarning(
                "HelixChatProvider: no bot user ID, cannot send message to {BroadcasterId}",
                broadcasterId
            );
            return;
        }

        await _api.SendChatMessageAsync(broadcasterId, botUserId, message, null, cancellationToken);
    }

    public async Task SendReplyAsync(
        string broadcasterId,
        string replyToMessageId,
        string message,
        CancellationToken cancellationToken = default
    )
    {
        string? botUserId = await GetBotUserIdAsync(cancellationToken);
        if (botUserId is null)
        {
            _logger.LogWarning(
                "HelixChatProvider: no bot user ID, cannot send reply to {BroadcasterId}",
                broadcasterId
            );
            return;
        }

        await _api.SendChatMessageAsync(
            broadcasterId,
            botUserId,
            message,
            replyToMessageId,
            cancellationToken
        );
    }

    public async Task TimeoutUserAsync(
        string broadcasterId,
        string userId,
        int durationSeconds,
        string? reason = null,
        CancellationToken cancellationToken = default
    )
    {
        await _api.TimeoutUserAsync(
            broadcasterId,
            userId,
            durationSeconds,
            reason,
            cancellationToken
        );
    }

    public async Task BanUserAsync(
        string broadcasterId,
        string userId,
        string? reason = null,
        CancellationToken cancellationToken = default
    )
    {
        await _api.BanUserAsync(broadcasterId, userId, reason, cancellationToken);
    }

    public async Task UnbanUserAsync(
        string broadcasterId,
        string userId,
        CancellationToken cancellationToken = default
    )
    {
        await _api.UnbanUserAsync(broadcasterId, userId, cancellationToken);
    }

    public async Task DeleteMessageAsync(
        string broadcasterId,
        string messageId,
        CancellationToken cancellationToken = default
    )
    {
        await _api.DeleteChatMessageAsync(broadcasterId, messageId, cancellationToken);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    private async Task<string?> GetBotUserIdAsync(CancellationToken ct)
    {
        if (_cachedBotUserId is not null)
            return _cachedBotUserId;

        Service? service = await _db
            .Services.Where(s => s.Name == "twitch_bot" && s.Enabled && s.UserId != null)
            .FirstOrDefaultAsync(ct);

        _cachedBotUserId = service?.UserId;
        return _cachedBotUserId;
    }
}
