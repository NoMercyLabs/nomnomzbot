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
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Moderation.ChatSafety;
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Infrastructure.Moderation.ChatSafety;

/// <summary>
/// Holds each channel's chat safety meters in memory: four sliding 60-second windows, a baseline for
/// each, the open cohorts, lockdown and shield mode. <see cref="Snapshot"/> reads them into a level and
/// the four numbers. A baseline takes one sample a minute, and a minute that was over twice its
/// baseline is not recorded, so a long attack cannot teach the channel that attacks are normal.
/// </summary>
public sealed class ChatSafetyMeter : IChatSafetyMeter
{
    private const int NewAccountMaxAgeDays = 7;
    private const int MaxSeenAccounts = 50_000;
    private const int MinimumCohortAccounts = 2;
    private const double WatchFactor = 2.0;

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<Guid, ChannelState> _channels = new();

    public ChatSafetyMeter(TimeProvider time) => _time = time;

    public static bool IsNewAccount(DateTime? accountCreatedAt, DateTimeOffset now) =>
        accountCreatedAt is { } created
        && (now - new DateTimeOffset(DateTime.SpecifyKind(created, DateTimeKind.Utc))).TotalDays
            < NewAccountMaxAgeDays;

    public void RecordChatLine(Guid channelId) => With(channelId, (s, now) => s.Lines.Add(now));

    public void RecordFollow(Guid channelId) => With(channelId, (s, now) => s.Follows.Add(now));

    public void RecordNewAccount(Guid channelId) =>
        With(channelId, (s, now) => s.NewAccounts.Add(now));

    public void RecordFlaggedLine(Guid channelId) =>
        With(channelId, (s, now) => s.Flagged.Add(now));

    public void SetShieldMode(Guid channelId, bool on) =>
        With(channelId, (s, _) => s.ShieldModeOn = on);

    public void SetLockdown(Guid channelId, bool engaged) =>
        With(channelId, (s, _) => s.LockdownEngaged = engaged);

    public void ObserveCohort(
        Guid channelId,
        string skeleton,
        CohortVerdict verdict,
        int distinctAccounts,
        DateTimeOffset expiresAt
    ) =>
        With(
            channelId,
            (s, _) => s.Cohorts[skeleton] = new CohortMark(verdict, distinctAccounts, expiresAt)
        );

    /// <summary>True the first time this account writes in the stream; false after.</summary>
    public bool MarkFirstLine(Guid channelId, string userId)
    {
        ChannelState state = StateFor(channelId);
        lock (state)
        {
            if (state.SeenAccounts.Count >= MaxSeenAccounts)
                state.SeenAccounts.Clear();

            return state.SeenAccounts.Add(userId);
        }
    }

    /// <summary>A new stream starts: every account may count as new again.</summary>
    public void ResetStream(Guid channelId)
    {
        ChannelState state = StateFor(channelId);
        lock (state)
            state.SeenAccounts.Clear();
    }

    public IReadOnlyList<Guid> ActiveChannels() => [.. _channels.Keys];

    public ChatSafetyStatusDto Snapshot(Guid channelId)
    {
        ChannelState state = StateFor(channelId);
        DateTimeOffset now = _time.GetUtcNow();
        lock (state)
            return state.Read(now);
    }

    private void With(Guid channelId, Action<ChannelState, DateTimeOffset> change)
    {
        ChannelState state = StateFor(channelId);
        DateTimeOffset now = _time.GetUtcNow();
        lock (state)
            change(state, now);
    }

    private ChannelState StateFor(Guid channelId) =>
        _channels.GetOrAdd(channelId, _ => new ChannelState());

    private readonly record struct CohortMark(
        CohortVerdict Verdict,
        int DistinctAccounts,
        DateTimeOffset ExpiresAt
    );

    private sealed class SlidingCount
    {
        private readonly Queue<DateTimeOffset> _stamps = new();

        public void Add(DateTimeOffset at) => _stamps.Enqueue(at);

        public int CountAt(DateTimeOffset now)
        {
            while (_stamps.Count > 0 && now - _stamps.Peek() >= Window)
                _stamps.Dequeue();

            return _stamps.Count;
        }
    }

    private sealed class ChannelState
    {
        private readonly ChatSafetyLevelTracker _level = new();
        private readonly ChannelBaseline _linesBaseline = new();
        private readonly ChannelBaseline _followsBaseline = new();
        private readonly ChannelBaseline _newAccountsBaseline = new();
        private readonly ChannelBaseline _flaggedBaseline = new();
        private DateTimeOffset? _nextSampleAt;

        public SlidingCount Lines { get; } = new();
        public SlidingCount Follows { get; } = new();
        public SlidingCount NewAccounts { get; } = new();
        public SlidingCount Flagged { get; } = new();
        public HashSet<string> SeenAccounts { get; } = [];
        public Dictionary<string, CohortMark> Cohorts { get; } = [];
        public bool ShieldModeOn { get; set; }
        public bool LockdownEngaged { get; set; }

        public ChatSafetyStatusDto Read(DateTimeOffset now)
        {
            int lines = Lines.CountAt(now);
            int follows = Follows.CountAt(now);
            int newAccounts = NewAccounts.CountAt(now);
            int flagged = Flagged.CountAt(now);

            bool overBaseline =
                _linesBaseline.IsSpike(lines, WatchFactor, 20)
                || _followsBaseline.IsSpike(follows, WatchFactor)
                || _newAccountsBaseline.IsSpike(newAccounts, WatchFactor, 3)
                || _flaggedBaseline.IsSpike(flagged, WatchFactor, 3);

            PruneCohorts(now);
            ChatSafetySignals signals = new(
                CampaignOpen: Cohorts.Values.Any(c => c.Verdict == CohortVerdict.Campaign),
                LockdownEngaged: LockdownEngaged,
                ShieldModeOn: ShieldModeOn,
                FollowSpike: _followsBaseline.IsSpike(follows),
                WatchingCohortOpen: Cohorts.Values.Any(c =>
                    c
                        is {
                            Verdict: CohortVerdict.Watching,
                            DistinctAccounts: >= MinimumCohortAccounts
                        }
                ),
                MeterOverBaseline: overBaseline
            );
            ChatSafetyLevel level = _level.Update(signals, now);

            Sample(now, lines, follows, newAccounts, flagged);

            return new ChatSafetyStatusDto(
                level,
                Math.Round(lines / 60.0, 2),
                follows,
                newAccounts,
                flagged
            );
        }

        private void PruneCohorts(DateTimeOffset now)
        {
            List<string> expired =
            [
                .. Cohorts.Where(c => c.Value.ExpiresAt <= now).Select(c => c.Key),
            ];
            foreach (string skeleton in expired)
                Cohorts.Remove(skeleton);
        }

        private void Sample(
            DateTimeOffset now,
            int lines,
            int follows,
            int newAccounts,
            int flagged
        )
        {
            _nextSampleAt ??= now + Window;
            if (now < _nextSampleAt)
                return;

            _nextSampleAt = now + Window;
            RecordUnlessSpike(_linesBaseline, lines, 20);
            RecordUnlessSpike(_followsBaseline, follows, 5);
            RecordUnlessSpike(_newAccountsBaseline, newAccounts, 3);
            RecordUnlessSpike(_flaggedBaseline, flagged, 3);
        }

        private static void RecordUnlessSpike(ChannelBaseline baseline, int count, double floor)
        {
            if (!baseline.IsSpike(count, WatchFactor, floor))
                baseline.Record(count);
        }
    }
}
