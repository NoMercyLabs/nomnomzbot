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
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Twitch.Events;

namespace NomNomzBot.Infrastructure.Identity.EventHandlers;

/// <summary>
/// The reactive half of missing-scope handling (identity-auth §3.4a): every <see cref="TwitchHelixReauthRequiredEvent"/>
/// raised with <c>Reason = "missing_scope"</c> — from the proactive per-method pre-check OR a real Helix 403 — is
/// recorded as a channel scope gap and then announced once in chat. Idempotent end-to-end: the recording upserts a
/// single <c>(channel, scope)</c> row and the notice stamps it, so a feature that keeps failing on the same missing
/// scope produces exactly one row and one chat message until a re-grant resolves it. Independently resilient —
/// caught + logged, never propagated (the event bus already isolates handlers, but the chat path must not surface
/// errors into the read flow that emitted the event).
/// </summary>
public sealed class MissingScopeRecordingHandler(
    IScopeNotificationService scopeNotifications,
    ILogger<MissingScopeRecordingHandler> logger
) : IEventHandler<TwitchHelixReauthRequiredEvent>
{
    public async Task HandleAsync(
        TwitchHelixReauthRequiredEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        // Only the missing-scope reason carries an actionable scope; unauthorized/token_revoked are a different
        // (re-login) concern owned by the connection-status path, not the per-scope grant surface.
        if (
            @event.Reason != "missing_scope"
            || string.IsNullOrWhiteSpace(@event.MissingScope)
            || @event.BroadcasterId == Guid.Empty
        )
            return;

        try
        {
            Result<bool> recorded = await scopeNotifications.RecordMissingScopeAsync(
                @event.BroadcasterId,
                @event.MissingScope,
                feature: null,
                cancellationToken
            );
            if (recorded.IsFailure)
            {
                logger.LogWarning(
                    "Recording missing scope '{Scope}' for {BroadcasterId} failed: {Error}",
                    @event.MissingScope,
                    @event.BroadcasterId,
                    recorded.ErrorMessage
                );
                return;
            }

            // Announce any un-notified gap once (covers this one + any earlier deferred when the bot was offline).
            await scopeNotifications.NotifyPendingAsync(@event.BroadcasterId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Failed to handle missing scope '{Scope}' for {BroadcasterId}",
                @event.MissingScope,
                @event.BroadcasterId
            );
        }
    }
}
