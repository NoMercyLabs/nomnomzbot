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
/// tenant and cached for the scope's lifetime (channels never change platform). S021b: an
/// unknown/unregistered provider is an honest <see cref="Result{T}"/> failure — NEVER a silent
/// fall-through to Twitch or any other platform — and no call is made against any platform API,
/// mirroring the fix <c>ChatPlatformRouter</c> already applies to chat (S021).
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
    )
    {
        IPlatformApi? platform = await ResolveAsync(broadcasterId, cancellationToken);
        if (platform is null)
        {
            string provider = await ResolveProviderAsync(broadcasterId, cancellationToken);
            return UnsupportedProviderFailure(broadcasterId, provider);
        }

        return await platform.UpdateStreamInfoAsync(broadcasterId, update, cancellationToken);
    }

    private Result<PlatformStreamInfoApplied> UnsupportedProviderFailure(
        Guid broadcasterId,
        string provider
    )
    {
        _logger.LogWarning(
            "No platform API registered for provider '{Provider}' (channel {BroadcasterId}) — update refused, never routed to another platform",
            provider,
            broadcasterId
        );
        return Result<PlatformStreamInfoApplied>.Failure(
            $"No platform API is registered for provider '{provider}'.",
            "unsupported_provider"
        );
    }

    /// <summary>
    /// Resolves the <see cref="IPlatformApi"/> for the tenant channel's own <c>Channel.Provider</c>.
    /// S021b: an unregistered provider is NEVER silently swapped for Twitch (or any other platform) —
    /// it returns <c>null</c> and the caller returns an honest failure, so a Kick-only tenant never gets
    /// its title/category applied to a Twitch channel it never asked for.
    /// </summary>
    private async Task<IPlatformApi?> ResolveAsync(Guid broadcasterId, CancellationToken ct)
    {
        string provider = await ResolveProviderAsync(broadcasterId, ct);
        return _platforms.TryGetValue(provider, out IPlatformApi? platform) ? platform : null;
    }

    private async Task<string> ResolveProviderAsync(Guid broadcasterId, CancellationToken ct)
    {
        if (_providerByTenant.TryGetValue(broadcasterId, out string? provider))
            return provider;

        provider = await _db
            .Channels.Where(c => c.Id == broadcasterId)
            .Select(c => c.Provider)
            .FirstOrDefaultAsync(ct);
        provider ??= AuthEnums.Platform.Twitch;
        _providerByTenant[broadcasterId] = provider;
        return provider;
    }
}
