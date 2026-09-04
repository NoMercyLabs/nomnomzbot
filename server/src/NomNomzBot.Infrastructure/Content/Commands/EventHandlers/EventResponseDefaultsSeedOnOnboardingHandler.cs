// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Content.Commands.EventHandlers;

/// <summary>
/// Onboarding seed job (Commands / event-responses domain): seeds the default (disabled) event-response
/// row for every catalog event type for the newly-onboarded channel immediately, instead of waiting for
/// <see cref="EventResponseDefaultsSeeder"/>'s next full-startup pass (order 81). Delegates to that same
/// seeder — scoped to this one channel via <see cref="EventResponseDefaultsSeeder.SeedAsync(Guid?, CancellationToken)"/>
/// — so there is exactly one idempotent upsert-by-natural-key implementation, never a duplicate. Mirrors
/// <c>DefaultCommandsSeedOnOnboardingHandler</c> (same domain, sibling entity). Independently resilient —
/// caught + logged, never propagated, so it cannot affect the other onboarding seed jobs.
/// </summary>
public sealed class EventResponseDefaultsSeedOnOnboardingHandler(
    EventResponseDefaultsSeeder seeder,
    ILogger<EventResponseDefaultsSeedOnOnboardingHandler> logger
) : IEventHandler<ChannelOnboardedEvent>
{
    public async Task HandleAsync(ChannelOnboardedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        try
        {
            await seeder.SeedAsync(@event.BroadcasterId, ct);

            logger.LogInformation(
                "Onboarding seed (event responses): default triggers seeded for {BroadcasterId} ({Name})",
                @event.BroadcasterId,
                @event.Name
            );
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(
                ex,
                "Onboarding seed (event responses): failed for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
    }
}
