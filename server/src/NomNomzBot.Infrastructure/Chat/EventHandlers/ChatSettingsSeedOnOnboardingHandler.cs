// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Chat.EventHandlers;

/// <summary>
/// Onboarding seed job (Chat domain): when a channel finishes onboarding, pulls its current Helix chat-room
/// configuration (slow / follower / subscriber / emote mode) and persists it to the <c>chat.settings</c>
/// <c>Configuration</c> row — the exact store <c>ChatController.GetSettings</c> reads and
/// <c>ChatController.UpdateSettings</c>/<c>PatchSettings</c> write. Without this, a freshly onboarded
/// channel's settings page shows the all-defaults fallback until the streamer opens the dashboard's own chat
/// settings panel once. Get Chat Settings needs only a valid user token — no additional scope — so a failure
/// here means the channel has no usable token yet rather than a missing grant; logged as a warning either way
/// and never propagated. Idempotent: an existing <c>chat.settings</c> row (e.g. already customized via the
/// dashboard before a re-onboard/backfill pass) is left untouched, never overwritten.
/// </summary>
public sealed class ChatSettingsSeedOnOnboardingHandler(
    IApplicationDbContext db,
    ITwitchChatApi chatApi,
    ILogger<ChatSettingsSeedOnOnboardingHandler> logger
) : IEventHandler<ChannelOnboardedEvent>
{
    private const string ConfigKey = "chat.settings";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task HandleAsync(ChannelOnboardedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        logger.LogInformation(
            "Onboarding seed (chat settings): fetching chat-room configuration from Twitch for {BroadcasterId} ({Name})",
            @event.BroadcasterId,
            @event.Name
        );

        try
        {
            bool alreadySeeded = await db.Configurations.AnyAsync(
                c => c.BroadcasterId == @event.BroadcasterId && c.Key == ConfigKey,
                ct
            );

            if (alreadySeeded)
            {
                logger.LogInformation(
                    "Onboarding seed (chat settings): {BroadcasterId} already has a chat.settings row — skipping",
                    @event.BroadcasterId
                );
                return;
            }

            Result<TwitchChatSettings> result = await chatApi.GetChatSettingsAsync(
                @event.BroadcasterId,
                ct
            );

            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Onboarding seed (chat settings): Helix call failed for {BroadcasterId}: {Error} ({Code})",
                    @event.BroadcasterId,
                    result.ErrorMessage,
                    result.ErrorCode
                );
                return;
            }

            TwitchChatSettings settings = result.Value;

            // Mirrors ChatController.ChatSettingsDto's shape verbatim (an Api-layer type Infrastructure cannot
            // reference) — keep the two in lockstep if that DTO's fields ever change.
            string json = JsonSerializer.Serialize(
                new
                {
                    slowMode = settings.SlowMode,
                    slowModeDelay = settings.SlowModeWaitTime ?? 0,
                    subscriberOnly = settings.SubscriberMode,
                    emotesOnly = settings.EmoteMode,
                    followersOnly = settings.FollowerMode,
                    followersOnlyDuration = settings.FollowerModeDuration ?? 0,
                },
                JsonOptions
            );

            db.Configurations.Add(
                new Configuration
                {
                    BroadcasterId = @event.BroadcasterId,
                    Key = ConfigKey,
                    Value = json,
                }
            );

            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Onboarding seed (chat settings): completed for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Onboarding seed (chat settings): failed for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
    }
}
