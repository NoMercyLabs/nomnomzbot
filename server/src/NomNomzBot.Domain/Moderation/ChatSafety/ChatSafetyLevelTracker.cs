// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Moderation.ChatSafety;

/// <summary>What the channel shows right now, as the level rules read it.</summary>
/// <param name="CampaignOpen">A Campaign cohort is open.</param>
/// <param name="LockdownEngaged">A lockdown window is engaged.</param>
/// <param name="ShieldModeOn">Twitch shield mode is on.</param>
/// <param name="FollowSpike">Follows a minute are a spike over the channel baseline.</param>
/// <param name="WatchingCohortOpen">A Watching cohort is open.</param>
/// <param name="MeterOverBaseline">Any meter is over twice its baseline.</param>
public sealed record ChatSafetySignals(
    bool CampaignOpen,
    bool LockdownEngaged,
    bool ShieldModeOn,
    bool FollowSpike,
    bool WatchingCohortOpen,
    bool MeterOverBaseline
);

/// <summary>
/// Turns the signals of one channel into a <see cref="ChatSafetyLevel"/>. Holds the one thing that needs
/// memory: <see cref="ChatSafetyLevel.Over"/> lasts for <see cref="OverDuration"/> after a raid ends.
/// </summary>
public sealed class ChatSafetyLevelTracker
{
    public static readonly TimeSpan OverDuration = TimeSpan.FromMinutes(5);

    private DateTimeOffset? _overUntil;
    private bool _wasRaid;

    public ChatSafetyLevel Update(ChatSafetySignals signals, DateTimeOffset now)
    {
        bool raid =
            signals.CampaignOpen
            || signals.LockdownEngaged
            || signals.ShieldModeOn
            || signals.FollowSpike;

        if (raid)
        {
            _wasRaid = true;
            _overUntil = null;
            return ChatSafetyLevel.Raid;
        }

        if (_wasRaid)
        {
            _wasRaid = false;
            _overUntil = now + OverDuration;
        }

        if (_overUntil is { } until && now < until)
            return ChatSafetyLevel.Over;

        _overUntil = null;
        return signals.WatchingCohortOpen || signals.MeterOverBaseline
            ? ChatSafetyLevel.Watch
            : ChatSafetyLevel.Calm;
    }
}
