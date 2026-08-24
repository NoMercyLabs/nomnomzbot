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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Contracts.Kick;
using NomNomzBot.Application.Contracts.YouTube;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Platform.Scheduling;

/// <summary>
/// S036 boot sweep — proactively refreshes every provider's expiring tokens once at process startup, before
/// the first real request needs one. <see cref="TokenRefreshService"/> already runs the periodic Twitch-family
/// sweep every 30 minutes; this covers the SAME Twitch pass plus Kick and YouTube (whose access-token
/// providers otherwise only refresh lazily, on the first caller to ask for a token after restart) so a
/// connection that was already expiring when the process went down does not wait for its first caller to pay
/// the refresh latency — and, being an <see cref="IHostedService"/> constructed through DI, uses the SAME
/// <see cref="Identity.IConnectionRefreshGate"/> singleton every request-time refresh does, so a concurrent
/// request for a connection the sweep is already refreshing serializes behind it instead of racing it.
///
/// X (<c>twitter</c>) is intentionally NOT swept — platform-identity.md §10 makes it login-only: its OAuth
/// tokens are vaulted at sign-in but no integration surface reads or refreshes them, so there is nothing to
/// proactively refresh yet. Once X gains an active token-consuming surface it gets a sweep pass here too.
/// </summary>
public sealed class IntegrationTokenBootSweepService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IntegrationTokenBootSweepService> _logger;

    public IntegrationTokenBootSweepService(
        IServiceProvider serviceProvider,
        ILogger<IntegrationTokenBootSweepService> logger
    )
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _serviceProvider.CreateScope();

        await SweepTwitchAsync(scope.ServiceProvider, cancellationToken);
        await SweepKickAsync(scope.ServiceProvider, cancellationToken);
        await SweepYouTubeAsync(scope.ServiceProvider, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SweepTwitchAsync(IServiceProvider services, CancellationToken ct)
    {
        try
        {
            await services.GetRequiredService<ITwitchAuthService>().RefreshExpiringTokensAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Boot token sweep failed for Twitch.");
        }
    }

    private async Task SweepKickAsync(IServiceProvider services, CancellationToken ct)
    {
        IApplicationDbContext db = services.GetRequiredService<IApplicationDbContext>();
        IKickAccessTokenProvider kick = services.GetRequiredService<IKickAccessTokenProvider>();

        List<Guid> broadcasterIds = await db
            .Channels.Where(c => c.Provider == AuthEnums.Platform.Kick)
            .Select(c => c.Id)
            .ToListAsync(ct);

        foreach (Guid broadcasterId in broadcasterIds)
        {
            try
            {
                await kick.GetAsync(broadcasterId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "Boot token sweep failed for Kick broadcaster {BroadcasterId}.",
                    broadcasterId
                );
            }
        }
    }

    private async Task SweepYouTubeAsync(IServiceProvider services, CancellationToken ct)
    {
        IApplicationDbContext db = services.GetRequiredService<IApplicationDbContext>();
        IYouTubeAccessTokenProvider youTube =
            services.GetRequiredService<IYouTubeAccessTokenProvider>();

        // S036c-b — YouTube candidates come from the vault now, not the legacy Service row.
        List<Guid> broadcasterIds = await db
            .IntegrationConnections.Where(c =>
                c.Provider == AuthEnums.IntegrationProvider.YouTube
                && c.Status == AuthEnums.IntegrationStatus.Connected
                && c.BroadcasterId != null
            )
            .Select(c => c.BroadcasterId!.Value)
            .Distinct()
            .ToListAsync(ct);

        foreach (Guid broadcasterId in broadcasterIds)
        {
            try
            {
                await youTube.GetAccessTokenAsync(broadcasterId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "Boot token sweep failed for YouTube broadcaster {BroadcasterId}.",
                    broadcasterId
                );
            }
        }
    }
}
