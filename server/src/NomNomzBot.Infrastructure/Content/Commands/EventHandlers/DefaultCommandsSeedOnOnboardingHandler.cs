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
/// Onboarding seed job (Commands / builtins domain): seeds the shipped default music builtins (<c>!sr</c>,
/// <c>!skip</c>, <c>!queue</c>, <c>!volume</c>, <c>!song</c>) as <c>ChannelBuiltinCommand</c> rows for the
/// newly-onboarded channel immediately, instead of waiting for <see cref="DefaultCommandsSeeder"/>'s next
/// full-startup pass (order 80). Delegates to that same seeder — scoped to this one channel via
/// <see cref="DefaultCommandsSeeder.SeedAsync(Guid?, CancellationToken)"/> — so there is exactly one
/// idempotent upsert-by-natural-key implementation, never a duplicate. This intentionally does NOT re-create
/// the legacy <c>Command</c>-table "template" rows the manual onboarding endpoint used to seed for these same
/// keys — that table was explicitly purged of builtins by the <c>RemoveBuiltinCommandsFromCommandsTable</c>
/// migration, and <c>ChannelBuiltinCommand</c> is the sole canonical home. Independently resilient — caught +
/// logged, never propagated, so it cannot affect the other onboarding seed jobs.
/// </summary>
public sealed class DefaultCommandsSeedOnOnboardingHandler(
    DefaultCommandsSeeder seeder,
    ILogger<DefaultCommandsSeedOnOnboardingHandler> logger
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
                "Onboarding seed (default commands): builtin music commands seeded for {BroadcasterId} ({Name})",
                @event.BroadcasterId,
                @event.Name
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Onboarding seed (default commands): failed for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
    }
}
