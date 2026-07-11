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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Platform;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Platform.ChannelOps;

/// <summary>
/// THE registered <see cref="IPlatformChannelApi"/> (BUILD slice 3b): routes a channel operation to the
/// <see cref="IPlatformApi"/> serving the tenant channel's <c>Channel.Provider</c> — the channel-ops
/// twin of <c>ChatPlatformRouter</c>, same resolution mechanics: the provider key is resolved once per
/// tenant and cached for the scope's lifetime (channels never change platform); an unknown/unregistered
/// provider falls back to Twitch — the dominant platform and the pre-seam behavior — with a warning.
/// </summary>
public sealed class PlatformApiRouter : IPlatformChannelApi
{
    private readonly IReadOnlyDictionary<string, IPlatformApi> _platforms;
    private readonly IApplicationDbContext _db;
    private readonly ILogger<PlatformApiRouter> _logger;
    private readonly Dictionary<Guid, string> _providerByTenant = [];

    public PlatformApiRouter(
        IEnumerable<IPlatformApi> platforms,
        IApplicationDbContext db,
        ILogger<PlatformApiRouter> logger
    )
    {
        _platforms = platforms.ToDictionary(p => p.Provider, StringComparer.Ordinal);
        _db = db;
        _logger = logger;
    }

    public async Task<Result<PlatformStreamInfoApplied>> UpdateStreamInfoAsync(
        Guid broadcasterId,
        PlatformStreamInfoUpdate update,
        CancellationToken cancellationToken = default
    ) =>
        await (await ResolveAsync(broadcasterId, cancellationToken)).UpdateStreamInfoAsync(
            broadcasterId,
            update,
            cancellationToken
        );

    private async Task<IPlatformApi> ResolveAsync(Guid broadcasterId, CancellationToken ct)
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

        if (_platforms.TryGetValue(provider, out IPlatformApi? platform))
            return platform;

        _logger.LogWarning(
            "No platform API registered for provider '{Provider}' (channel {BroadcasterId}) — falling back to twitch",
            provider,
            broadcasterId
        );
        return _platforms[AuthEnums.Platform.Twitch];
    }
}
