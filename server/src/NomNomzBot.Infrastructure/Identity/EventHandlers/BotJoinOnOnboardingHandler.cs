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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Identity.EventHandlers;

/// <summary>
/// Onboarding seed job (Identity / bot domain): when a channel finishes onboarding, grants the shared platform
/// bot moderator status on the new channel via Helix Add Channel Moderator, so the SaaS shared-bot identity can
/// moderate (timeout/ban/delete) from the moment it joins. A no-op when no shared <see cref="BotAccount"/> is
/// registered yet — the self-host default, where the streamer's own account IS the bot identity until a
/// dedicated bot is connected. Requires <c>channel:manage:moderators</c>, scope-gated inside
/// <see cref="ITwitchModeratorsApi.AddModeratorAsync"/>; a missing scope or an already-moderator bot both
/// surface as an ordinary failure here, logged as a warning — idempotent and safe to re-run, since the
/// Helix call is its only outward side effect (locally it only clears the "bot removed by the streamer" marker). Independently resilient — caught +
/// logged, never propagated, so it cannot affect the other onboarding seed jobs.
///
/// <para>
/// <b>Only the configured SaaS bot is ever granted.</b> On 2026-09-04 a personal Twitch account was
/// registered as the shared bot for ninety seconds, and the grant path handed it moderator on other
/// people's channels. Granting a moderator role is the broadcaster's decision, so the one identity this
/// deployment may auto-grant is pinned in configuration (<c>Twitch:BotUserId</c>, else
/// <c>Twitch:BotUsername</c>) and anything else is refused and logged. Prefer pinning the id: this very bot
/// was renamed from <c>nomercybot_</c> to <c>nomz_bot</c> while keeping user id 1335549269, and a name-only
/// gate silently stops granting the moment that happens.
/// </para>
/// </summary>
public sealed class BotJoinOnOnboardingHandler(
    IApplicationDbContext db,
    ITwitchModeratorsApi moderators,
    IConfiguration configuration,
    IOutboundSanctionAccessor sanctions,
    ILogger<BotJoinOnOnboardingHandler> logger
) : IEventHandler<ChannelOnboardedEvent>
{
    /// <summary>
    /// True only for the one bot identity this deployment is configured to auto-grant. An id pin wins because
    /// it survives a Twitch rename; the username is the fallback for deployments that never pinned one.
    /// With NEITHER configured nothing is auto-granted — a deployment that has not named its bot has not
    /// consented to hand anyone moderator, and defaulting to "grant whatever is registered" is the behaviour
    /// that caused the incident.
    /// </summary>
    private bool IsConfiguredSaasBot(BotAccount bot) =>
        SaasBotPin.IsConfigured(configuration, bot.BotUserId, bot.BotUsername);

    /// <summary>
    /// An id-pin mismatch is a different account and stays an Error. A username-pin mismatch is usually a Twitch
    /// rename the deployment config never caught up with, so it is a Warning that names the fix.
    /// </summary>
    private void LogRefusal(Guid broadcasterId, BotAccount bot)
    {
        string? expectedUserId = configuration["Twitch:BotUserId"];
        if (!string.IsNullOrWhiteSpace(expectedUserId))
        {
            logger.LogError(
                "Onboarding seed (bot join): REFUSED to grant moderator in {BroadcasterId} — the registered "
                    + "shared bot {BotUsername} ({BotUserId}) is not the configured SaaS bot (Twitch:BotUserId = "
                    + "{ExpectedUserId}). Granting a moderator role to an unexpected account is exactly the "
                    + "2026-09-04 incident.",
                broadcasterId,
                bot.BotUsername,
                bot.BotUserId,
                expectedUserId.Trim()
            );
            return;
        }

        string? expectedUsername = configuration["Twitch:BotUsername"];
        if (!string.IsNullOrWhiteSpace(expectedUsername))
        {
            logger.LogWarning(
                "Onboarding seed (bot join): not granting moderator in {BroadcasterId} — the registered shared "
                    + "bot is {BotUsername} ({BotUserId}) but Twitch:BotUsername is {ExpectedUsername}. If the bot "
                    + "was renamed on Twitch, pin it by id instead: set Twitch:BotUserId (TWITCH_BOT_USER_ID) to "
                    + "{BotUserId}.",
                broadcasterId,
                bot.BotUsername,
                bot.BotUserId,
                expectedUsername.Trim(),
                bot.BotUserId
            );
            return;
        }

        logger.LogWarning(
            "Onboarding seed (bot join): not granting moderator in {BroadcasterId} — no SaaS bot is pinned. Set "
                + "Twitch:BotUserId (TWITCH_BOT_USER_ID) to {BotUserId} to auto-grant {BotUsername}.",
            broadcasterId,
            bot.BotUserId,
            bot.BotUsername
        );
    }

    /// <summary>
    /// Running onboarding again is the streamer asking for the bot, so an earlier "removed on purpose" marker no
    /// longer holds the moderator self-heal back.
    /// </summary>
    private async Task ClearRemovalMarkerAsync(Guid broadcasterId, CancellationToken ct)
    {
        Channel? channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == broadcasterId, ct);
        if (channel?.BotModeratorRemovedAt is null)
            return;

        channel.BotModeratorRemovedAt = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task HandleAsync(ChannelOnboardedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        try
        {
            await ClearRemovalMarkerAsync(@event.BroadcasterId, ct);

            BotAccount? sharedBot = await db
                .BotAccounts.AsNoTracking()
                .FirstOrDefaultAsync(
                    b =>
                        b.IdentityType == AuthEnums.BotIdentityType.Shared
                        && b.IsActive
                        && b.DeletedAt == null,
                    ct
                );

            if (sharedBot is null)
            {
                logger.LogInformation(
                    "Onboarding seed (bot join): no shared platform bot registered — skipping mod-grant for {BroadcasterId}",
                    @event.BroadcasterId
                );
                return;
            }

            if (!IsConfiguredSaasBot(sharedBot))
            {
                LogRefusal(@event.BroadcasterId, sharedBot);
                return;
            }

            using IDisposable sanction = sanctions.Begin(
                OutboundSanction.PlatformConfiguration(
                    $"saas_bot_moderator_grant:{sharedBot.BotUsername}"
                )
            );

            Result modResult = await moderators.AddModeratorAsync(
                @event.BroadcasterId,
                sharedBot.BotUserId,
                ct
            );

            if (modResult.IsSuccess)
                logger.LogInformation(
                    "Onboarding seed (bot join): granted moderator status to the shared bot {BotUsername} in {BroadcasterId}",
                    sharedBot.BotUsername,
                    @event.BroadcasterId
                );
            else
                logger.LogWarning(
                    "Onboarding seed (bot join): could not grant moderator status to the shared bot in {BroadcasterId}: {Error} ({Code})",
                    @event.BroadcasterId,
                    modResult.ErrorMessage,
                    modResult.ErrorCode
                );
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(
                ex,
                "Onboarding seed (bot join): failed for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
    }
}
