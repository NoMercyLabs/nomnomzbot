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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Enums;
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// Carries out a newcomer-gate verdict (<see cref="AccountAgeGate"/>): the message is removed, and when
/// the channel chose to hold rather than remove, it also lands in the moderation queue where a moderator
/// reads it and decides.
///
/// <para>Never touches the account. The gate measures how new a viewer is, not what they did, so it has
/// no business timing anyone out — and it stays out of the escalation heat for the same reason.</para>
/// </summary>
public sealed class AccountAgeGateExecutor
{
    private readonly ITwitchModerationApi _twitch;
    private readonly IModerationQueueService _queue;
    private readonly ILogger<AccountAgeGateExecutor> _logger;

    public AccountAgeGateExecutor(
        ITwitchModerationApi twitch,
        IModerationQueueService queue,
        ILogger<AccountAgeGateExecutor> logger
    )
    {
        _twitch = twitch;
        _queue = queue;
        _logger = logger;
    }

    public async Task<SpamEnforcementOutcome> ExecuteAsync(
        Guid broadcasterId,
        string provider,
        string messageId,
        string userId,
        string login,
        string message,
        SpamDecision decision,
        AccountAgeGateVerdict verdict,
        CancellationToken ct = default
    )
    {
        // The second lock on the door, as in SpamEnforcementExecutor: a decision that disagrees with
        // dry run must not act.
        if (decision.IsDryRun)
            return new SpamEnforcementOutcome(false, false, "dry run");

        // Removal rides Helix, so a message from another platform is recorded but not acted on.
        if (!string.Equals(provider, AuthEnums.Platform.Twitch, StringComparison.OrdinalIgnoreCase))
            return new SpamEnforcementOutcome(false, false, $"no enforcement path for {provider}");

        bool deleted = await DeleteMessageAsync(broadcasterId, messageId, ct);

        // Queue only what was really removed: a held row for a message still on screen would offer the
        // moderator an approval that changes nothing.
        if (deleted && verdict.HoldsForReview)
            await QueueForReviewAsync(
                broadcasterId,
                messageId,
                userId,
                login,
                message,
                verdict,
                ct
            );

        return new SpamEnforcementOutcome(deleted, false, null);
    }

    private async Task<bool> DeleteMessageAsync(
        Guid broadcasterId,
        string messageId,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(messageId))
            return false;

        Result result = await _twitch.DeleteChatMessageAsync(broadcasterId, messageId, ct);
        if (result.IsFailure)
            _logger.LogWarning(
                "Newcomer gate could not delete message {MessageId} in {BroadcasterId}: {Error}",
                messageId,
                broadcasterId,
                result.ErrorMessage
            );

        return result.IsSuccess;
    }

    private async Task QueueForReviewAsync(
        Guid broadcasterId,
        string messageId,
        string userId,
        string login,
        string message,
        AccountAgeGateVerdict verdict,
        CancellationToken ct
    )
    {
        Result<Guid> queued = await _queue.EnqueueHeldMessageAsync(
            broadcasterId,
            messageId,
            userId,
            login,
            message,
            verdict.Kind.ToString(),
            ct,
            ModerationQueueSource.AccountAgeGate
        );

        if (queued.IsFailure)
            _logger.LogWarning(
                "Newcomer gate could not queue message {MessageId} in {BroadcasterId} for review: {Error}",
                messageId,
                broadcasterId,
                queued.ErrorMessage
            );
    }
}
