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
using NomNomzBot.Infrastructure.Tests.EventStore;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves the escalation ladder (moderation.md §3.11, J.10/J.11): offenses climb the configured rungs and
/// clamp at the top; the tally restarts when the offense window lapses; forgiveness resets to rung one; a
/// disabled or absent ladder refuses to decide; and the policy upsert validates ascending steps + actions.
///
/// Runs on the real relational SQLite harness (<see cref="SqliteTestDatabase"/>/<see cref="EventStoreTestDbContext"/>),
/// not EF's InMemory provider: S005/F13's fix atomically advances <c>OffenseCount</c> via
/// <c>ExecuteUpdateAsync</c>, which InMemory does not support at all (mirrors the S004b precedent —
/// UsageMeteringServiceTests made the same move for the same reason).
/// </summary>
public sealed class ModerationEscalationServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000ff01");
    private static readonly Guid Subject = Guid.Parse("0192a000-0000-7000-8000-00000000ff02");
    private static readonly DateTimeOffset T0 = new(2026, 7, 17, 7, 0, 0, TimeSpan.Zero);

    private static (
        ModerationEscalationService Sut,
        EventStoreTestDbContext Db,
        FakeTimeProvider Clock
    ) Build(SqliteTestDatabase database)
    {
        EventStoreTestDbContext db = database.NewContext();
        FakeTimeProvider clock = new(T0);
        return (new(db, clock), db, clock);
    }

    private static UpsertEscalationPolicyRequest EnabledPolicy(int windowHours = 168) =>
        new(
            IsEnabled: true,
            Ladder: [new(1, "warn", null), new(2, "timeout", 60), new(3, "ban", null)],
            OffenseWindowHours: windowHours,
            CountAutoModViolations: false
        );

    [Fact]
    public async Task Offenses_climb_the_ladder_and_clamp_at_the_top_rung()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (ModerationEscalationService sut, _, _) = Build(database);
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
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (ModerationEscalationService sut, _, FakeTimeProvider clock) = Build(database);
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
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (ModerationEscalationService sut, EventStoreTestDbContext db, _) = Build(database);
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
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (ModerationEscalationService sut, _, _) = Build(database);

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
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (ModerationEscalationService sut, _, _) = Build(database);

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
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (ModerationEscalationService sut, _, _) = Build(database);

        // Non-ascending steps.
        (
            await sut.UpsertPolicyAsync(
                Channel,
                new(true, [new(2, "warn", null), new(1, "ban", null)], 168, false)
            )
        )
            .ErrorCode.Should()
            .Be("VALIDATION_FAILED");

        // Unknown action.
        (await sut.UpsertPolicyAsync(Channel, new(true, [new(1, "vaporize", null)], 168, false)))
            .ErrorCode.Should()
            .Be("VALIDATION_FAILED");

        // Timeout step without a duration.
        (await sut.UpsertPolicyAsync(Channel, new(true, [new(1, "timeout", null)], 168, false)))
            .ErrorCode.Should()
            .Be("VALIDATION_FAILED");
    }

    /// <summary>
    /// Proves S005/F13: <c>ModerationEscalationService.ResolveAndRecordAsync</c> no longer read-modify-writes
    /// a TRACKED <c>ModerationEscalationState.OffenseCount++</c> then <c>SaveChangesAsync</c> — before the
    /// fix, two concurrent offenses for the SAME subject both loaded the SAME row, both incremented their own
    /// in-memory copy, and whichever committed second silently overwrote the first's increment: one offense
    /// vanishes and the ladder can under-escalate a repeat offender. Reuses the S004/S004b mechanism (an
    /// unconditional <c>ExecuteUpdateAsync</c> <c>OffenseCount + 1</c> evaluated against the CURRENT row).
    ///
    /// Pre-fix, running N concurrent first-time offenses for one subject converges <c>OffenseCount</c> well
    /// below N (a lost-update race, not an exception) — the same shape S004d observed landing 15 concurrent
    /// +1 folds at well under 15. This test proves the POST-fix count is exactly N, then makes one more
    /// SEQUENTIAL call to prove the ladder actually escalates to the correct rung for N+1 — a state-based
    /// assertion, not just a counter.
    /// </summary>
    [Fact]
    public async Task Concurrent_offenses_for_one_subject_all_land_and_the_ladder_escalates_to_the_correct_rung()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        const int concurrency = 15;

        // Seed the policy once, through its own context, before any concurrent recording starts.
        using (EventStoreTestDbContext seed = database.NewContext())
        {
            ModerationEscalationService seeder = new(seed, new FakeTimeProvider(T0));
            Result<ModerationEscalationPolicyDto> upserted = await seeder.UpsertPolicyAsync(
                Channel,
                new(
                    IsEnabled: true,
                    // One rung per offense up to 20, so OffenseCount N maps 1:1 to rung N — makes the
                    // post-batch escalation assertion unambiguous.
                    Ladder:
                    [
                        .. Enumerable
                            .Range(1, 20)
                            .Select(n => new EscalationLadderStep(
                                n,
                                n == 20 ? "ban" : "warn",
                                null
                            )),
                    ],
                    OffenseWindowHours: 168,
                    CountAutoModViolations: false
                )
            );
            upserted.IsSuccess.Should().BeTrue();
        }

        // Every task gets its OWN DbContext + service instance (mirrors independent concurrent request
        // handling) but targets the SAME (Channel, Subject) — the exact race the fix closes.
        Task[] tasks =
        [
            .. Enumerable
                .Range(0, concurrency)
                .Select(_ =>
                    Task.Run(async () =>
                    {
                        using EventStoreTestDbContext db = database.NewContext();
                        ModerationEscalationService sut = new(db, new FakeTimeProvider(T0));
                        Result<EscalationDecision> result = await sut.ResolveAndRecordAsync(
                            Channel,
                            Subject,
                            "v-1"
                        );
                        result.IsSuccess.Should().BeTrue();
                    })
                ),
        ];

        await Task.WhenAll(tasks);

        using (EventStoreTestDbContext verify = database.NewContext())
        {
            NomNomzBot.Domain.Moderation.Entities.ModerationEscalationState state =
                await verify.ModerationEscalationStates.SingleAsync(s =>
                    s.BroadcasterId == Channel && s.SubjectUserId == Subject
                );
            state
                .OffenseCount.Should()
                .Be(
                    concurrency,
                    $"a lost-update race would leave OffenseCount short of the {concurrency} concurrent "
                        + "offenses actually recorded"
                );
        }

        // One more, SEQUENTIAL offense proves the ladder is actually consulted at the correct, undamaged
        // rung — not just that the raw counter survived.
        using EventStoreTestDbContext next = database.NewContext();
        ModerationEscalationService nextSut = new(next, new FakeTimeProvider(T0));
        Result<EscalationDecision> nextOffense = await nextSut.ResolveAndRecordAsync(
            Channel,
            Subject,
            "v-1"
        );
        nextOffense.Value.Should().Be(new EscalationDecision("warn", null, concurrency + 1));
    }
}
