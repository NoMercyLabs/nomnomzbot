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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Platform;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// Primary <see cref="IChatProvider"/> implementation over the Twitch Helix API. Sending posts
/// <c>/helix/chat/messages</c> on the bot token through <see cref="ITwitchHelixTransport"/> (this provider is
/// the chat-send owner the moderation sub-client deliberately leaves the plain send to); chat enforcement
/// delegates to <see cref="ITwitchModerationApi"/>, which resolves the tenant Guid → Twitch id internally.
///
/// This is the EventSub-first path. IRC (<c>TwitchIrcService</c>) is only a thin fallback for features not
/// yet available in Helix (e.g. watch streaks).
/// </summary>
public sealed class HelixChatProvider : IChatProvider
{
    private readonly ITwitchHelixTransport _transport;
    private readonly ITwitchModerationApi _moderation;
    private readonly ITwitchIdentityResolver _identityResolver;
    private readonly IApplicationDbContext _db;
    private readonly TwitchOptions _options;
    private readonly ILogger<HelixChatProvider> _logger;

    // Cached bot user ID — resolved once from DB
    private string? _cachedBotUserId;

    public HelixChatProvider(
        ITwitchHelixTransport transport,
        ITwitchModerationApi moderation,
        ITwitchIdentityResolver identityResolver,
        IApplicationDbContext db,
        IOptions<TwitchOptions> options,
        ILogger<HelixChatProvider> logger
    )
    {
        _transport = transport;
        _moderation = moderation;
        _identityResolver = identityResolver;
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public Task SendMessageAsync(
        Guid broadcasterId,
        string message,
        CancellationToken cancellationToken = default
    ) => PostChatMessageAsync(broadcasterId, message, null, cancellationToken);

    public Task SendReplyAsync(
        Guid broadcasterId,
        string replyToMessageId,
        string message,
        CancellationToken cancellationToken = default
    ) => PostChatMessageAsync(broadcasterId, message, replyToMessageId, cancellationToken);

    public Task TimeoutUserAsync(
        Guid broadcasterId,
        string userId,
        int durationSeconds,
        string? reason = null,
        CancellationToken cancellationToken = default
    ) =>
        _moderation.TimeoutUserAsync(
            broadcasterId,
            userId,
            durationSeconds,
            reason,
            cancellationToken
        );

    public Task BanUserAsync(
        Guid broadcasterId,
        string userId,
        string? reason = null,
        CancellationToken cancellationToken = default
    ) => _moderation.BanUserAsync(broadcasterId, userId, reason, cancellationToken);

    public Task UnbanUserAsync(
        Guid broadcasterId,
        string userId,
        CancellationToken cancellationToken = default
    ) => _moderation.UnbanUserAsync(broadcasterId, userId, cancellationToken);

    public Task DeleteMessageAsync(
        Guid broadcasterId,
        string messageId,
        CancellationToken cancellationToken = default
    ) => _moderation.DeleteChatMessageAsync(broadcasterId, messageId, cancellationToken);

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Posts a chat message (or reply) as the bot. The bot sends as itself, so the call rides the bot token
    /// (<see cref="TwitchHelixAuth.App"/>) rather than the tenant's user token — the target channel and
    /// sender id travel in the body. The transport serialises this PascalCase body to snake_case and omits
    /// the null reply id for a plain message.
    /// </summary>
    private async Task PostChatMessageAsync(
        Guid broadcasterId,
        string message,
        string? replyToMessageId,
        CancellationToken ct
    )
    {
        string? twitchBroadcasterId = await ResolveTwitchChannelIdAsync(broadcasterId, ct);
        if (twitchBroadcasterId is null)
            return;

        string? botUserId = await GetBotUserIdAsync(ct);
        if (botUserId is null)
        {
            _logger.LogWarning(
                "HelixChatProvider: no bot user ID, cannot send message to {BroadcasterId}",
                broadcasterId
            );
            return;
        }

        TwitchHelixRequest request = new(
            HttpMethod.Post,
            "chat/messages",
            TwitchHelixAuth.App,
            Body: new
            {
                BroadcasterId = twitchBroadcasterId,
                SenderId = botUserId,
                Message = message,
                ReplyParentMessageId = replyToMessageId,
            },
            Priority: TwitchCallPriority.UserInteractive
        );

        Result result = await _transport.SendAsync(request, ct);
        if (result.IsFailure)
            _logger.LogWarning(
                "HelixChatProvider: send to {BroadcasterId} failed: {Error}",
                broadcasterId,
                result.ErrorMessage
            );
    }

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

        // The shared platform bot's Twitch user id is the connected account on its vault connection
        // (Provider "twitch_bot", no broadcaster) — it is the sender_id on every chat send.
        string botProvider = AuthEnums.IntegrationProvider.Twitch + "_bot";
        _cachedBotUserId = await _db
            .IntegrationConnections.IgnoreQueryFilters()
            .Where(c => c.Provider == botProvider && c.BroadcasterId == null && c.DeletedAt == null)
            .Select(c => c.ProviderAccountId)
            .FirstOrDefaultAsync(ct);

        return _cachedBotUserId;
    }
}
