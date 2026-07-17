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
/// The Twitch <see cref="IChatPlatform"/> over the Helix API (reached through the platform-routing
/// <see cref="IChatProvider"/> for twitch-provider channels). Sending posts
/// <c>/helix/chat/messages</c> as the bot identity resolved PER BROADCASTER through <see cref="ITwitchHelixTransport"/>
/// (this provider is the chat-send owner the moderation sub-client deliberately leaves the plain send to); chat
/// enforcement delegates to <see cref="ITwitchModerationApi"/>, which resolves the tenant Guid → Twitch id internally.
///
/// This is the sole Twitch chat path: chat is read via EventSub (<c>channel.chat.message</c>) and sent via Helix
/// here — there is no IRC connection.
/// </summary>
public sealed class HelixChatProvider : IChatPlatform
{
    private const string BotProvider = AuthEnums.IntegrationProvider.Twitch + "_bot";
    private const string UserProvider = AuthEnums.IntegrationProvider.Twitch;

    private readonly ITwitchHelixTransport _transport;
    private readonly ITwitchModerationApi _moderation;
    private readonly ITwitchIdentityResolver _identityResolver;
    private readonly IApplicationDbContext _db;
    private readonly IHelixBadgeSendGate _badgeGate;
    private readonly TwitchOptions _options;
    private readonly ILogger<HelixChatProvider> _logger;

    // Bot sender identity resolved PER BROADCASTER — never one process-wide account. On a multi-tenant
    // deployment each channel's send must ride the identity whose token signs THAT channel's send, so the
    // cache is keyed by broadcaster. Scoped lifetime, so it lives for one request/pipeline run and spares
    // repeat sends to the same channel a DB round-trip.
    private readonly Dictionary<Guid, BotSenderIdentity> _senderByBroadcaster = new();

    public string Provider => AuthEnums.Platform.Twitch;

    public HelixChatProvider(
        ITwitchHelixTransport transport,
        ITwitchModerationApi moderation,
        ITwitchIdentityResolver identityResolver,
        IApplicationDbContext db,
        IHelixBadgeSendGate badgeGate,
        IOptions<TwitchOptions> options,
        ILogger<HelixChatProvider> logger
    )
    {
        _transport = transport;
        _moderation = moderation;
        _identityResolver = identityResolver;
        _db = db;
        _badgeGate = badgeGate;
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

        // Prefer the app-access-token send when the bot has granted `user:bot`: Twitch shows the chatbot badge
        // ONLY for a message sent on an app token (bot `user:bot` + bot-is-mod / broadcaster `channel:bot`),
        // never on a user token. The broadcaster-side half is per CHANNEL: when the broadcaster granted
        // `channel:bot` the send is eligible by grant; otherwise the attempt itself is the only proof the bot
        // is a moderator there — so we try optimistically, but a rejection gates that channel for a while
        // (IHelixBadgeSendGate) instead of paying a doomed extra Helix call on every message. Either way a
        // failed app-token send falls back to the user-token send so the message still goes out — just
        // without the badge.
        bool attemptAppToken =
            sender.CanUseAppToken
            && (sender.ChannelGrantsBotScope || !_badgeGate.IsBlocked(broadcasterId));

        if (attemptAppToken)
        {
            if (
                await TrySendAsync(
                    broadcasterId,
                    twitchBroadcasterId,
                    sender,
                    message,
                    replyToMessageId,
                    TwitchHelixAuth.BotApp,
                    ct
                )
            )
            {
                _badgeGate.Clear(broadcasterId);
                return true;
            }

            // With `channel:bot` granted a rejection is transient (retry next message); without it the
            // rejection means the bot isn't a moderator there either — gate the channel until the TTL
            // re-proves it, and say exactly what restores the badge.
            if (!sender.ChannelGrantsBotScope)
            {
                _badgeGate.Block(broadcasterId);
                _logger.LogInformation(
                    "HelixChatProvider: badge (app-token) send rejected in {BroadcasterId} — the broadcaster "
                        + "has not granted channel:bot and the bot is not a moderator there. Falling back to the "
                        + "bot's user token (no badge); grant channel:bot or mod the bot to restore it.",
                    broadcasterId
                );
            }
        }

        return await TrySendAsync(
            broadcasterId,
            twitchBroadcasterId,
            sender,
            message,
            replyToMessageId,
            sender.FallbackAuth,
            ct
        );
    }

    /// <summary>
    /// Posts one chat message on the given <paramref name="auth"/>. Only the <see cref="TwitchHelixAuth.User"/>
    /// (owner-as-bot) path is tenant-scoped — it rides THIS channel's broadcaster token, so the transport needs
    /// the tenant to resolve it; the shared-bot (<c>App</c>) and app-token (<c>BotApp</c>) paths ride a
    /// subject-agnostic token, so they carry no tenant. Returns false on any transport failure (the caller
    /// decides whether to retry on a different token).
    /// </summary>
    private async Task<bool> TrySendAsync(
        Guid broadcasterId,
        string twitchBroadcasterId,
        BotSenderIdentity sender,
        string message,
        string? replyToMessageId,
        TwitchHelixAuth auth,
        CancellationToken ct
    )
    {
        TwitchHelixRequest request = new(
            HttpMethod.Post,
            "chat/messages",
            auth,
            BroadcasterId: auth == TwitchHelixAuth.User ? broadcasterId : null,
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
                "HelixChatProvider: {Auth} send to {BroadcasterId} failed: {Error}",
                auth,
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

        BotSenderIdentity? sender =
            await SharedBotSenderAsync(broadcasterId, ct)
            ?? await OwnerSenderAsync(broadcasterId, ct);

        if (sender is not null)
            _senderByBroadcaster[broadcasterId] = sender;

        return sender;
    }

    /// <summary>
    /// The shared platform bot account as the bot sender (<c>twitch_bot</c>, no broadcaster) — its fallback
    /// send rides the bot's own user token (<see cref="TwitchHelixAuth.App"/>). Null when no shared bot with a
    /// real account id exists. <c>CanUseAppToken</c> is set when the bot granted <c>user:bot</c>, so the send
    /// can ride the badge-bearing app token; <c>ChannelGrantsBotScope</c> carries the broadcaster-side half —
    /// whether THIS channel's broadcaster granted <c>channel:bot</c>.
    /// </summary>
    private async Task<BotSenderIdentity?> SharedBotSenderAsync(
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        var row = await _db
            .IntegrationConnections.IgnoreQueryFilters()
            .Where(c => c.Provider == BotProvider && c.BroadcasterId == null && c.DeletedAt == null)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new { c.ProviderAccountId, c.Scopes })
            .FirstOrDefaultAsync(ct);

        if (row is null || string.IsNullOrEmpty(row.ProviderAccountId))
            return null;

        return new BotSenderIdentity(
            TwitchHelixAuth.App,
            row.ProviderAccountId,
            HasBotScope(row.Scopes),
            await BroadcasterGrantsChannelBotAsync(broadcasterId, ct)
        );
    }

    /// <summary>
    /// The channel's OWN owner account as the bot sender (<c>twitch</c>, this broadcaster) — the fallback send
    /// rides the same channel's broadcaster user token (<see cref="TwitchHelixAuth.User"/>), so <c>sender_id</c>
    /// and the token always belong to one account. Scoped strictly to <paramref name="broadcasterId"/>, never
    /// the oldest connection across all tenants. Null when this channel has no owner connection. <c>CanUseAppToken</c>
    /// is set when the owner-as-bot grant carries <c>user:bot</c> (the single-account model still earns the badge).
    /// </summary>
    private async Task<BotSenderIdentity?> OwnerSenderAsync(
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        var row = await _db
            .IntegrationConnections.IgnoreQueryFilters()
            .Where(c =>
                c.Provider == UserProvider
                && c.BroadcasterId == broadcasterId
                && c.DeletedAt == null
            )
            .OrderBy(c => c.CreatedAt)
            .Select(c => new { c.ProviderAccountId, c.Scopes })
            .FirstOrDefaultAsync(ct);

        return row is null || string.IsNullOrEmpty(row.ProviderAccountId)
            ? null
            : new BotSenderIdentity(
                TwitchHelixAuth.User,
                row.ProviderAccountId,
                HasBotScope(row.Scopes),
                HasChannelBotScope(row.Scopes)
            );
    }

    /// <summary>
    /// The broadcaster-side half of the app-token requirement for one channel: true when THIS channel's
    /// owner connection granted <c>channel:bot</c>. When absent the channel may still be eligible via
    /// bot-is-mod, which only the send attempt itself can prove.
    /// </summary>
    private async Task<bool> BroadcasterGrantsChannelBotAsync(
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        List<string>? scopes = await _db
            .IntegrationConnections.IgnoreQueryFilters()
            .Where(c =>
                c.Provider == UserProvider
                && c.BroadcasterId == broadcasterId
                && c.DeletedAt == null
            )
            .OrderBy(c => c.CreatedAt)
            .Select(c => c.Scopes)
            .FirstOrDefaultAsync(ct);

        return scopes is not null && HasChannelBotScope(scopes);
    }

    /// <summary>True when the sender's granted scope set carries <c>user:bot</c> — the prerequisite for the
    /// app-token (badge) send. The other side (<c>channel:bot</c> / bot-is-mod) is per channel — see
    /// <see cref="BroadcasterGrantsChannelBotAsync"/> and the gate in <see cref="PostChatMessageAsync"/>.</summary>
    private static bool HasBotScope(IEnumerable<string> scopes) =>
        scopes.Contains(TwitchScopes.UserBot, StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the scope set carries the broadcaster-side <c>channel:bot</c> grant.</summary>
    private static bool HasChannelBotScope(IEnumerable<string> scopes) =>
        scopes.Contains(TwitchScopes.ChannelBot, StringComparer.OrdinalIgnoreCase);

    /// <summary>The resolved bot sender for one channel: the fallback token the send rides, the account id on
    /// it, whether the badge-bearing app-token send is available (<c>user:bot</c> granted), and whether this
    /// channel's broadcaster granted <c>channel:bot</c> (the by-grant half of the app-token requirement).</summary>
    private sealed record BotSenderIdentity(
        TwitchHelixAuth FallbackAuth,
        string TwitchUserId,
        bool CanUseAppToken,
        bool ChannelGrantsBotScope
    );
}
