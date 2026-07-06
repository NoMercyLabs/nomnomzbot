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
/// <c>/helix/chat/messages</c> as the bot identity resolved PER BROADCASTER through <see cref="ITwitchHelixTransport"/>
/// (this provider is the chat-send owner the moderation sub-client deliberately leaves the plain send to); chat
/// enforcement delegates to <see cref="ITwitchModerationApi"/>, which resolves the tenant Guid → Twitch id internally.
///
/// This is the sole chat path: chat is read via EventSub (<c>channel.chat.message</c>) and sent via Helix here
/// — there is no IRC connection.
/// </summary>
public sealed class HelixChatProvider : IChatProvider
{
    private const string BotProvider = AuthEnums.IntegrationProvider.Twitch + "_bot";
    private const string UserProvider = AuthEnums.IntegrationProvider.Twitch;

    private readonly ITwitchHelixTransport _transport;
    private readonly ITwitchModerationApi _moderation;
    private readonly ITwitchIdentityResolver _identityResolver;
    private readonly IApplicationDbContext _db;
    private readonly TwitchOptions _options;
    private readonly ILogger<HelixChatProvider> _logger;

    // Bot sender identity resolved PER BROADCASTER — never one process-wide account. On a multi-tenant
    // deployment each channel's send must ride the identity whose token signs THAT channel's send, so the
    // cache is keyed by broadcaster. Scoped lifetime, so it lives for one request/pipeline run and spares
    // repeat sends to the same channel a DB round-trip.
    private readonly Dictionary<Guid, BotSenderIdentity> _senderByBroadcaster = new();

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

    public Task<bool> SendMessageAsync(
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
    /// Posts a chat message (or reply) as the bot for one tenant. The sender identity is resolved PER
    /// BROADCASTER (never one process-wide account): a shared platform bot rides the app/bot token
    /// (<see cref="TwitchHelixAuth.App"/>); otherwise the channel's OWN owner account is the bot and rides
    /// that channel's broadcaster user token (<see cref="TwitchHelixAuth.User"/>). Either way the body's
    /// <c>sender_id</c> is the SAME account the resolved token belongs to, so Twitch never rejects a send
    /// whose sender doesn't match the signing token. The transport serialises this PascalCase body to
    /// snake_case and omits the null reply id for a plain message.
    /// </summary>
    private async Task<bool> PostChatMessageAsync(
        Guid broadcasterId,
        string message,
        string? replyToMessageId,
        CancellationToken ct
    )
    {
        string? twitchBroadcasterId = await ResolveTwitchChannelIdAsync(broadcasterId, ct);
        if (twitchBroadcasterId is null)
            return false;

        BotSenderIdentity? sender = await ResolveBotSenderAsync(broadcasterId, ct);
        if (sender is null)
        {
            _logger.LogWarning(
                "HelixChatProvider: no bot sender identity for {BroadcasterId}, cannot send message",
                broadcasterId
            );
            return false;
        }

        TwitchHelixRequest request = new(
            HttpMethod.Post,
            "chat/messages",
            sender.Auth,
            // The owner-as-bot path rides THIS channel's broadcaster token, so the transport needs the tenant
            // to resolve it; the shared-bot path rides the subject-agnostic app/bot token (no tenant).
            BroadcasterId: sender.Auth == TwitchHelixAuth.User ? broadcasterId : null,
            Body: new
            {
                BroadcasterId = twitchBroadcasterId,
                SenderId = sender.TwitchUserId,
                Message = message,
                ReplyParentMessageId = replyToMessageId,
            },
            Priority: TwitchCallPriority.UserInteractive
        );

        Result result = await _transport.SendAsync(request, ct);
        if (result.IsFailure)
        {
            _logger.LogWarning(
                "HelixChatProvider: send to {BroadcasterId} failed: {Error}",
                broadcasterId,
                result.ErrorMessage
            );
            return false;
        }

        return true;
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

    /// <summary>
    /// Resolves the bot SENDER identity for one broadcaster — the account whose <c>sender_id</c> travels on
    /// the send and whose token signs it. The order mirrors the token resolution
    /// (<see cref="ITwitchTokenResolver.GetBotTokenAsync"/> → <c>GetBroadcasterTokenAsync</c>):
    /// <list type="number">
    ///   <item>a shared platform bot (<c>twitch_bot</c>, no broadcaster) → rides the app/bot token;</item>
    ///   <item>else THIS channel's OWN owner (<c>twitch</c>, this broadcaster) → the main-account-is-the-bot
    ///   model (onboarding.md), riding that channel's own broadcaster token.</item>
    /// </list>
    /// Keyed by broadcaster (never a single process-wide field), so a multi-tenant deployment never sends
    /// channel B's message as channel A's account. Returns null only on a channel with no usable identity.
    /// </summary>
    private async Task<BotSenderIdentity?> ResolveBotSenderAsync(
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        if (_senderByBroadcaster.TryGetValue(broadcasterId, out BotSenderIdentity? cached))
            return cached;

        string? sharedBotUserId = await SharedBotUserIdAsync(ct);
        BotSenderIdentity? sender = sharedBotUserId is not null
            ? new BotSenderIdentity(TwitchHelixAuth.App, sharedBotUserId)
            : await OwnerSenderAsync(broadcasterId, ct);

        if (sender is not null)
            _senderByBroadcaster[broadcasterId] = sender;

        return sender;
    }

    /// <summary>The shared platform bot account's Twitch user id (<c>twitch_bot</c>, no broadcaster), or null.</summary>
    private Task<string?> SharedBotUserIdAsync(CancellationToken ct) =>
        _db
            .IntegrationConnections.IgnoreQueryFilters()
            .Where(c => c.Provider == BotProvider && c.BroadcasterId == null && c.DeletedAt == null)
            .OrderBy(c => c.CreatedAt)
            .Select(c => c.ProviderAccountId)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// The channel's OWN owner account as the bot sender (<c>twitch</c>, this broadcaster) — the send rides
    /// the same channel's broadcaster user token, so <c>sender_id</c> and the token always belong to one
    /// account. Scoped strictly to <paramref name="broadcasterId"/>, never the oldest connection across all
    /// tenants. Null when this channel has no owner connection.
    /// </summary>
    private async Task<BotSenderIdentity?> OwnerSenderAsync(
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        string? ownerUserId = await _db
            .IntegrationConnections.IgnoreQueryFilters()
            .Where(c =>
                c.Provider == UserProvider
                && c.BroadcasterId == broadcasterId
                && c.DeletedAt == null
            )
            .OrderBy(c => c.CreatedAt)
            .Select(c => c.ProviderAccountId)
            .FirstOrDefaultAsync(ct);

        return ownerUserId is null
            ? null
            : new BotSenderIdentity(TwitchHelixAuth.User, ownerUserId);
    }

    /// <summary>The resolved bot sender for one channel: which token the send rides and the account id on it.</summary>
    private sealed record BotSenderIdentity(TwitchHelixAuth Auth, string TwitchUserId);
}
