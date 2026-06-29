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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Identity.EventHandlers;

/// <summary>
/// Onboarding seed job (Identity / channel domain): when a channel finishes onboarding, pull its current
/// title, category, language, and tags from the Helix "Get Channel Information" endpoint and persist them
/// to the <c>Channel</c> row. This uses the app token (no streamer scope required) so it always succeeds.
/// Without this, an offline channel's stored title/game stay null until their next <c>stream.online</c> event.
/// Independently resilient — caught + logged, never propagated. Idempotent and safe to re-run.
/// </summary>
public sealed class ChannelInfoSeedOnOnboardingHandler(
    IApplicationDbContext db,
    ITwitchChannelsApi channels,
    ILogger<ChannelInfoSeedOnOnboardingHandler> logger
) : IEventHandler<ChannelOnboardedEvent>
{
    public async Task HandleAsync(ChannelOnboardedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        logger.LogInformation(
            "Onboarding seed (channel info): fetching title/category from Twitch for {BroadcasterId} ({Name})",
            @event.BroadcasterId,
            @event.Name
        );

        try
        {
            Result<TwitchChannelInformation> result = await channels.GetChannelInformationAsync(
                @event.BroadcasterId,
                ct
            );

            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Onboarding seed (channel info): Helix call failed for {BroadcasterId}: {Error} ({Code})",
                    @event.BroadcasterId,
                    result.ErrorMessage,
                    result.ErrorCode
                );
                return;
            }

            Channel? channel = await db.Channels.FindAsync([@event.BroadcasterId], ct);
            if (channel is null)
                return;

            TwitchChannelInformation info = result.Value;
            bool changed = false;

            if (!string.IsNullOrEmpty(info.Title) && channel.Title != info.Title)
            {
                channel.Title = info.Title;
                changed = true;
            }

            if (!string.IsNullOrEmpty(info.GameName) && channel.GameName != info.GameName)
            {
                channel.GameName = info.GameName;
                changed = true;
            }

            if (
                !string.IsNullOrEmpty(info.BroadcasterLanguage)
                && channel.Language != info.BroadcasterLanguage
            )
            {
                channel.Language = info.BroadcasterLanguage;
                changed = true;
            }

            if (info.Tags.Count > 0)
            {
                List<string> incoming = info.Tags.OrderBy(t => t).ToList();
                List<string> existing = channel.Tags.OrderBy(t => t).ToList();
                if (!incoming.SequenceEqual(existing))
                {
                    channel.Tags = [.. info.Tags];
                    changed = true;
                }
            }

            if (changed)
                await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Onboarding seed (channel info): completed for {BroadcasterId} — title={Title}, game={Game}, lang={Lang}",
                @event.BroadcasterId,
                info.Title,
                info.GameName,
                info.BroadcasterLanguage
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Onboarding seed (channel info): failed for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
    }
}
