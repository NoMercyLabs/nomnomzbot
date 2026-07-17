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

namespace NomNomzBot.Infrastructure.CustomEvents;

/// <summary>
/// A process-lifetime record of the last <em>attempt</em> (success OR failure) per poll source, keyed by source id.
/// The poll service is scoped — a fresh instance per ~5 s scan tick — so it cannot itself remember that it just
/// attempted a source; this singleton carries that state across ticks. It lets "due" be gated by the source's
/// <c>PollIntervalSeconds</c> regardless of outcome, so a persistently-failing source (whose <c>LastReceivedAt</c>
/// never advances) is retried on its interval rather than every scan tick.
/// </summary>
public interface ICustomDataPollAttemptTracker
{
    /// <summary>The instant this source was last attempted, or <c>null</c> if it has not been attempted this process.</summary>
    DateTimeOffset? LastAttempt(Guid sourceId);

    /// <summary>Stamps the attempt instant for a source — called on every attempt, success or fail.</summary>
    void RecordAttempt(Guid sourceId, DateTimeOffset attemptedAt);
}

/// <summary>
/// In-memory <see cref="ICustomDataPollAttemptTracker"/> backed by a <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// Registered as a SINGLETON so the last-attempt record survives the scoped poll service's per-tick lifetime.
/// No persistence: a restart simply forgets the timings and the first post-restart scan re-attempts every source,
/// which is the correct behaviour (a bounded retry, not a hot loop).
/// </summary>
internal sealed class CustomDataPollAttemptTracker : ICustomDataPollAttemptTracker
{
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastAttempts = new();

    public DateTimeOffset? LastAttempt(Guid sourceId) =>
        _lastAttempts.TryGetValue(sourceId, out DateTimeOffset attemptedAt) ? attemptedAt : null;

    public void RecordAttempt(Guid sourceId, DateTimeOffset attemptedAt) =>
        _lastAttempts[sourceId] = attemptedAt;
}
