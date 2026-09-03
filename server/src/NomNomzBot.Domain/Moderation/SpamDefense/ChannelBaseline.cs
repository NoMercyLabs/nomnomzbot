// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Moderation.SpamDefense;

/// <summary>
/// A channel's own rolling baseline for a rate — chatter joins per minute, follows per minute
/// (spam-defense.md §L3).
///
/// <para><b>Baselines are per-channel and self-calibrating.</b> A 50-viewer channel and a 50 000-viewer
/// channel must not share a threshold: ten follows a minute is an attack on one and a quiet Tuesday on
/// the other. Nothing here is an absolute number — a spike is only ever a multiple of what THIS channel
/// normally does.</para>
///
/// <para><b>A spike is a trigger to scrutinise, never a set to action (SD9).</b> This type deliberately
/// exposes no way to select accounts. Getting raided, going viral, being hosted or landing on the front
/// page all produce exactly this shape, and the people arriving are real viewers. All a spike may do is
/// raise the channel's evaluation sensitivity for its window; every block still needs that account's own
/// evidence, which is <see cref="FollowBotTrack"/>'s job.</para>
/// </summary>
public sealed class ChannelBaseline
{
    private readonly Queue<double> _samples = new();
    private readonly int _capacity;
    private readonly int _minimumSamples;

    /// <param name="capacity">How many recent samples make up the rolling baseline.</param>
    /// <param name="minimumSamples">
    /// How many samples are needed before anything can be called a spike. Without this a brand-new
    /// channel's very first busy minute is infinitely above a baseline of nothing, and every new
    /// streamer's opening night would read as an attack.
    /// </param>
    public ChannelBaseline(int capacity = 60, int minimumSamples = 10)
    {
        _capacity = capacity < 1 ? 1 : capacity;
        _minimumSamples = minimumSamples < 1 ? 1 : minimumSamples;
    }

    /// <summary>True once there is enough history for a comparison to mean anything.</summary>
    public bool HasEnoughHistory => _samples.Count >= _minimumSamples;

    /// <summary>Mean of the retained samples, or 0 before any have been recorded.</summary>
    public double Mean => _samples.Count == 0 ? 0 : _samples.Sum() / _samples.Count;

    /// <summary>Number of samples currently retained.</summary>
    public int SampleCount => _samples.Count;

    /// <summary>Add one observation, evicting the oldest once the window is full.</summary>
    public void Record(double rate)
    {
        _samples.Enqueue(rate < 0 ? 0 : rate);
        while (_samples.Count > _capacity)
            _samples.Dequeue();
    }

    /// <summary>
    /// Whether <paramref name="rate"/> exceeds this channel's own baseline by
    /// <paramref name="factor"/>. Returns false while history is short — the safe answer when we do not
    /// yet know what normal looks like here is "this is not a spike".
    /// </summary>
    /// <param name="minimumRate">
    /// A floor below which nothing counts as a spike whatever the multiple. A channel whose baseline is
    /// 0.1 follows a minute would otherwise register a spike at two follows, which is one friend telling
    /// another to hit the button.
    /// </param>
    public bool IsSpike(double rate, double factor = 3.0, double minimumRate = 5.0)
    {
        if (!HasEnoughHistory)
            return false;
        if (rate < minimumRate)
            return false;

        return rate > Mean * factor;
    }
}
