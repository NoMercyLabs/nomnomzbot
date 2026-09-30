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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Platform;

/// <summary>
/// Populates the in-memory <see cref="IChannelRegistry"/> from the database on startup.
/// Without this the registry starts empty and commands/timers never fire until the channel
/// is evicted and re-registered — which effectively means they never fire at all.
///
/// Deliberately NOT behind the cluster lease: the registry is this process's own memory, so every
/// instance must fill its own. During a blue/green overlap the incoming colour is the one that must
/// serve once the outgoing colour stops — a lease here left it with an empty registry.
/// </summary>
public sealed class ChannelRegistryBootstrapService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IChannelRegistry _registry;
    private readonly ILogger<ChannelRegistryBootstrapService> _logger;

    public ChannelRegistryBootstrapService(
        IServiceScopeFactory scopeFactory,
        IChannelRegistry registry,
        ILogger<ChannelRegistryBootstrapService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        // Provider-agnostic: every live channel row gets pre-loaded regardless of which platform it runs
        // on. TwitchChannelId is Twitch-only (null for a Kick/YouTube-only channel), so filtering on it
        // silently dropped every non-Twitch channel from the bootstrap pass — a Kick-only channel never
        // got a registry entry until its first chat message forced a lazy load, and until then welcome /
        // triggers / timers never fired. A row carries its platform channel id in EITHER TwitchChannelId
        // (Twitch) or ExternalChannelId (platform-identity.md §9.4's provider-agnostic key, set for every
        // provider including Twitch on a fully-backfilled row) — accept a row with either populated so an
        // older Twitch row that only ever got TwitchChannelId written still bootstraps.
        List<Channel> channels = await db
            .Channels.IgnoreQueryFilters()
            .Where(c =>
                c.DeletedAt == null && (c.TwitchChannelId != null || c.ExternalChannelId != "")
            )
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "Bootstrapping channel registry with {Count} channel(s)",
            channels.Count
        );

        foreach (Channel channel in channels)
        {
            try
            {
                // Prefer the provider-agnostic key; fall back to the legacy Twitch-only field for a row
                // that predates ExternalChannelId being backfilled.
                string platformChannelId = !string.IsNullOrEmpty(channel.ExternalChannelId)
                    ? channel.ExternalChannelId
                    : channel.TwitchChannelId!;

                await _registry.GetOrCreateAsync(
                    channel.Id,
                    platformChannelId,
                    channel.Name,
                    cancellationToken
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to pre-load channel {ChannelId} ({ChannelName}) into registry",
                    channel.Id,
                    channel.Name
                );
            }
        }

        _logger.LogInformation(
            "Channel registry bootstrap complete: {Count} channel(s) loaded",
            _registry.Count
        );
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
