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

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Moderation.Entities;

namespace NomNomzBot.Infrastructure.Moderation;

/// <inheritdoc cref="IViolationEscalationService" />
public sealed class ViolationEscalationService(
    IModerationEscalationService escalation,
    IUserService users,
    IModerationService moderation,
    IActingModeratorResolverService actingModerator,
    ILogger<ViolationEscalationService> logger
) : IViolationEscalationService
{
    /// <summary>Only reached for a timeout step the escalation service left without seconds.</summary>
    private const int DefaultTimeoutSeconds = EscalationPolicyDefaults.DefaultTimeoutSeconds;

    public async Task<ViolationEscalationOutcome> TryEscalateAsync(
        Guid broadcasterId,
        string platformUserId,
        string login,
        string displayName,
        string reason,
        CancellationToken ct = default
    )
    {
        Result<ModerationEscalationPolicyDto> policy = await escalation.GetPolicyAsync(
            broadcasterId,
            ct
        );
        if (
            policy.IsFailure
            || policy.Value is not { IsEnabled: true, CountAutoModViolations: true }
        )
            return ViolationEscalationOutcome.NotHandled;

        // Signed as the channel owner when the channel is onboarded, otherwise as whichever signed-in
        // moderator is live on the channel right now — this is the channel's own automation, so no
        // dashboard user needs to be in the loop, but it must never silently do nothing when the owner
        // has no token on file (Stoney 2026-11-05).
        Result<ActingModerator> actor = await actingModerator.ResolveAsync(broadcasterId, ct);
        if (actor.IsFailure)
            return ViolationEscalationOutcome.NotHandled;
        Guid ownerUserId = actor.Value.OperatorUserId;

        Result<UserDto> user = await users.GetOrCreateAsync(
            platformUserId,
            login,
            displayName,
            cancellationToken: ct
        );
        if (user.IsFailure || !Guid.TryParse(user.Value.Id, out Guid subjectUserId))
            return ViolationEscalationOutcome.NotHandled;

        Result<EscalationDecision> decision = await escalation.ResolveAndRecordAsync(
            broadcasterId,
            subjectUserId,
            platformUserId,
            ct
        );
        if (decision.IsFailure)
            return ViolationEscalationOutcome.NotHandled;

        Result<ModerationActionResult> applied = await ApplyAsync(
            decision.Value,
            broadcasterId,
            ownerUserId,
            platformUserId,
            reason,
            ct
        );
        if (applied.IsFailure)
            logger.LogWarning(
                "Escalation '{Action}' failed for {User} in {BroadcasterId}: {Error}",
                decision.Value.Action,
                platformUserId,
                broadcasterId,
                applied.ErrorMessage
            );

        // The offense is recorded either way, so the ladder owns this violation: the caller must not add
        // a second punishment of its own.
        return new ViolationEscalationOutcome(true, decision.Value.Action, applied.IsSuccess);
    }

    private Task<Result<ModerationActionResult>> ApplyAsync(
        EscalationDecision decision,
        Guid broadcasterId,
        Guid ownerUserId,
        string platformUserId,
        string reason,
        CancellationToken ct
    )
    {
        string channel = broadcasterId.ToString();
        return decision.Action switch
        {
            "ban" => moderation.BanAsync(channel, ownerUserId, platformUserId, reason, null, ct),
            "timeout" => moderation.TimeoutAsync(
                channel,
                ownerUserId,
                platformUserId,
                decision.TimeoutSeconds ?? DefaultTimeoutSeconds,
                reason,
                null,
                ct
            ),
            _ => moderation.WarnUserAsync(channel, ownerUserId, platformUserId, reason, null, ct),
        };
    }
}
