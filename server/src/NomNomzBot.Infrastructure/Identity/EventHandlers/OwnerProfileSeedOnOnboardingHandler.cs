// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Identity.Jobs;

namespace NomNomzBot.Infrastructure.Identity.EventHandlers;

/// <summary>
/// Onboarding seed job (Identity / user domain): when a channel finishes onboarding, enrich the owner's
/// <c>User</c> row with broadcaster type (partner / affiliate / ""), bio description, and profile image URL
/// from the Helix "Get Users" endpoint (app token — no scope required). Idempotent: only saves when at least
/// one value differs. Independently resilient — caught + logged, never propagated.
/// </summary>
public sealed class OwnerProfileSeedOnOnboardingHandler(
    IApplicationDbContext db,
    ITwitchUsersApi users,
    ILogger<OwnerProfileSeedOnOnboardingHandler> logger
) : IEventHandler<ChannelOnboardedEvent>
{
    public async Task HandleAsync(ChannelOnboardedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        logger.LogInformation(
            "Onboarding seed (owner profile): fetching broadcaster type / description / avatar for {BroadcasterId} ({Name})",
            @event.BroadcasterId,
            @event.Name
        );

        try
        {
            Channel? channel = await db
                .Channels.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == @event.BroadcasterId, ct);
            if (channel is null)
                return;

            Result<IReadOnlyList<TwitchUser>> result = await users.GetUsersByIdsAsync(
                [channel.TwitchChannelId!],
                ct
            );

            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Onboarding seed (owner profile): Helix users call failed for {BroadcasterId}: {Error} ({Code})",
                    @event.BroadcasterId,
                    result.ErrorMessage,
                    result.ErrorCode
                );
                return;
            }

            TwitchUser? twitchUser = result.Value.Count > 0 ? result.Value[0] : null;
            if (twitchUser is null)
                return;

            User? owner = await db.Users.FindAsync([@event.OwnerUserId], ct);
            if (owner is null)
                return;

            // The same rules as the hydration worker: a lost affiliate status clears to "".
            bool changed = UserProfileHydrationService.ApplyProfile(owner, twitchUser);

            if (changed)
                await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Onboarding seed (owner profile): completed for {BroadcasterId} — type={BroadcasterType}",
                @event.BroadcasterId,
                twitchUser.BroadcasterType
            );
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(
                ex,
                "Onboarding seed (owner profile): failed for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
    }
}
