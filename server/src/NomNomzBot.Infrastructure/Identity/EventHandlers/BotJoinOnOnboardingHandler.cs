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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
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
/// surface as an ordinary failure here, logged as a warning — idempotent and safe to re-run, since this
/// handler writes nothing locally (the Helix call is its only side effect). Independently resilient — caught +
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
    NomNomzBot.Application.Contracts.Security.IOutboundSanctionAccessor sanctions,
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
    private bool IsConfiguredSaasBot(BotAccount bot)
    {
        string? expectedUserId = configuration["Twitch:BotUserId"];
        if (!string.IsNullOrWhiteSpace(expectedUserId))
            return string.Equals(bot.BotUserId, expectedUserId.Trim(), StringComparison.Ordinal);

        string? expectedUsername = configuration["Twitch:BotUsername"];
        if (!string.IsNullOrWhiteSpace(expectedUsername))
            return string.Equals(
                bot.BotUsername,
                expectedUsername.Trim(),
                StringComparison.OrdinalIgnoreCase
            );

        return false;
    }

    public async Task HandleAsync(ChannelOnboardedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        try
        {
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
                logger.LogError(
                    "Onboarding seed (bot join): REFUSED to grant moderator in {BroadcasterId} — the registered "
                        + "shared bot {BotUsername} ({BotUserId}) is not this deployment's configured SaaS bot. "
                        + "Granting a moderator role to an unexpected account is exactly the 2026-09-04 incident.",
                    @event.BroadcasterId,
                    sharedBot.BotUsername,
                    sharedBot.BotUserId
                );
                return;
            }

            using IDisposable sanction = sanctions.Begin(
                NomNomzBot.Application.Contracts.Security.OutboundSanction.PlatformConfiguration(
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
