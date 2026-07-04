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
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Commands.EventHandlers;

/// <summary>
/// Onboarding seed job (Commands domain): seeds the six default <see cref="EventResponse"/> rows (follow, sub,
/// gift-sub, resub, cheer, raid) for a newly-onboarded channel — exactly what the former manual onboarding
/// endpoint (<c>POST /channels</c>) used to seed inline, now the only place that does it. Idempotent: skipped
/// per <c>(BroadcasterId, EventType)</c>, so a re-onboard or the startup backfill never duplicates a response —
/// or clobbers one the broadcaster has already edited. Independently resilient — caught + logged, never
/// propagated, so it cannot affect the other onboarding seed jobs. Uses <see cref="IServiceScopeFactory"/> to
/// create its own <see cref="IApplicationDbContext"/> scope, matching
/// <c>EarningRuleSeedOnOnboardingHandler</c>'s isolation for a multi-row insert.
/// </summary>
public sealed class EventResponseSeedOnOnboardingHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<EventResponseSeedOnOnboardingHandler> logger
) : IEventHandler<ChannelOnboardedEvent>
{
    private static readonly IReadOnlyList<(string EventType, string Message)> Defaults =
    [
        ("channel.follow", "Welcome {user}! Thanks for the follow!"),
        ("channel.subscribe", "{user} just subscribed! Thank you for the support!"),
        ("channel.subscription.gift", "{user} gifted {amount} sub(s)! How generous!"),
        ("channel.subscription.message", "{user} resubscribed for {months} months! {message}"),
        ("channel.cheer", "{user} cheered {amount} bits! Thank you!"),
        ("channel.raid", "{user} is raiding with {viewers} viewers! Welcome raiders!"),
    ];

    public async Task HandleAsync(ChannelOnboardedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        logger.LogInformation(
            "Onboarding seed (event responses): seeding default event responses for {BroadcasterId} ({Name})",
            @event.BroadcasterId,
            @event.Name
        );

        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IApplicationDbContext db =
                scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            HashSet<string> existing = (
                await db
                    .EventResponses.Where(r =>
                        r.BroadcasterId == @event.BroadcasterId && r.DeletedAt == null
                    )
                    .Select(r => r.EventType)
                    .ToListAsync(ct)
            ).ToHashSet(StringComparer.OrdinalIgnoreCase);

            int seeded = 0;
            foreach ((string eventType, string message) in Defaults)
            {
                if (existing.Contains(eventType))
                    continue;

                db.EventResponses.Add(
                    new EventResponse
                    {
                        BroadcasterId = @event.BroadcasterId,
                        EventType = eventType,
                        IsEnabled = true,
                        ResponseType = "chat_message",
                        Message = message,
                    }
                );
                seeded++;
            }

            if (seeded > 0)
                await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Onboarding seed (event responses): completed for {BroadcasterId} — {Count} response(s) seeded",
                @event.BroadcasterId,
                seeded
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Onboarding seed (event responses): failed for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
    }
}
