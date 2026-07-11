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
/// </summary>
public sealed class ChatPlatformRouter : IChatProvider
{
    private readonly IReadOnlyDictionary<string, IChatPlatform> _platforms;
    private readonly IApplicationDbContext _db;
    private readonly ILogger<ChatPlatformRouter> _logger;
    private readonly Dictionary<Guid, string> _providerByTenant = [];

    public ChatPlatformRouter(
        IEnumerable<IChatPlatform> platforms,
        IApplicationDbContext db,
        ILogger<ChatPlatformRouter> logger
    )
    {
        _platforms = platforms.ToDictionary(p => p.Provider, StringComparer.Ordinal);
        _db = db;
        _logger = logger;
    }

    public async Task<bool> SendMessageAsync(
        Guid broadcasterId,
        string message,
        CancellationToken cancellationToken = default
    ) =>
        await (await ResolveAsync(broadcasterId, cancellationToken)).SendMessageAsync(
            broadcasterId,
            message,
            cancellationToken
        );

    public async Task SendReplyAsync(
        Guid broadcasterId,
        string replyToMessageId,
        string message,
        CancellationToken cancellationToken = default
    ) =>
        await (await ResolveAsync(broadcasterId, cancellationToken)).SendReplyAsync(
            broadcasterId,
            replyToMessageId,
            message,
            cancellationToken
        );

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
