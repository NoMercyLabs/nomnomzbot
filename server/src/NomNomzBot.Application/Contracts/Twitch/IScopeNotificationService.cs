// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Twitch;

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// The general "a required scope is missing" mechanism (identity-auth §3.4a): detect a scope the channel's
/// streamer token lacks (proactively from the granted set, reactively from a real Helix <c>missing_scope</c>
/// failure), surface it (dashboard + one chat notice), and drive the additive one-click re-grant. Any scope,
/// any feature — derived from the offered-feature → scope registry, never a hand-listed subset.
/// </summary>
public interface IScopeNotificationService
{
    /// <summary>
    /// Record that a Helix call surfaced a <c>missing_scope</c> for <paramref name="scope"/> on
    /// <paramref name="broadcasterId"/>'s streamer token. Idempotent — re-detecting an already-recorded gap is a
    /// no-op (never a duplicate row, never a second chat notice). A no-op when the connection actually holds the
    /// scope (a stale failure) so detection can't get stuck. <paramref name="feature"/> is the blocked feature
    /// key when known. Returns whether a new gap was recorded (true the first time a scope is seen missing).
    /// </summary>
    Task<Result<bool>> RecordMissingScopeAsync(
        Guid broadcasterId,
        string scope,
        string? feature,
        CancellationToken ct = default
    );

    /// <summary>
    /// The channel's outstanding scope gaps: the union of the proactive feature-gated gaps (an offered feature's
    /// required scope the connection never held) and the reactive runtime-detected gaps, grouped per scope with
    /// the feature(s) each blocks. Empty when the connection holds every offered feature's scopes. <c>NOT_FOUND</c>
    /// when the channel has no Twitch connection.
    /// </summary>
    Task<Result<MissingScopesDto>> GetMissingScopesAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Post the one-time chat notice for any recorded gap that has not yet been announced, then stamp it notified
    /// so it never repeats for the same gap. Best-effort: if the bot cannot post (not connected / send fails) the
    /// gap stays un-notified for a later retry and the dashboard still shows it. Returns the count of scopes newly
    /// announced. Driven off the recording path so a freshly detected gap is announced once, promptly.
    /// </summary>
    Task<Result<int>> NotifyPendingAsync(Guid broadcasterId, CancellationToken ct = default);

    /// <summary>
    /// Drop any recorded gap whose scope is now present in <paramref name="grantedScopes"/> — the re-grant
    /// resolved it, so the banner clears and a future loss of the same scope is announced afresh. Called from the
    /// scope reconciliation that runs on every token store/refresh. Returns the scopes cleared.
    /// </summary>
    Task<Result<IReadOnlyList<string>>> ClearResolvedAsync(
        Guid broadcasterId,
        IReadOnlyCollection<string> grantedScopes,
        CancellationToken ct = default
    );

    /// <summary>
    /// The additive scope set the one-click re-grant must request: <c>currently-granted ∪ every-missing-scope</c>,
    /// so the operator re-consents to the full set and the existing grant is never downgraded. <c>NOT_FOUND</c>
    /// when the channel has no Twitch connection; a failure with no missing scopes is reported so the caller can
    /// surface "nothing to grant" rather than launch a pointless OAuth.
    /// </summary>
    Task<Result<IReadOnlyList<string>>> BuildRegrantScopeSetAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );
}
