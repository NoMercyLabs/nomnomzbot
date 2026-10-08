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
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>One follow as the tracker sees it.</summary>
public sealed record FollowObservation(
    string UserId,
    string Login,
    string DisplayName,
    DateTimeOffset FollowedAt
);

/// <summary>
/// The follows of one spike that still need examining. <see cref="BatchId"/> is the same for every
/// follow of one spike, so a sweep that continues into later follows stays one reversible batch.
/// </summary>
public sealed record FollowSpikeWindow(
    Guid BatchId,
    IReadOnlyList<FollowObservation> Unexamined,
    int WindowSize
);

/// <summary>
/// Keeps each channel's own follows-per-minute baseline and says when the current minute is a spike
/// (spam-defense.md §L3). The spike only selects a window to look at; it never selects an account.
///
/// <para>A minute that was a spike is not recorded into the baseline, so a long attack cannot teach the
/// channel that attacks are normal. Quiet minutes between follows are recorded as zero, so the baseline
/// is a true rate rather than the rate of busy minutes only.</para>
/// </summary>
public sealed class FollowSpikeTracker
{
    private readonly ConcurrentDictionary<Guid, ChannelState> _channels = new();

    public FollowSpikeWindow? Observe(
        Guid broadcasterId,
        FollowObservation follow,
        double spikeFactor
    )
    {
        ChannelState state = _channels.GetOrAdd(broadcasterId, _ => new ChannelState());
        lock (state)
            return state.Observe(follow, spikeFactor);
    }

    private sealed class ChannelState
    {
        private readonly ChannelBaseline _baseline = new();
        private readonly List<FollowObservation> _minute = [];
        private readonly HashSet<string> _seen = [];
        private DateTimeOffset _minuteStart = DateTimeOffset.MinValue;
        private Guid? _batchId;
        private int _examined;

        public FollowSpikeWindow? Observe(FollowObservation follow, double spikeFactor)
        {
            DateTimeOffset minute = new(
                follow.FollowedAt.Year,
                follow.FollowedAt.Month,
                follow.FollowedAt.Day,
                follow.FollowedAt.Hour,
                follow.FollowedAt.Minute,
                0,
                TimeSpan.Zero
            );
            if (minute > _minuteStart)
                CloseMinute(minute);

            if (!_seen.Add(follow.UserId))
                return null;
            _minute.Add(follow);

            if (!_baseline.IsSpike(_minute.Count, spikeFactor))
                return null;

            _batchId ??= Guid.CreateVersion7();
            List<FollowObservation> unexamined = [.. _minute.Skip(_examined)];
            _examined = _minute.Count;
            return new FollowSpikeWindow(_batchId.Value, unexamined, _minute.Count);
        }

        private void CloseMinute(DateTimeOffset next)
        {
            if (_minuteStart != DateTimeOffset.MinValue)
            {
                if (_batchId is null)
                    _baseline.Record(_minute.Count);

                int quiet = (int)Math.Min(60, (next - _minuteStart).TotalMinutes - 1);
                for (int i = 0; i < quiet; i++)
                    _baseline.Record(0);
            }

            _minute.Clear();
            _seen.Clear();
            _batchId = null;
            _examined = 0;
            _minuteStart = next;
        }
    }
}
