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
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.Commands.Entities;

namespace NomNomzBot.Infrastructure.Content.Commands;

/// <summary>
/// Seeds the platform event-response defaults (plan item A4): one <see cref="PlatformEventResponseDefault"/>
/// per <see cref="EventResponsePresetCatalog"/> event type. The six core Twitch alerts start ON with the
/// welcome lines new channels used to receive as their own rows at onboarding; every other type starts off.
/// GLOBAL reference data (Order 12). It only ADDS missing event types and never touches an existing row, so
/// the platform admin's edit survives every redeploy.
/// </summary>
public sealed class PlatformEventResponseDefaultsSeeder(IApplicationDbContext db) : ISeeder
{
    private static readonly IReadOnlyDictionary<string, string> EnabledMessages = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        ["channel.follow"] = "Welcome {user}! Thanks for the follow!",
        ["channel.subscribe"] = "{user} just subscribed! Thank you for the support!",
        ["channel.subscription.gift"] = "{user} gifted {count} sub(s)! How generous!",
        ["channel.subscription.gift.anonymous"] =
            "An anonymous gifter gave {count} sub(s) to the community!",
        ["channel.subscription.gift.received"] = "{user} was gifted a sub by {gifter}!",
        ["channel.subscription.gift.received.anonymous"] =
            "An anonymous gifter gave a sub to {user}!",
        ["channel.subscription.message"] =
            "{user} resubscribed for {months} months! Thank you!{also_said}",
        ["channel.cheer"] = "{user} cheered {bits} bits! Thank you!",
        ["channel.raid"] = "{user} is raiding with {viewers} viewers! Welcome raiders!",
    };

    public int Order => 12;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        HashSet<string> present = (
            await db.PlatformEventResponseDefaults.Select(d => d.EventType).ToListAsync(ct)
        ).ToHashSet(StringComparer.Ordinal);

        foreach (string eventType in EventResponsePresetCatalog.EventTypes)
        {
            if (present.Contains(eventType))
                continue;

            string? message = EnabledMessages.GetValueOrDefault(eventType);
            db.PlatformEventResponseDefaults.Add(
                new()
                {
                    EventType = eventType,
                    IsEnabled = message is not null,
                    Message = message,
                }
            );
        }
        await db.SaveChangesAsync(ct);
    }
}
