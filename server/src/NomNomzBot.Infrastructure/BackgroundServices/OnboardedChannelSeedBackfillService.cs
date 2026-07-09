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
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.BackgroundServices;

/// <summary>
/// Startup backfill for the onboarding seed pipeline. Channels that finished onboarding before the seed-job
/// handlers existed (e.g. the first self-host owner) never had their Twitch data pulled in, so their dashboard
/// Rewards / Community / roles pages stay empty. On boot this re-publishes <see cref="ChannelOnboardedEvent"/>
/// for every onboarded channel, which fans out to the same independently-resilient seed handlers as a live
/// onboarding — re-pulling rewards, the moderator roster, and management memberships from Twitch.
///
/// Safe to run on every startup: the seed handlers each call an idempotent upsert sync (matched by Twitch id /
/// title / (channel,user)), so re-publishing never duplicates rows — it only fills gaps and refreshes existing
/// rows. Migrations + content seeding run synchronously before the host starts hosted services, so the
/// <c>Channels</c> table is queryable here. Runs once and exits; the per-channel 5-minute reconciliation that
/// keeps state fresh thereafter lives in <see cref="BotLifecycleService"/> + the role-sync paths.
/// </summary>
public sealed class OnboardedChannelSeedBackfillService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEventBus _eventBus;
    private readonly ILogger<OnboardedChannelSeedBackfillService> _logger;

    public OnboardedChannelSeedBackfillService(
        IServiceScopeFactory scopeFactory,
        IEventBus eventBus,
        ILogger<OnboardedChannelSeedBackfillService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _eventBus = eventBus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            List<ChannelOnboardedEvent> events = await LoadOnboardedChannelsAsync(stoppingToken);

            if (events.Count == 0)
            {
                _logger.LogInformation(
                    "Onboarding seed backfill: no onboarded channels to backfill."
                );
                return;
            }

            _logger.LogInformation(
                "Onboarding seed backfill: re-publishing ChannelOnboardedEvent for {Count} onboarded channel(s).",
                events.Count
            );

            foreach (ChannelOnboardedEvent @event in events)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                // The bus isolates each handler's failure, and every seed handler is itself try/catch-guarded,
                // so one bad channel / handler cannot abort the backfill of the rest.
                await _eventBus.PublishAsync(@event, stoppingToken);
            }

            _logger.LogInformation(
                "Onboarding seed backfill: dispatched seed jobs for {Count} channel(s).",
                events.Count
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Onboarding seed backfill failed.");
        }
    }

    private async Task<List<ChannelOnboardedEvent>> LoadOnboardedChannelsAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        List<ChannelSeedRow> rows = await db
            .Channels.Where(c => c.IsOnboarded)
            .Select(c => new ChannelSeedRow(c.Id, c.OwnerUserId, c.TwitchChannelId!, c.Name))
            .ToListAsync(ct);

        return
        [
            .. rows.Select(r => new ChannelOnboardedEvent
            {
                BroadcasterId = r.Id,
                OwnerUserId = r.OwnerUserId,
                TwitchChannelId = r.TwitchChannelId,
                Name = r.Name,
            }),
        ];
    }

    private sealed record ChannelSeedRow(
        Guid Id,
        Guid OwnerUserId,
        string TwitchChannelId,
        string Name
    );
}
