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
    private readonly ITwitchIdentityResolver _identityResolver;
    private readonly IApplicationDbContext _db;
    private readonly TwitchOptions _options;
    private readonly ILogger<HelixChatProvider> _logger;

    // Cached bot user ID — resolved once from DB
    private string? _cachedBotUserId;

    public HelixChatProvider(
        ITwitchApiService api,
        ITwitchIdentityResolver identityResolver,
        IApplicationDbContext db,
        IOptions<TwitchOptions> options,
        ILogger<HelixChatProvider> logger
    )
    {
        _api = api;
        _identityResolver = identityResolver;
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendMessageAsync(
        Guid broadcasterId,
        string message,
        CancellationToken cancellationToken = default
    )
    {
        string? twitchBroadcasterId = await ResolveTwitchChannelIdAsync(
            broadcasterId,
            cancellationToken
        );
        if (twitchBroadcasterId is null)
            return;

        string? botUserId = await GetBotUserIdAsync(cancellationToken);
        if (botUserId is null)
        {
            _logger.LogWarning(
                "HelixChatProvider: no bot user ID, cannot send message to {BroadcasterId}",
                broadcasterId
            );
            return;
        }

        await _api.SendChatMessageAsync(
            twitchBroadcasterId,
            botUserId,
            message,
            null,
            cancellationToken
        );
    }

    public async Task SendReplyAsync(
        Guid broadcasterId,
        string replyToMessageId,
        string message,
        CancellationToken cancellationToken = default
    )
    {
        string? twitchBroadcasterId = await ResolveTwitchChannelIdAsync(
            broadcasterId,
            cancellationToken
        );
        if (twitchBroadcasterId is null)
            return;

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
            twitchBroadcasterId,
            botUserId,
            message,
            replyToMessageId,
            cancellationToken
        );
    }

    public async Task TimeoutUserAsync(
        Guid broadcasterId,
        string userId,
        int durationSeconds,
        string? reason = null,
        CancellationToken cancellationToken = default
    )
    {
        string? twitchBroadcasterId = await ResolveTwitchChannelIdAsync(
            broadcasterId,
            cancellationToken
        );
        if (twitchBroadcasterId is null)
            return;

        await _api.TimeoutUserAsync(
            twitchBroadcasterId,
            userId,
            durationSeconds,
            reason,
            cancellationToken
        );
    }

    public async Task BanUserAsync(
        Guid broadcasterId,
        string userId,
        string? reason = null,
        CancellationToken cancellationToken = default
    )
    {
        string? twitchBroadcasterId = await ResolveTwitchChannelIdAsync(
            broadcasterId,
            cancellationToken
        );
        if (twitchBroadcasterId is null)
            return;

        await _api.BanUserAsync(twitchBroadcasterId, userId, reason, cancellationToken);
    }

    public async Task UnbanUserAsync(
        Guid broadcasterId,
        string userId,
        CancellationToken cancellationToken = default
    )
    {
        string? twitchBroadcasterId = await ResolveTwitchChannelIdAsync(
            broadcasterId,
            cancellationToken
        );
        if (twitchBroadcasterId is null)
            return;

        await _api.UnbanUserAsync(twitchBroadcasterId, userId, cancellationToken);
    }

    public async Task DeleteMessageAsync(
        Guid broadcasterId,
        string messageId,
        CancellationToken cancellationToken = default
    )
    {
        string? twitchBroadcasterId = await ResolveTwitchChannelIdAsync(
            broadcasterId,
            cancellationToken
        );
        if (twitchBroadcasterId is null)
            return;

        await _api.DeleteChatMessageAsync(twitchBroadcasterId, messageId, cancellationToken);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the tenant Guid to the Twitch channel string id. Logs and returns null when the channel
    /// is unknown — the caller skips the Twitch call rather than sending a Guid (the invariant).
    /// </summary>
    private async Task<string?> ResolveTwitchChannelIdAsync(
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        string? twitchChannelId = await _identityResolver.GetTwitchChannelIdAsync(
            broadcasterId,
            ct
        );
        if (twitchChannelId is null)
            _logger.LogWarning(
                "HelixChatProvider: no Twitch channel id for tenant {BroadcasterId}, skipping",
                broadcasterId
            );
        return twitchChannelId;
    }

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
