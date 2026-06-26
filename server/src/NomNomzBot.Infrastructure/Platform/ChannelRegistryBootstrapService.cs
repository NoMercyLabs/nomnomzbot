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

        List<Channel> channels = await db
            .Channels.IgnoreQueryFilters()
            .Where(c => c.DeletedAt == null && c.TwitchChannelId != null)
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "Bootstrapping channel registry with {Count} channel(s)",
            channels.Count
        );

        foreach (Channel channel in channels)
        {
            try
            {
                await _registry.GetOrCreateAsync(
                    channel.Id,
                    channel.TwitchChannelId!,
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
