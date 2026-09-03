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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Chat;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// THE registered <see cref="IChatProvider"/> (BUILD slice 3): routes every chat operation to the
/// <see cref="IChatPlatform"/> serving the tenant channel's <c>Channel.Provider</c>, so commands,
/// pipelines, timers, and the dashboard all speak to the right platform with zero call-site changes.
/// The provider key is resolved once per tenant and cached for the scope's lifetime (channels never
/// change their primary platform); an unknown/unregistered provider is dropped with a warning — NEVER
/// a silent fall-through to Twitch or any other platform (S021) — and never a throw into the hot chat
/// path. <see cref="IInboundOriginChatSender"/> is this router's OTHER registered interface (same scoped
/// instance): it routes by an explicit provider key — the platform an inbound message actually arrived
/// on — instead of the tenant's single <c>Channel.Provider</c>, which is wrong once a channel has more
/// than one platform connection live at once (a Kick reply must never be able to reach Twitch just
/// because Twitch happens to be the channel's primary platform).
///
/// This is also where every outbound line is stamped with <see cref="BotEmittedLine.Marker"/> (S009b):
/// the router is the ONE seam every bot-voice send crosses regardless of platform, so stamping here —
/// rather than inside each <see cref="IChatPlatform"/> implementation — means a future fourth platform
/// inherits the loop-guard automatically just by registering, instead of each implementation having to
/// remember to call <see cref="BotEmittedLine.Stamp"/> itself. <see cref="OperatorChatSender"/> is a
/// deliberately separate path (a human operator's own composer send) and is never stamped.
///
/// S010 (outbound shaping): every send also passes through <see cref="IOutboundChatShaper"/> — chunked to
/// the resolved platform's visible-character budget and given a trailing invisible variation when it
/// repeats the previous line verbatim — and through <see cref="IChatSendQueue"/>, a per-channel-per-
/// platform token bucket that paces sends to the platform's limit and coalesces concurrent identical
/// sends. A chunk the platform rejects is never swallowed: it is logged and folds the whole call's result
/// to <c>false</c> (S008d) rather than reporting success for a partially-delivered line.
///
/// S011 (bot-line prefix, D5): before shaping, the visible <c>Channel.BotLinePrefix</c> (e.g. <c>*</c> or
/// an emoji) is prepended to the message body — NOT the invisible <see cref="BotEmittedLine"/> marker,
/// which stays a separate, always-applied loop-guard stamp. The prefix is applied here, before
/// <see cref="IOutboundChatShaper.Shape"/> chunks the body, so it becomes part of the chunked text itself:
/// it appears exactly once, on the first chunk, and counts toward the platform's character budget like any
/// other text (never appended after chunking, which would either repeat it per chunk or let it overflow
/// the budget unaccounted for). The prefix is applied ONLY when the bot has no dedicated account connected
/// for this tenant (self-host default: the bot types as the streamer's own account, D5) — once a dedicated
/// bot account is connected, its distinct username already tells viewers apart from the streamer, so the
/// prefix would be redundant noise and is skipped.
/// </summary>
public sealed class ChatPlatformRouter : IChatProvider, IInboundOriginChatSender
{
    private readonly IReadOnlyDictionary<string, IChatPlatform> _platforms;
    private readonly IApplicationDbContext _db;
    private readonly IOutboundChatShaper _shaper;
    private readonly IChatSendQueue _sendQueue;
    private readonly ILogger<ChatPlatformRouter> _logger;
    private readonly Dictionary<Guid, string> _providerByTenant = [];
    private readonly Dictionary<Guid, string?> _botLinePrefixByTenant = [];

    public ChatPlatformRouter(
        IEnumerable<IChatPlatform> platforms,
        IApplicationDbContext db,
        IOutboundChatShaper shaper,
        IChatSendQueue sendQueue,
        ILogger<ChatPlatformRouter> logger
    )
    {
        _platforms = platforms.ToDictionary(p => p.Provider, StringComparer.Ordinal);
        _db = db;
        _shaper = shaper;
        _sendQueue = sendQueue;
        _logger = logger;
    }

    public async Task<bool> SendMessageAsync(
        Guid broadcasterId,
        string message,
        CancellationToken cancellationToken = default
    )
    {
        IChatPlatform? platform = await ResolveAsync(broadcasterId, cancellationToken);
        return platform is null
            ? false
            : await SendMessageViaAsync(
                platform,
                broadcasterId,
                message,
                asBroadcaster: false,
                cancellationToken
            );
    }

    public async Task<bool> SendMessageAsBroadcasterAsync(
        Guid broadcasterId,
        string message,
        CancellationToken cancellationToken = default
    )
    {
        IChatPlatform? platform = await ResolveAsync(broadcasterId, cancellationToken);
        return platform is null
            ? false
            : await SendMessageViaAsync(
                platform,
                broadcasterId,
                message,
                asBroadcaster: true,
                cancellationToken
            );
    }

    public async Task<bool> SendReplyAsync(
        Guid broadcasterId,
        string replyToMessageId,
        string message,
        CancellationToken cancellationToken = default
    )
    {
        IChatPlatform? platform = await ResolveAsync(broadcasterId, cancellationToken);
        return platform is null
            ? false
            : await SendReplyViaAsync(
                platform,
                broadcasterId,
                replyToMessageId,
                message,
                cancellationToken
            );
    }

    /// <summary>
    /// S021 — the counterpart to <see cref="SendMessageAsync(Guid,string,CancellationToken)"/> that routes
    /// by an EXPLICIT provider key (the platform an inbound message actually arrived on) instead of the
    /// tenant channel's single <c>Channel.Provider</c> field. A provider with no registered
    /// <see cref="IChatPlatform"/> is an honest failure — never a silent fall-through to Twitch or any
    /// other platform, and no send is attempted anywhere.
    /// </summary>
    public async Task<Result> SendMessageAsync(
        Guid broadcasterId,
        string provider,
        string message,
        CancellationToken cancellationToken = default
    )
    {
        if (!_platforms.TryGetValue(provider, out IChatPlatform? platform))
            return UnsupportedProviderFailure(broadcasterId, provider);

        bool sent = await SendMessageViaAsync(
            platform,
            broadcasterId,
            message,
            asBroadcaster: false,
            cancellationToken
        );
        return sent
            ? Result.Success()
            : Result.Failure($"The '{provider}' chat platform rejected the send.", "send_rejected");
    }

    /// <summary>
    /// S021 — the counterpart to <see cref="SendReplyAsync(Guid,string,string,CancellationToken)"/> that
    /// routes by an EXPLICIT provider key. See <see cref="SendMessageAsync(Guid,string,string,CancellationToken)"/>
    /// for the honest-failure contract on an unregistered provider.
    /// </summary>
    public async Task<Result> SendReplyAsync(
        Guid broadcasterId,
        string provider,
        string replyToMessageId,
        string message,
        CancellationToken cancellationToken = default
    )
    {
        if (!_platforms.TryGetValue(provider, out IChatPlatform? platform))
            return UnsupportedProviderFailure(broadcasterId, provider);

        bool sent = await SendReplyViaAsync(
            platform,
            broadcasterId,
            replyToMessageId,
            message,
            cancellationToken
        );
        return sent
            ? Result.Success()
            : Result.Failure($"The '{provider}' chat platform rejected the send.", "send_rejected");
    }

    private Result UnsupportedProviderFailure(Guid broadcasterId, string provider)
    {
        _logger.LogWarning(
            "No chat platform registered for provider '{Provider}' (channel {BroadcasterId}) — inbound-origin send refused, never routed to another platform",
            provider,
            broadcasterId
        );
        return Result.Failure(
            $"No chat platform is registered for provider '{provider}'.",
            "unsupported_provider"
        );
    }

    private async Task<bool> SendMessageViaAsync(
        IChatPlatform platform,
        Guid broadcasterId,
        string message,
        bool asBroadcaster,
        CancellationToken cancellationToken
    )
    {
        string provider = platform.Provider;
        string queueKey = $"{broadcasterId:D}:{provider}";
        string coalesceKey = $"{queueKey}|msg|{message}";
        // The bot-line prefix distinguishes the bot's voice from the streamer's own — a message sent AS
        // the broadcaster is the streamer's own voice by definition, so it never gets that prefix either.
        string? botLinePrefix = asBroadcaster
            ? null
            : await ResolveBotLinePrefixAsync(broadcasterId, cancellationToken);

        return await _sendQueue.EnqueueAsync(
            queueKey,
            coalesceKey,
            async ct =>
            {
                string prefixedMessage = botLinePrefix is null ? message : botLinePrefix + message;
                IReadOnlyList<string> chunks = _shaper.Shape(
                    provider,
                    queueKey,
                    BotEmittedLine.Stamp(prefixedMessage)
                );
                bool allSucceeded = true;
                foreach (string chunk in chunks)
                {
                    bool sent = asBroadcaster
                        ? await platform.SendMessageAsBroadcasterAsync(broadcasterId, chunk, ct)
                        : await platform.SendMessageAsync(broadcasterId, chunk, ct);
                    if (!sent)
                    {
                        allSucceeded = false;
                        _logger.LogWarning(
                            "Chat send chunk rejected by {Provider} for channel {BroadcasterId}",
                            provider,
                            broadcasterId
                        );
                    }
                }
                return allSucceeded;
            },
            cancellationToken
        );
    }

    private async Task<bool> SendReplyViaAsync(
        IChatPlatform platform,
        Guid broadcasterId,
        string replyToMessageId,
        string message,
        CancellationToken cancellationToken
    )
    {
        string provider = platform.Provider;
        string queueKey = $"{broadcasterId:D}:{provider}";
        string coalesceKey = $"{queueKey}|reply|{replyToMessageId}|{message}";
        string? botLinePrefix = await ResolveBotLinePrefixAsync(broadcasterId, cancellationToken);

        return await _sendQueue.EnqueueAsync(
            queueKey,
            coalesceKey,
            async ct =>
            {
                string prefixedMessage = botLinePrefix is null ? message : botLinePrefix + message;
                IReadOnlyList<string> chunks = _shaper.Shape(
                    provider,
                    queueKey,
                    BotEmittedLine.Stamp(prefixedMessage)
                );
                bool allSucceeded = true;
                for (int i = 0; i < chunks.Count; i++)
                {
                    bool sent =
                        i == 0
                            ? await platform.SendReplyAsync(
                                broadcasterId,
                                replyToMessageId,
                                chunks[i],
                                ct
                            )
                            : await platform.SendMessageAsync(broadcasterId, chunks[i], ct);
                    if (!sent)
                    {
                        allSucceeded = false;
                        _logger.LogWarning(
                            "Chat {Kind} chunk rejected by {Provider} for channel {BroadcasterId}",
                            i == 0 ? "reply" : "reply-overflow",
                            provider,
                            broadcasterId
                        );
                    }
                }
                return allSucceeded;
            },
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
        IChatPlatform? platform = await ResolveForModerationAsync(
            broadcasterId,
            "timeout",
            cancellationToken
        );
        if (platform is not null)
            await platform.TimeoutUserAsync(
                broadcasterId,
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
        IChatPlatform? platform = await ResolveForModerationAsync(
            broadcasterId,
            "ban",
            cancellationToken
        );
        if (platform is not null)
            await platform.BanUserAsync(broadcasterId, userId, reason, cancellationToken);
    }

    public async Task<ChatUnbanOutcome> UnbanUserAsync(
        Guid broadcasterId,
        string userId,
        CancellationToken cancellationToken = default
    )
    {
        IChatPlatform? platform = await ResolveForModerationAsync(
            broadcasterId,
            "unban",
            cancellationToken
        );
        return platform is null
            ? ChatUnbanOutcome.Failed
            : await platform.UnbanUserAsync(broadcasterId, userId, cancellationToken);
    }

    public async Task DeleteMessageAsync(
        Guid broadcasterId,
        string messageId,
        CancellationToken cancellationToken = default
    )
    {
        IChatPlatform? platform = await ResolveForModerationAsync(
            broadcasterId,
            "delete-message",
            cancellationToken
        );
        if (platform is not null)
            await platform.DeleteMessageAsync(broadcasterId, messageId, cancellationToken);
    }

    /// <summary>
    /// Shared resolve-or-log-and-drop path for the four fire-and-forget moderation operations above —
    /// an unregistered provider is dropped with a warning, never routed to a platform it never happened
    /// on (see <see cref="ResolveAsync"/>).
    /// </summary>
    private async Task<IChatPlatform?> ResolveForModerationAsync(
        Guid broadcasterId,
        string operation,
        CancellationToken ct
    )
    {
        IChatPlatform? platform = await ResolveAsync(broadcasterId, ct);
        if (platform is null)
            _logger.LogWarning(
                "Moderation action '{Operation}' dropped for channel {BroadcasterId} — no chat platform registered",
                operation,
                broadcasterId
            );
        return platform;
    }

    /// <summary>
    /// The visible bot-line prefix (D5) to apply for this tenant, or null when there is nothing to prepend —
    /// either no prefix is configured, or a dedicated bot account is connected (its own username already
    /// distinguishes it from the streamer, so the courtesy prefix would be redundant). Resolved once per
    /// tenant and cached for the scope's lifetime, mirroring <see cref="ResolveAsync"/>'s provider cache.
    /// </summary>
    private async Task<string?> ResolveBotLinePrefixAsync(Guid broadcasterId, CancellationToken ct)
    {
        if (_botLinePrefixByTenant.TryGetValue(broadcasterId, out string? cached))
            return cached;

        string? configuredPrefix = await _db
            .Channels.Where(c => c.Id == broadcasterId)
            .Select(c => c.BotLinePrefix)
            .FirstOrDefaultAsync(ct);

        string? resolved =
            configuredPrefix is not null && !await HasDedicatedBotAsync(broadcasterId, ct)
                ? configuredPrefix
                : null;

        _botLinePrefixByTenant[broadcasterId] = resolved;
        return resolved;
    }

    /// <summary>
    /// True when this tenant sends chat through a dedicated bot account — either its own connected
    /// per-channel custom bot, or (absent that) the shared platform bot — rather than the streamer's own
    /// account. Mirrors <c>BotSelfEchoGuard</c>'s identity-resolution order.
    /// </summary>
    private async Task<bool> HasDedicatedBotAsync(Guid broadcasterId, CancellationToken ct)
    {
        bool hasChannelBot = await _db
            .ChannelBotAuthorizations.IgnoreQueryFilters()
            .AnyAsync(
                a => a.BroadcasterId == broadcasterId && a.IsActive && a.DeletedAt == null,
                ct
            );
        if (hasChannelBot)
            return true;

        return await _db
            .BotAccounts.IgnoreQueryFilters()
            .AnyAsync(
                b =>
                    b.IdentityType == AuthEnums.BotIdentityType.Shared
                    && b.IsActive
                    && b.DeletedAt == null
                    && b.ConnectionId != null,
                ct
            );
    }

    /// <summary>
    /// Resolves the <see cref="IChatPlatform"/> for the tenant channel's own <c>Channel.Provider</c> —
    /// the fallback path used only by non-inbound-triggered sends (pipelines, timers, announcements) that
    /// have no inbound message to key off. S021: an unregistered provider is NEVER silently swapped for
    /// Twitch (or any other platform) — it returns <c>null</c> and callers drop/log the operation, so a
    /// Kick-only tenant never gets a Twitch line it never asked for.
    /// </summary>
    private async Task<IChatPlatform?> ResolveAsync(Guid broadcasterId, CancellationToken ct)
    {
        if (!_providerByTenant.TryGetValue(broadcasterId, out string? provider))
        {
            provider = await _db
                .Channels.Where(c => c.Id == broadcasterId)
                .Select(c => c.Provider)
                .FirstOrDefaultAsync(ct);
            provider ??= AuthEnums.Platform.Twitch;
            _providerByTenant[broadcasterId] = provider;
        }

        if (_platforms.TryGetValue(provider, out IChatPlatform? platform))
            return platform;

        _logger.LogWarning(
            "No chat platform registered for provider '{Provider}' (channel {BroadcasterId}) — chat operation dropped, never routed to another platform",
            provider,
            broadcasterId
        );
        return null;
    }
}
