// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Coalesces missing-scope chat notices across the same reconnect/onboarding pass (identity-auth §3.4a).
/// <see cref="ScopeNotificationService.NotifyPendingAsync"/> already batches every un-notified gap into ONE chat
/// line — but several proactive jobs (community roster sync, subscriber/VIP standing, banned-user import) can
/// each detect a DIFFERENT missing scope a few milliseconds apart, each via its own <see cref="MissingScopeRecordingHandler"/>
/// invocation. Without this, the first invocation's "batch of everything pending right now" runs BEFORE the
/// second job has recorded its own gap, so the two calls still produce two separate chat messages even though
/// each individual send is, in isolation, a correct one-shot notice. This service is a per-broadcaster sliding
/// debounce: each request resets a short coalesce window, and only the LAST request in a burst survives to
/// actually flush — at which point it re-reads every currently-pending gap (including ones recorded by sibling
/// jobs after this call started), so a whole burst collapses into the one message the batching was always meant
/// to produce.
/// </summary>
public interface IScopeNotificationDebouncer
{
    /// <summary>
    /// Requests a coalesced flush for <paramref name="broadcasterId"/>. Resets the coalesce window if a request
    /// is already pending for that channel. The returned task completes once EITHER this request's own flush ran,
    /// OR it was superseded by a later request within the window (in which case it completes without flushing —
    /// the superseding request owns the eventual send). Safe to fire-and-forget in production; callers that need
    /// to observe the flush (tests, with a fake <see cref="TimeProvider"/>) can await it.
    /// </summary>
    Task RequestFlushAsync(Guid broadcasterId, CancellationToken ct = default);
}

/// <inheritdoc cref="IScopeNotificationDebouncer"/>
public sealed class ScopeNotificationDebouncer(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ScopeNotificationDebouncer> logger
) : IScopeNotificationDebouncer
{
    /// <summary>
    /// How long a channel must go quiet before a coalesced batch actually posts. Long enough that the proactive
    /// jobs a single reconnect/onboarding pass kicks off (roster sync, standing sync, banned-user import) have
    /// all had a chance to record their own gap; short enough that the streamer isn't left waiting noticeably
    /// longer than today's effectively-immediate notice.
    /// </summary>
    public static readonly TimeSpan CoalesceWindow = TimeSpan.FromSeconds(3);

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _pending = new();

    public async Task RequestFlushAsync(Guid broadcasterId, CancellationToken ct = default)
    {
        CancellationTokenSource mine = new();
        _pending.AddOrUpdate(
            broadcasterId,
            mine,
            (_, previous) =>
            {
                // A newer request within the window supersedes the one still waiting — cancel it so only the
                // last request in the burst survives to flush.
                previous.Cancel();
                previous.Dispose();
                return mine;
            }
        );

        try
        {
            await Task.Delay(CoalesceWindow, timeProvider, mine.Token);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a later gap detected within the window — that request owns the eventual flush.
            return;
        }

        // Only remove the entry if it is still THIS request's token — a later request may already have replaced
        // it (and would otherwise have its own cancellation source torn down from under it).
        _pending.TryRemove(new KeyValuePair<Guid, CancellationTokenSource>(broadcasterId, mine));
        mine.Dispose();

        try
        {
            // A fresh scope (fresh DbContext) — the scope that recorded the gap and requested this flush may
            // already be disposed by the time the coalesce window elapses.
            using IServiceScope scope = scopeFactory.CreateScope();
            IScopeNotificationService notifications =
                scope.ServiceProvider.GetRequiredService<IScopeNotificationService>();
            await notifications.NotifyPendingAsync(broadcasterId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Failed to flush the debounced missing-scope notice for channel {BroadcasterId}",
                broadcasterId
            );
        }
    }
}
