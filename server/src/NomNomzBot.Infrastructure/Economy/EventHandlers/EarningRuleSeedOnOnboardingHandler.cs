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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Economy.EventHandlers;

/// <summary>
/// Onboarding seed job (Economy domain): seeds one <see cref="EarningRule"/> per
/// <see cref="EarningSource"/> for a newly-onboarded channel. All rules are seeded
/// <see cref="EarningRule.IsEnabled">disabled</see> so the broadcaster must opt in per-source from the Earning
/// Rules page. The defaults are tuned for a mid-sized stream: 1 currency per chat message, 100 per follow, 200 per
/// subscription/gift, 1 per bit cheered, 500 per raid. Idempotent: existing rules are skipped so a backfill or
/// re-onboard never duplicates rows. Uses <see cref="IServiceScopeFactory"/> to create its own
/// <see cref="IApplicationDbContext"/> scope so it never contends with parallel onboarding seed handlers that
/// share the EventBus dispatch scope.
/// </summary>
public sealed class EarningRuleSeedOnOnboardingHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<EarningRuleSeedOnOnboardingHandler> logger
) : IEventHandler<ChannelOnboardedEvent>
{
    private static readonly IReadOnlyList<(
        EarningSource Source,
        long Rate,
        string Description
    )> Defaults =
    [
        (EarningSource.ChatMessage, 1, "1 point per message"),
        (EarningSource.WatchTime, 10, "10 points per watch-time window"),
        (EarningSource.Follow, 100, "100 points for a follow"),
        (EarningSource.Subscription, 200, "200 points for a new subscription"),
        (EarningSource.GiftSubscription, 200, "200 points for a gifted subscription"),
        (EarningSource.Cheer, 1, "1 point per bit cheered"),
        (EarningSource.Raid, 500, "500 points for bringing a raid"),
        (
            EarningSource.Supporter,
            250,
            "250 points for a supporter event (tip / membership / merch)"
        ),
    ];

    public async Task HandleAsync(ChannelOnboardedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        logger.LogInformation(
            "Onboarding seed (economy): seeding default earning rules for {BroadcasterId} ({Name})",
            @event.BroadcasterId,
            @event.Name
        );

        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IApplicationDbContext db =
                scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            HashSet<EarningSource> existing = (
                await db
                    .EarningRules.Where(r =>
                        r.BroadcasterId == @event.BroadcasterId && r.DeletedAt == null
                    )
                    .Select(r => r.Source)
                    .ToListAsync(ct)
            ).ToHashSet();

            foreach ((EarningSource source, long rate, string _) in Defaults)
            {
                if (existing.Contains(source))
                    continue;

                db.EarningRules.Add(
                    new EarningRule
                    {
                        BroadcasterId = @event.BroadcasterId,
                        Source = source,
                        IsEnabled = false,
                        Rate = rate,
                    }
                );
            }

            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Onboarding seed (economy): earning rules seeded for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Onboarding seed (economy): failed to seed earning rules for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
    }
}
