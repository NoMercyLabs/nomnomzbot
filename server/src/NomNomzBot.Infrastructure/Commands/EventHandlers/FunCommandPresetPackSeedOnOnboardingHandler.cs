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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Commands.EventHandlers;

/// <summary>
/// Onboarding seed job (Commands domain, S068f — legacy builtins audit): seeds the fun-command preset pack
/// (<c>!8ball</c>, <c>!hug</c>, <c>!slap</c>, <c>!ping</c>, <c>!rps</c>, <c>!compliment</c>) through
/// <see cref="ICommandPresetService.SeedAsync"/>, so a fresh channel isn't a blank slate. The startup backfill
/// re-fires this on every boot; the seed is idempotent, never touches a name the channel already uses, and
/// never brings back a preset the channel deleted. Independently resilient — a failure is logged, never
/// propagated, so it cannot affect the other onboarding seed jobs.
/// </summary>
public sealed class FunCommandPresetPackSeedOnOnboardingHandler(
    ICommandPresetService presets,
    ILogger<FunCommandPresetPackSeedOnOnboardingHandler> logger
) : IEventHandler<ChannelOnboardedEvent>
{
    public async Task HandleAsync(ChannelOnboardedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        try
        {
            Result<CommandPresetSeedReport> result = await presets.SeedAsync(
                @event.BroadcasterId,
                ct
            );
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Onboarding seed (fun command preset pack): seeding failed for {BroadcasterId}: {Error} ({Code})",
                    @event.BroadcasterId,
                    result.ErrorMessage,
                    result.ErrorCode
                );
                return;
            }

            CommandPresetSeedReport report = result.Value;
            foreach (string failure in report.Failed)
                logger.LogWarning(
                    "Onboarding seed (fun command preset pack): a preset could not be created for {BroadcasterId}: {Failure}",
                    @event.BroadcasterId,
                    failure
                );

            logger.LogInformation(
                "Onboarding seed (fun command preset pack): {BroadcasterId} ({Name}) — {Seeded} seeded, {Present} already present, {Deleted} deleted by the channel and left gone",
                @event.BroadcasterId,
                @event.Name,
                report.Seeded,
                report.AlreadyPresent,
                report.DeletedByChannel
            );
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(
                ex,
                "Onboarding seed (fun command preset pack): failed for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
    }
}
