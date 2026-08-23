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
using NomNomzBot.Application.Contracts.Chat;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// THE registered <see cref="IChatProvider"/> (BUILD slice 3): routes every chat operation to the
/// <see cref="IChatPlatform"/> serving the tenant channel's <c>Channel.Provider</c>, so commands,
/// pipelines, timers, and the dashboard all speak to the right platform with zero call-site changes.
/// The provider key is resolved once per tenant and cached for the scope's lifetime (channels never
/// change platform); an unknown/unregistered provider falls back to Twitch — the dominant platform and
/// the pre-seam behavior — with a warning, never a throw into the hot chat path.
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
/// </summary>
public sealed class ChatPlatformRouter : IChatProvider
{
    private readonly IReadOnlyDictionary<string, IChatPlatform> _platforms;
    private readonly IApplicationDbContext _db;
    private readonly IOutboundChatShaper _shaper;
    private readonly IChatSendQueue _sendQueue;
    private readonly ILogger<ChatPlatformRouter> _logger;
    private readonly Dictionary<Guid, string> _providerByTenant = [];

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
        IChatPlatform platform = await ResolveAsync(broadcasterId, cancellationToken);
        string provider = platform.Provider;
        string queueKey = $"{broadcasterId:D}:{provider}";
        string coalesceKey = $"{queueKey}|msg|{message}";

        return await _sendQueue.EnqueueAsync(
            queueKey,
            coalesceKey,
            async ct =>
            {
                IReadOnlyList<string> chunks = _shaper.Shape(
                    provider,
                    queueKey,
                    BotEmittedLine.Stamp(message)
                );
                bool allSucceeded = true;
                foreach (string chunk in chunks)
                {
                    bool sent = await platform.SendMessageAsync(broadcasterId, chunk, ct);
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

    public async Task<bool> SendReplyAsync(
        Guid broadcasterId,
        string replyToMessageId,
        string message,
        CancellationToken cancellationToken = default
    )
    {
        IChatPlatform platform = await ResolveAsync(broadcasterId, cancellationToken);
        string provider = platform.Provider;
        string queueKey = $"{broadcasterId:D}:{provider}";
        string coalesceKey = $"{queueKey}|reply|{replyToMessageId}|{message}";

        return await _sendQueue.EnqueueAsync(
            queueKey,
            coalesceKey,
            async ct =>
            {
                IReadOnlyList<string> chunks = _shaper.Shape(
                    provider,
                    queueKey,
                    BotEmittedLine.Stamp(message)
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
    ) =>
        await (await ResolveAsync(broadcasterId, cancellationToken)).TimeoutUserAsync(
            broadcasterId,
            userId,
            durationSeconds,
            reason,
            cancellationToken
        );

    public async Task BanUserAsync(
        Guid broadcasterId,
        string userId,
        string? reason = null,
        CancellationToken cancellationToken = default
    ) =>
        await (await ResolveAsync(broadcasterId, cancellationToken)).BanUserAsync(
            broadcasterId,
            userId,
            reason,
            cancellationToken
        );

    public async Task UnbanUserAsync(
        Guid broadcasterId,
        string userId,
        CancellationToken cancellationToken = default
    ) =>
        await (await ResolveAsync(broadcasterId, cancellationToken)).UnbanUserAsync(
            broadcasterId,
            userId,
            cancellationToken
        );

    public async Task DeleteMessageAsync(
        Guid broadcasterId,
        string messageId,
        CancellationToken cancellationToken = default
    ) =>
        await (await ResolveAsync(broadcasterId, cancellationToken)).DeleteMessageAsync(
            broadcasterId,
            messageId,
            cancellationToken
        );

    private async Task<IChatPlatform> ResolveAsync(Guid broadcasterId, CancellationToken ct)
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
            "No chat platform registered for provider '{Provider}' (channel {BroadcasterId}) — falling back to twitch",
            provider,
            broadcasterId
        );
        return _platforms[AuthEnums.Platform.Twitch];
    }
}
