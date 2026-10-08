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
using NomNomzBot.Application.Chat.Services;
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
    private readonly IInboundOriginModerator _origin;
    private readonly IViolationEscalationService _escalation;
    private readonly ILogger<SpamEnforcementExecutor> _logger;

    public SpamEnforcementExecutor(
        IApplicationDbContext db,
        IModerationService moderation,
        ITwitchModerationApi twitch,
        IInboundOriginModerator origin,
        IViolationEscalationService escalation,
        ILogger<SpamEnforcementExecutor> logger
    )
    {
        _db = db;
        _moderation = moderation;
        _twitch = twitch;
        _origin = origin;
        _escalation = escalation;
        _logger = logger;
    }

    private static bool IsTwitch(string provider) =>
        string.Equals(provider, AuthEnums.Platform.Twitch, StringComparison.OrdinalIgnoreCase);

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
        CancellationToken ct = default,
        string? subjectLogin = null,
        string? subjectDisplayName = null
    )
    {
        // Dry run is checked HERE as well as in the decision, not instead of it. The decision already
        // returns None while observing; this is the second lock on the door, because the cost of the two
        // disagreeing is somebody actioned during the week they were told nothing would happen.
        if (decision.IsDryRun)
            return new SpamEnforcementOutcome(false, false, "dry run");

        if (decision.Outcome is SpamOutcome.None or SpamOutcome.Flag)
            return new SpamEnforcementOutcome(false, false, "nothing to enforce");

        // A message acts on the platform it came from. Twitch rides Helix and the escalation ladder; any
        // other platform goes through the inbound-origin seam, which answers honestly when it cannot act.
        if (!IsTwitch(provider))
            return await ExecuteViaOriginAsync(
                broadcasterId,
                provider,
                messageId,
                subjectPlatformUserId,
                decision,
                ct
            );

        bool deleted = await DeleteMessageAsync(broadcasterId, messageId, ct);

        if (decision.Outcome != SpamOutcome.DeleteAndEscalate)
            return new SpamEnforcementOutcome(deleted, false, null);

        // A channel that counts AutoMod violations as ladder offenses lets the ladder pick the punishment
        // INSTEAD of the heat timeout below, so the account is never actioned twice for one message.
        ViolationEscalationOutcome escalated = await _escalation.TryEscalateAsync(
            broadcasterId,
            subjectPlatformUserId,
            subjectLogin ?? subjectPlatformUserId,
            subjectDisplayName ?? subjectLogin ?? subjectPlatformUserId,
            decision.Reason,
            ct
        );
        if (escalated.Handled)
            return new SpamEnforcementOutcome(
                deleted,
                escalated is { Applied: true, Action: "timeout" or "ban" },
                null
            );

        bool timedOut = await TimeoutAsync(broadcasterId, subjectPlatformUserId, decision, ct);
        return new SpamEnforcementOutcome(deleted, timedOut, null);
    }

    /// <summary>
    /// The non-Twitch path. Deletes, and for <see cref="SpamOutcome.DeleteAndEscalate"/> times out, on the
    /// platform the message came from. The Twitch escalation ladder is keyed to Twitch accounts, so it is
    /// not run here and the outcome says so. A refusal or an unsupported platform is carried in
    /// <see cref="SpamEnforcementOutcome.Skipped"/>; an action that did not happen is never claimed.
    /// </summary>
    private async Task<SpamEnforcementOutcome> ExecuteViaOriginAsync(
        Guid broadcasterId,
        string provider,
        string messageId,
        string subjectPlatformUserId,
        SpamDecision decision,
        CancellationToken ct
    )
    {
        List<string> notes = [];

        bool deleted = false;
        if (string.IsNullOrWhiteSpace(messageId))
            notes.Add("no message id to delete");
        else
            deleted = Succeeded(
                await _origin.DeleteMessageAsync(broadcasterId, provider, messageId, ct),
                $"delete message {messageId}",
                provider,
                broadcasterId,
                notes
            );

        if (decision.Outcome != SpamOutcome.DeleteAndEscalate)
            return OriginOutcome(deleted, false, notes);

        notes.Add(
            $"the escalation ladder is keyed to Twitch accounts, so it was not applied to this {provider} account"
        );

        bool timedOut = Succeeded(
            await _origin.TimeoutUserAsync(
                broadcasterId,
                provider,
                subjectPlatformUserId,
                await ResolveTimeoutSecondsAsync(broadcasterId, ct),
                decision.Reason,
                ct
            ),
            $"time out {subjectPlatformUserId}",
            provider,
            broadcasterId,
            notes
        );

        return OriginOutcome(deleted, timedOut, notes);
    }

    private bool Succeeded(
        InboundModerationOutcome outcome,
        string action,
        string provider,
        Guid broadcasterId,
        List<string> notes
    )
    {
        if (outcome.Status == InboundModerationStatus.Done)
            return true;

        _logger.LogWarning(
            "Spam defence could not {Action} on {Provider} in {BroadcasterId}: {Status} {Reason}",
            action,
            provider,
            broadcasterId,
            outcome.Status,
            outcome.Reason
        );
        if (outcome.Reason is not null && !notes.Contains(outcome.Reason))
            notes.Add(outcome.Reason);
        return false;
    }

    private static SpamEnforcementOutcome OriginOutcome(
        bool deleted,
        bool timedOut,
        List<string> notes
    ) => new(deleted, timedOut, notes.Count == 0 ? null : string.Join("; ", notes));

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

        return config is { IsSuccess: true, Value.HeatTimeoutSeconds: > 0 }
            ? config.Value.HeatTimeoutSeconds
            : FallbackTimeoutSeconds;
    }
}
