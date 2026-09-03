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
/// A room-tightening control (spam-defense.md §L5.1).
///
/// <para>Every member restricts <b>posting</b>. There is deliberately no ban, timeout, or block member
/// in this enum: lockdown must not be able to express an action against a person, because SD0's whole
/// claim is that it costs everyone a few minutes and costs nobody their account.</para>
/// </summary>
public enum LockdownControl
{
    /// <summary>Twitch Shield Mode.</summary>
    ShieldMode,

    /// <summary>Followers-only, with a minimum follow age.</summary>
    FollowersOnly,

    /// <summary>Slow mode.</summary>
    SlowMode,

    /// <summary>Unique-chat / no repeated messages.</summary>
    UniqueChat,

    /// <summary>Subscribers-only. The heaviest control, and optional.</summary>
    SubscribersOnly,

    /// <summary>The platform's own AutoMod at its strictest level.</summary>
    StrictAutoMod,

    /// <summary>Push the active campaign's skeletons into the platform's blocked-term list.</summary>
    BlockedTerms,
}

/// <summary>
/// What one platform can actually do. Lockdown is a per-platform capability map, not one uniform
/// action: where a platform offers nothing pre-emptive we are honestly reactive there, and say so,
/// rather than implying cover we do not have.
/// </summary>
public sealed record PlatformLockdownCapabilities(
    string Platform,
    IReadOnlySet<LockdownControl> Supported
)
{
    /// <summary>Twitch — every one of these exists in the Helix moderation and chat APIs.</summary>
    public static PlatformLockdownCapabilities Twitch { get; } =
        new(
            "twitch",
            new HashSet<LockdownControl>
            {
                LockdownControl.ShieldMode,
                LockdownControl.FollowersOnly,
                LockdownControl.SlowMode,
                LockdownControl.UniqueChat,
                LockdownControl.SubscribersOnly,
                LockdownControl.StrictAutoMod,
                LockdownControl.BlockedTerms,
            }
        );

    /// <summary>Kick — followers/subscriber gating and slow mode.</summary>
    public static PlatformLockdownCapabilities Kick { get; } =
        new(
            "kick",
            new HashSet<LockdownControl>
            {
                LockdownControl.FollowersOnly,
                LockdownControl.SlowMode,
                LockdownControl.SubscribersOnly,
            }
        );

    /// <summary>YouTube — slow mode, members-only, and its own blocked-term list.</summary>
    public static PlatformLockdownCapabilities YouTube { get; } =
        new(
            "youtube",
            new HashSet<LockdownControl>
            {
                LockdownControl.SlowMode,
                LockdownControl.SubscribersOnly,
                LockdownControl.BlockedTerms,
            }
        );

    /// <summary>X Live — nothing pre-emptive we can drive. We are reactive there, and say so.</summary>
    public static PlatformLockdownCapabilities X { get; } =
        new("x", new HashSet<LockdownControl>());

    public bool Supports(LockdownControl control) => Supported.Contains(control);
}

/// <summary>
/// One control engaged, with the value it had beforehand so the window can put it back exactly.
/// </summary>
/// <param name="Control">What was tightened.</param>
/// <param name="PreviousValue">
/// The setting's prior value, serialized by the caller. Required: a lockdown that cannot say what the
/// room looked like before is a lockdown that cannot restore it, and would silently leave a channel in
/// followers-only after the raid moved on.
/// </param>
public sealed record EngagedControl(LockdownControl Control, string PreviousValue);

/// <summary>
/// What a lockdown will do on one platform, including — honestly — what it cannot do there.
/// </summary>
/// <param name="Platform">Which platform this plan is for.</param>
/// <param name="Engaged">Controls that will be applied, each carrying its prior value.</param>
/// <param name="Unavailable">
/// Requested controls this platform does not offer. Surfaced to the operator rather than dropped, so
/// the dashboard never implies cover that does not exist.
/// </param>
public sealed record LockdownPlan(
    string Platform,
    IReadOnlyList<EngagedControl> Engaged,
    IReadOnlyList<LockdownControl> Unavailable
)
{
    /// <summary>True when this platform offers nothing pre-emptive at all — we are reactive here.</summary>
    public bool IsPurelyReactive => Engaged.Count == 0;
}

/// <summary>
/// A hate-raid lockdown (spam-defense.md §L5.1, SD0, SD12).
///
/// <para><b>Lockdown is not "we stop messages."</b> We do not host the chat — Twitch, YouTube, Kick and
/// X publish a message the moment it is sent, and there is no pre-publish hook we can stand in. What
/// lockdown does is tighten the platform's own rules for the room, all at once, reversibly. That is the
/// preferred response to uncertainty precisely because it costs everyone a few minutes and costs nobody
/// their account.</para>
///
/// <para>It <b>restricts posting and never removes, blocks, or bans anyone for being present</b>. It
/// carries a timer, restores every setting it changed, and ends early on one click. Semi-Trusted and
/// Established viewers keep talking through it.</para>
///
/// <para>The hate-raid track trips this as its <b>first</b> action rather than its last: tightening the
/// room ahead of judging individuals is the whole point of SD0.</para>
/// </summary>
public sealed class LockdownWindow
{
    private readonly List<EngagedControl> _engaged = [];

    public LockdownWindow(
        string platform,
        string trigger,
        DateTimeOffset startedAt,
        TimeSpan duration
    )
    {
        Platform = platform;
        Trigger = trigger;
        StartedAt = startedAt;
        ExpiresAt = startedAt + duration;
    }

    public string Platform { get; }

    /// <summary>Why the room was tightened, in words an operator can read back later.</summary>
    public string Trigger { get; }

    public DateTimeOffset StartedAt { get; }

    /// <summary>When the window auto-expires. A lockdown always ends by itself.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Set when a moderator ends the window early.</summary>
    public DateTimeOffset? EndedAt { get; private set; }

    /// <summary>Controls currently engaged, each with the value to put back.</summary>
    public IReadOnlyList<EngagedControl> Engaged => _engaged;

    public bool IsActive(DateTimeOffset now) => EndedAt is null && now < ExpiresAt;

    /// <summary>
    /// Work out what can be tightened here. Requested controls the platform does not support come back
    /// in <see cref="LockdownPlan.Unavailable"/> rather than being silently dropped.
    /// </summary>
    public static LockdownPlan Plan(
        PlatformLockdownCapabilities capabilities,
        IReadOnlyCollection<LockdownControl> requested,
        IReadOnlyDictionary<LockdownControl, string> currentSettings
    )
    {
        List<EngagedControl> engaged = [];
        List<LockdownControl> unavailable = [];

        foreach (LockdownControl control in requested)
        {
            if (!capabilities.Supports(control))
            {
                unavailable.Add(control);
                continue;
            }

            engaged.Add(
                new EngagedControl(
                    control,
                    currentSettings.TryGetValue(control, out string? previous)
                        ? previous
                        : string.Empty
                )
            );
        }

        return new LockdownPlan(capabilities.Platform, engaged, unavailable);
    }

    /// <summary>Apply a plan to this window.</summary>
    public void Engage(LockdownPlan plan)
    {
        _engaged.Clear();
        _engaged.AddRange(plan.Engaged);
    }

    /// <summary>End the window now. One click, at any point.</summary>
    public void EndEarly(DateTimeOffset now)
    {
        EndedAt = now;
        if (ExpiresAt > now)
            ExpiresAt = now;
    }

    /// <summary>
    /// Every setting to put back, in the value it held before. Produced whether the window expired on
    /// its timer or was ended by hand — a room left tightened after the raid moved on is the failure
    /// this exists to prevent.
    /// </summary>
    public IReadOnlyList<EngagedControl> BuildRestore() => _engaged.ToList();

    /// <summary>
    /// Whether a viewer keeps talking through the lockdown. Standing means yes: the room is being
    /// tightened against strangers, and the people who have been here for years are not the raid.
    /// </summary>
    public static bool KeepsTalkingThrough(SpamTrustTier tier) =>
        TrustTierLadder.IsShieldedFromAutomatedAccountAction(tier);
}
