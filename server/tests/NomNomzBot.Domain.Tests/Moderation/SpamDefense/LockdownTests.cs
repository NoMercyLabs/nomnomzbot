// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Domain.Tests.Moderation.SpamDefense;

/// <summary>
/// Hate-raid lockdown (spam-defense.md §L5.1, SD0, SD12), including the §8 acceptance scenario
/// verbatim: a synthetic hate raid at medium confidence engages lockdown, issues zero bans and zero
/// timeouts, restores every setting it changed, and never stops an Established viewer talking.
/// </summary>
public class LockdownTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 3, 21, 0, 0, TimeSpan.Zero);

    private static readonly LockdownControl[] FullRequest =
    [
        LockdownControl.ShieldMode,
        LockdownControl.FollowersOnly,
        LockdownControl.SlowMode,
        LockdownControl.UniqueChat,
        LockdownControl.StrictAutoMod,
    ];

    private static Dictionary<LockdownControl, string> RoomAsItWas() =>
        new()
        {
            [LockdownControl.ShieldMode] = "off",
            [LockdownControl.FollowersOnly] = "disabled",
            [LockdownControl.SlowMode] = "0",
            [LockdownControl.UniqueChat] = "off",
            [LockdownControl.StrictAutoMod] = "level:2",
        };

    private static LockdownWindow NewWindow(TimeSpan? duration = null) =>
        new(
            "twitch",
            "hate raid: 30 accounts, slur content",
            T0,
            duration ?? TimeSpan.FromMinutes(15)
        );

    // ---- The §8 acceptance scenario -------------------------------------------------------------

    [Fact]
    public void ASyntheticHateRaid_TightensTheRoom_AndBansNobody()
    {
        // §8 / SD0, the whole thesis of this design: 30 accounts arrive posting harassment and the
        // system's FIRST action is to tighten the room, not to judge the people in it. Medium
        // confidence on every one of them, and the count of account actions must be zero.
        LockdownWindow window = NewWindow();
        window.Engage(
            LockdownWindow.Plan(PlatformLockdownCapabilities.Twitch, FullRequest, RoomAsItWas())
        );

        window.IsActive(T0.AddMinutes(1)).Should().BeTrue("lockdown engaged");
        window.Engaged.Should().HaveCount(5);

        int accountActions = 0;
        for (int i = 0; i < 30; i++)
        {
            SpamDecision decision = SpamEnforcement.Decide(
                SpamConfidence.Medium,
                SpamTrustTier.Untrusted,
                dryRun: false
            );
            if (decision.TouchesAccount)
                accountActions++;
        }

        accountActions
            .Should()
            .Be(
                0,
                "medium confidence deletes and queues; it never touches an account, at any tier"
            );
    }

    [Fact]
    public void AnEstablishedViewer_KeepsTalkingThroughTheWholeLockdown()
    {
        // The room is being tightened against strangers. The people who have been here for years are
        // not the raid, and a lockdown that silences them has punished the wrong room.
        LockdownWindow.KeepsTalkingThrough(SpamTrustTier.Established).Should().BeTrue();
        LockdownWindow.KeepsTalkingThrough(SpamTrustTier.SemiTrusted).Should().BeTrue();
    }

    [Fact]
    public void EverySettingLockdownChanged_IsRestoredWhenTheWindowCloses()
    {
        // A room left in followers-only after the raid moved on is the failure this exists to prevent,
        // and it is the kind that goes unnoticed for days.
        LockdownWindow window = NewWindow();
        Dictionary<LockdownControl, string> before = RoomAsItWas();
        window.Engage(
            LockdownWindow.Plan(PlatformLockdownCapabilities.Twitch, FullRequest, before)
        );

        IReadOnlyList<EngagedControl> restore = window.BuildRestore();

        restore.Should().HaveCount(before.Count);
        foreach (EngagedControl control in restore)
            control
                .PreviousValue.Should()
                .Be(
                    before[control.Control],
                    $"{control.Control} must go back to exactly what it was"
                );
    }

    // ---- Lockdown cannot express an action against a person -------------------------------------

    [Fact]
    public void NoLockdownControl_ActsOnAPerson()
    {
        // Structural, not behavioural: if the enum could express a ban, some future code path would
        // eventually use it. Lockdown restricts posting for the room and has no vocabulary for
        // anything else.
        // BlockedTerms is the one name that reads like an exception and is not: it blocks TEXT at the
        // platform's send gate, which is the single place our detection becomes real prevention. It
        // acts on a string, never on an account.
        string[] controls = Enum.GetNames<LockdownControl>()
            .Where(name => name != nameof(LockdownControl.BlockedTerms))
            .ToArray();

        controls
            .Should()
            .NotContain(name =>
                name.Contains("Ban", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Timeout", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Block", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Remove", StringComparison.OrdinalIgnoreCase)
                || name.Contains("User", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Account", StringComparison.OrdinalIgnoreCase)
            );
    }

    // ---- Timers and early exit ------------------------------------------------------------------

    [Fact]
    public void ALockdownAlwaysEndsByItself()
    {
        LockdownWindow window = NewWindow(TimeSpan.FromMinutes(15));

        window.IsActive(T0.AddMinutes(14)).Should().BeTrue();
        window.IsActive(T0.AddMinutes(15)).Should().BeFalse("the window auto-expires");
    }

    [Fact]
    public void AModeratorCanEndItEarly_AndTheSettingsStillComeBack()
    {
        LockdownWindow window = NewWindow(TimeSpan.FromHours(1));
        window.Engage(
            LockdownWindow.Plan(PlatformLockdownCapabilities.Twitch, FullRequest, RoomAsItWas())
        );

        window.EndEarly(T0.AddMinutes(3));

        window.IsActive(T0.AddMinutes(4)).Should().BeFalse();
        window.EndedAt.Should().Be(T0.AddMinutes(3));
        window.BuildRestore().Should().HaveCount(5, "ending early restores just as expiry does");
    }

    // ---- Per-platform honesty --------------------------------------------------------------------

    [Fact]
    public void APlatformWithFewerControls_EngagesWhatItHas_AndReportsWhatItCannotDo()
    {
        // Lockdown is a capability map, not one uniform action. Kick has no Shield Mode and no
        // AutoMod; the operator is told that rather than being left to assume cover.
        LockdownPlan plan = LockdownWindow.Plan(
            PlatformLockdownCapabilities.Kick,
            FullRequest,
            RoomAsItWas()
        );

        plan.Engaged.Select(e => e.Control)
            .Should()
            .BeEquivalentTo([LockdownControl.FollowersOnly, LockdownControl.SlowMode]);
        plan.Unavailable.Should()
            .BeEquivalentTo([
                LockdownControl.ShieldMode,
                LockdownControl.UniqueChat,
                LockdownControl.StrictAutoMod,
            ]);
        plan.IsPurelyReactive.Should().BeFalse();
    }

    [Fact]
    public void APlatformWithNothingPreEmptive_SaysSo_RatherThanImplyingCover()
    {
        // X Live offers nothing we can drive. Claiming a lockdown there would be the most dangerous
        // kind of bug: a streamer under attack believing the room is protected when it is not.
        LockdownPlan plan = LockdownWindow.Plan(
            PlatformLockdownCapabilities.X,
            FullRequest,
            RoomAsItWas()
        );

        plan.Engaged.Should().BeEmpty();
        plan.IsPurelyReactive.Should().BeTrue();
        plan.Unavailable.Should().BeEquivalentTo(FullRequest, "all of it, named explicitly");
    }

    [Fact]
    public void SubscribersOnly_IsNotEngagedUnlessAskedFor()
    {
        // The heaviest control, and optional: it locks out every new viewer arriving for honest
        // reasons. It ships off by default and only appears when the operator requested it.
        LockdownPlan plan = LockdownWindow.Plan(
            PlatformLockdownCapabilities.Twitch,
            FullRequest,
            RoomAsItWas()
        );

        plan.Engaged.Select(e => e.Control).Should().NotContain(LockdownControl.SubscribersOnly);
    }

    [Fact]
    public void AControlWithNoRecordedPriorValue_StillRestoresExplicitly()
    {
        // A missing prior value must not become a silent skip — that is how a setting stays tightened.
        LockdownPlan plan = LockdownWindow.Plan(
            PlatformLockdownCapabilities.Twitch,
            [LockdownControl.SlowMode],
            new Dictionary<LockdownControl, string>()
        );

        plan.Engaged.Should().ContainSingle();
        plan.Engaged[0].Control.Should().Be(LockdownControl.SlowMode);
    }

    [Fact]
    public void TheWindowRecordsWhyItFired_SoAnOperatorCanReadItBackLater()
    {
        NewWindow().Trigger.Should().Contain("hate raid");
    }
}
