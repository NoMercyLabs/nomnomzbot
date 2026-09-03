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
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>What an enforcement attempt actually did, so the caller can log the truth rather than the intent.</summary>
/// <param name="DeletedMessage">The message was removed.</param>
/// <param name="TimedOutAccount">The account was timed out.</param>
/// <param name="Skipped">Why nothing happened, when nothing did.</param>
public sealed record SpamEnforcementOutcome(
    bool DeletedMessage,
    bool TimedOutAccount,
    string? Skipped
);

/// <summary>
/// Turns a <see cref="SpamDecision"/> into real moderation actions (spam-defense.md §L5).
///
/// <para>Separate from <see cref="SpamDefenseService"/> on purpose. Deciding and acting are different
/// responsibilities with different risk: the decision is pure and exhaustively tested, while acting
/// touches somebody's account and depends on tokens, scopes and a platform being reachable. Keeping
/// them apart is what lets the whole engine run in dry run with this type simply never called.</para>
///
/// <para><b>Account actions route through <see cref="IModerationService"/>, never
/// <see cref="ITwitchModerationApi"/> directly.</b> The service emits the domain events that
/// <c>ModerationProjectionService</c> projects into heat, so a spam timeout counts toward the escalation
/// ladder. Calling Helix directly is exactly the defect the older auto-mod handler has: its own bans
/// contribute no heat, so the ladder never sees the offences it acted on.</para>
/// </summary>
public sealed class SpamEnforcementExecutor
{
    /// <summary>Used when the channel has not configured its own automatic timeout length.</summary>
    private const int FallbackTimeoutSeconds = 600;

    private readonly IApplicationDbContext _db;
    private readonly IModerationService _moderation;
    private readonly ITwitchModerationApi _twitch;
    private readonly ILogger<SpamEnforcementExecutor> _logger;

    public SpamEnforcementExecutor(
        IApplicationDbContext db,
        IModerationService moderation,
        ITwitchModerationApi twitch,
        ILogger<SpamEnforcementExecutor> logger
    )
    {
        _db = db;
        _moderation = moderation;
        _twitch = twitch;
        _logger = logger;
    }

    /// <summary>
    /// Carry out a decision. Returns without acting for anything that is not an enforceable outcome —
    /// dry run, a flag, or nothing at all — so the caller never has to remember to check first.
    /// </summary>
    public async Task<SpamEnforcementOutcome> ExecuteAsync(
        Guid broadcasterId,
        string provider,
        string messageId,
        string subjectPlatformUserId,
        SpamDecision decision,
        CancellationToken ct = default
    )
    {
        // Dry run is checked HERE as well as in the decision, not instead of it. The decision already
        // returns None while observing; this is the second lock on the door, because the cost of the two
        // disagreeing is somebody actioned during the week they were told nothing would happen.
        if (decision.IsDryRun)
            return new SpamEnforcementOutcome(false, false, "dry run");

        if (decision.Outcome is SpamOutcome.None or SpamOutcome.Flag)
            return new SpamEnforcementOutcome(false, false, "nothing to enforce");

        // Enforcement rides Helix, so a non-Twitch message is recorded and explained but not acted on.
        // Claiming otherwise would be the dishonest kind of bug: an operator believing the room is
        // covered when it is not.
        if (!string.Equals(provider, AuthEnums.Platform.Twitch, StringComparison.OrdinalIgnoreCase))
            return new SpamEnforcementOutcome(false, false, $"no enforcement path for {provider}");

        bool deleted = await DeleteMessageAsync(broadcasterId, messageId, ct);

        if (decision.Outcome != SpamOutcome.DeleteAndEscalate)
            return new SpamEnforcementOutcome(deleted, false, null);

        bool timedOut = await TimeoutAsync(broadcasterId, subjectPlatformUserId, decision, ct);
        return new SpamEnforcementOutcome(deleted, timedOut, null);
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
                "Spam defence could not delete message {MessageId} in {BroadcasterId}: {Error}",
                messageId,
                broadcasterId,
                result.ErrorMessage
            );

        return result.IsSuccess;
    }

    private async Task<bool> TimeoutAsync(
        Guid broadcasterId,
        string subjectPlatformUserId,
        SpamDecision decision,
        CancellationToken ct
    )
    {
        // Issued as the broadcaster: this is the channel's own automation, not a moderator's personal
        // action, and no dashboard user is in the loop.
        Guid ownerUserId = await _db
            .Channels.Where(c => c.Id == broadcasterId)
            .Select(c => c.OwnerUserId)
            .FirstOrDefaultAsync(ct);

        if (ownerUserId == Guid.Empty)
            return false;

        Result<ModerationActionResult> result = await _moderation.TimeoutAsync(
            broadcasterId.ToString(),
            ownerUserId,
            subjectPlatformUserId,
            await ResolveTimeoutSecondsAsync(broadcasterId, ct),
            // The decision's own explanation, verbatim (SD7). A viewer reading their timeout reason sees
            // what the system saw, not "automated action".
            decision.Reason,
            null,
            ct
        );

        if (result.IsFailure)
            _logger.LogWarning(
                "Spam defence could not time out {User} in {BroadcasterId}: {Error}",
                subjectPlatformUserId,
                broadcasterId,
                result.ErrorMessage
            );

        return result.IsSuccess;
    }

    /// <summary>
    /// Reuses the channel's existing automatic-timeout length rather than adding a second one. An
    /// operator who has already decided how long the bot times people out for should not have to decide
    /// it twice, and a knob nobody asked for is a knob nobody tunes.
    /// </summary>
    private async Task<int> ResolveTimeoutSecondsAsync(Guid broadcasterId, CancellationToken ct)
    {
        Result<AutomodConfigDto> config = await _moderation.GetAutomodConfigAsync(
            broadcasterId.ToString(),
            ct
        );

        return config.IsSuccess && config.Value.HeatTimeoutSeconds > 0
            ? config.Value.HeatTimeoutSeconds
            : FallbackTimeoutSeconds;
    }
}
