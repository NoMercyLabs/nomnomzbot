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
/// per <see cref="EventResponsePresetCatalog"/> event type. The core Twitch alerts start ON and every other
/// type starts off. A row carries no message: an event that ships ON speaks a line from
/// <see cref="EventResponseToneCatalog"/> in each channel's tone until a platform admin writes text.
/// GLOBAL reference data (Order 12). It ADDS missing event types and clears a message that still equals the
/// <see cref="LegacyMessages"/> line this seeder wrote before tones existed, so an untouched row becomes
/// tone-aware; any other message is an admin's edit and survives every redeploy.
/// </summary>
public sealed class PlatformEventResponseDefaultsSeeder(IApplicationDbContext db) : ISeeder
{
    /// <summary>The enabled event types and the fixed line each used to be seeded with — now the first Informative line.</summary>
    internal static readonly IReadOnlyDictionary<string, string> LegacyMessages = new Dictionary<
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

    /// <summary>The types that ship ON: the legacy alerts plus the poll result, which the old bot also announced.</summary>
    internal static readonly HashSet<string> EnabledByDefault = new(
        LegacyMessages.Keys.Append("channel.poll.end"),
        StringComparer.Ordinal
    );

    public int Order => 12;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        List<PlatformEventResponseDefault> existing =
            await db.PlatformEventResponseDefaults.ToListAsync(ct);

        foreach (PlatformEventResponseDefault row in existing)
        {
            if (
                LegacyMessages.TryGetValue(row.EventType, out string? legacy)
                && string.Equals(row.Message, legacy, StringComparison.Ordinal)
            )
                row.Message = null;
        }

        HashSet<string> present = existing
            .Select(d => d.EventType)
            .ToHashSet(StringComparer.Ordinal);
        foreach (string eventType in EventResponsePresetCatalog.EventTypes)
        {
            if (present.Contains(eventType))
                continue;

            db.PlatformEventResponseDefaults.Add(
                new() { EventType = eventType, IsEnabled = EnabledByDefault.Contains(eventType) }
            );
        }
        await db.SaveChangesAsync(ct);
    }
}
