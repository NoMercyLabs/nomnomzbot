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
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;

namespace NomNomzBot.Infrastructure.Moderation;

/// <inheritdoc cref="IViolationEscalationService" />
public sealed class ViolationEscalationService(
    IApplicationDbContext db,
    IModerationEscalationService escalation,
    IUserService users,
    IModerationService moderation,
    ILogger<ViolationEscalationService> logger
) : IViolationEscalationService
{
    /// <summary>Only reached for a timeout step the escalation service left without seconds.</summary>
    private const int DefaultTimeoutSeconds = 600;

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

        // Issued as the channel owner, like every other automation: no dashboard user is in the loop.
        Guid ownerUserId = await db
            .Channels.Where(c => c.Id == broadcasterId)
            .Select(c => c.OwnerUserId)
            .FirstOrDefaultAsync(ct);
        if (ownerUserId == Guid.Empty)
            return ViolationEscalationOutcome.NotHandled;

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
