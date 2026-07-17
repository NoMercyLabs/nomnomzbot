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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Infrastructure.Moderation;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves the escalation ladder (moderation.md §3.11, J.10/J.11): offenses climb the configured rungs and
/// clamp at the top; the tally restarts when the offense window lapses; forgiveness resets to rung one; a
/// disabled or absent ladder refuses to decide; and the policy upsert validates ascending steps + actions.
/// </summary>
public sealed class ModerationEscalationServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000ff01");
    private static readonly Guid Subject = Guid.Parse("0192a000-0000-7000-8000-00000000ff02");
    private static readonly DateTimeOffset T0 = new(2026, 7, 17, 7, 0, 0, TimeSpan.Zero);

    private static (
        ModerationEscalationService Sut,
        ModerationServiceTestDbContext Db,
        FakeTimeProvider Clock
    ) Build()
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        FakeTimeProvider clock = new(T0);
        return (new ModerationEscalationService(db, clock), db, clock);
    }

    private static UpsertEscalationPolicyRequest EnabledPolicy(int windowHours = 168) =>
        new(
            IsEnabled: true,
            Ladder:
            [
                new EscalationLadderStep(1, "warn", null),
                new EscalationLadderStep(2, "timeout", 60),
                new EscalationLadderStep(3, "ban", null),
            ],
            OffenseWindowHours: windowHours,
            CountAutoModViolations: false
        );

    [Fact]
    public async Task Offenses_climb_the_ladder_and_clamp_at_the_top_rung()
    {
        (ModerationEscalationService sut, _, _) = Build();
        await sut.UpsertPolicyAsync(Channel, EnabledPolicy());

        Result<EscalationDecision> first = await sut.ResolveAndRecordAsync(Channel, Subject, "v-1");
        first.Value.Should().Be(new EscalationDecision("warn", null, 1));

        Result<EscalationDecision> second = await sut.ResolveAndRecordAsync(
            Channel,
            Subject,
            "v-1"
        );
        second.Value.Should().Be(new EscalationDecision("timeout", 60, 2));

        Result<EscalationDecision> third = await sut.ResolveAndRecordAsync(Channel, Subject, "v-1");
        third.Value.Should().Be(new EscalationDecision("ban", null, 3));

        // Beyond the top rung: the highest step clamps.
        Result<EscalationDecision> fourth = await sut.ResolveAndRecordAsync(
            Channel,
            Subject,
            "v-1"
        );
        fourth.Value.Should().Be(new EscalationDecision("ban", null, 4));
    }

    [Fact]
    public async Task The_tally_restarts_when_the_offense_window_lapses()
    {
        (ModerationEscalationService sut, _, FakeTimeProvider clock) = Build();
        await sut.UpsertPolicyAsync(Channel, EnabledPolicy(windowHours: 24));

        await sut.ResolveAndRecordAsync(Channel, Subject, "v-1"); // offense 1
        await sut.ResolveAndRecordAsync(Channel, Subject, "v-1"); // offense 2

        clock.Advance(TimeSpan.FromHours(25)); // past the 24h window

        Result<EscalationDecision> afterLapse = await sut.ResolveAndRecordAsync(
            Channel,
            Subject,
            "v-1"
        );
        afterLapse.Value.Should().Be(new EscalationDecision("warn", null, 1), "the window lapsed");
    }

    [Fact]
    public async Task Forgiveness_clears_the_tally_and_is_idempotent()
    {
        (ModerationEscalationService sut, ModerationServiceTestDbContext db, _) = Build();
        await sut.UpsertPolicyAsync(Channel, EnabledPolicy());
        await sut.ResolveAndRecordAsync(Channel, Subject, "v-1");
        await sut.ResolveAndRecordAsync(Channel, Subject, "v-1");

        (await sut.ResetUserAsync(Channel, Subject)).IsSuccess.Should().BeTrue();
        (await db.ModerationEscalationStates.CountAsync()).Should().Be(0);
        (await sut.ResetUserAsync(Channel, Subject)).IsSuccess.Should().BeTrue("idempotent");

        (await sut.ResolveAndRecordAsync(Channel, Subject, "v-1"))
            .Value.Should()
            .Be(new EscalationDecision("warn", null, 1), "forgiveness restarted the climb");
    }

    [Fact]
    public async Task A_disabled_or_absent_ladder_refuses_to_decide()
    {
        (ModerationEscalationService sut, _, _) = Build();

        // No policy row at all.
        (await sut.ResolveAndRecordAsync(Channel, Subject, "v-1"))
            .ErrorCode.Should()
            .Be("VALIDATION_FAILED");

        // A configured but DISABLED policy.
        await sut.UpsertPolicyAsync(Channel, EnabledPolicy() with { IsEnabled = false });
        (await sut.ResolveAndRecordAsync(Channel, Subject, "v-1"))
            .ErrorCode.Should()
            .Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task GetPolicy_reads_the_disabled_default_ladder_when_unset_and_the_saved_one_after()
    {
        (ModerationEscalationService sut, _, _) = Build();

        Result<ModerationEscalationPolicyDto> unset = await sut.GetPolicyAsync(Channel);
        unset.Value.IsEnabled.Should().BeFalse();
        unset.Value.OffenseWindowHours.Should().Be(168);
        unset.Value.Ladder.Should().HaveCount(6);
        unset.Value.Ladder[0].Should().Be(new EscalationLadderStep(1, "warn", null));
        unset.Value.Ladder[5].Should().Be(new EscalationLadderStep(6, "ban", null));

        await sut.UpsertPolicyAsync(Channel, EnabledPolicy(windowHours: 48));
        Result<ModerationEscalationPolicyDto> saved = await sut.GetPolicyAsync(Channel);
        saved.Value.IsEnabled.Should().BeTrue();
        saved.Value.OffenseWindowHours.Should().Be(48);
        saved.Value.Ladder.Should().HaveCount(3);
    }

    [Fact]
    public async Task Upsert_validates_the_ladder_shape()
    {
        (ModerationEscalationService sut, _, _) = Build();

        // Non-ascending steps.
        (
            await sut.UpsertPolicyAsync(
                Channel,
                new UpsertEscalationPolicyRequest(
                    true,
                    [
                        new EscalationLadderStep(2, "warn", null),
                        new EscalationLadderStep(1, "ban", null),
                    ],
                    168,
                    false
                )
            )
        )
            .ErrorCode.Should()
            .Be("VALIDATION_FAILED");

        // Unknown action.
        (
            await sut.UpsertPolicyAsync(
                Channel,
                new UpsertEscalationPolicyRequest(
                    true,
                    [new EscalationLadderStep(1, "vaporize", null)],
                    168,
                    false
                )
            )
        )
            .ErrorCode.Should()
            .Be("VALIDATION_FAILED");

        // Timeout step without a duration.
        (
            await sut.UpsertPolicyAsync(
                Channel,
                new UpsertEscalationPolicyRequest(
                    true,
                    [new EscalationLadderStep(1, "timeout", null)],
                    168,
                    false
                )
            )
        )
            .ErrorCode.Should()
            .Be("VALIDATION_FAILED");
    }
}
