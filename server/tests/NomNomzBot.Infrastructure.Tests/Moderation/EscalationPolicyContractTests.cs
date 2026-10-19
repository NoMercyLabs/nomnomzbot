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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Tests.EventStore;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves the escalation API contract the dashboard builds its form from: the policy read carries the
/// defaults and the limits (so no number is hard-coded in the UI), and every refused save names the
/// failing request field in its error data.
/// </summary>
public sealed class EscalationPolicyContractTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000ff31");

    private static (ModerationEscalationService Sut, EventStoreTestDbContext Db) Build(
        SqliteTestDatabase database
    )
    {
        EventStoreTestDbContext db = database.NewContext();
        return (new(db, TimeProvider.System), db);
    }

    private static UpsertEscalationPolicyRequest Valid() =>
        new(
            IsEnabled: true,
            Ladder: [new(1, "warn", null), new(2, "timeout", 60), new(3, "ban", null)],
            OffenseWindowHours: 24,
            CountAutoModViolations: false,
            DefaultTimeoutSeconds: 300
        );

    private static void AssertDefaultsAndLimits(ModerationEscalationPolicyDto dto)
    {
        dto.Defaults.IsEnabled.Should().Be(EscalationPolicyDefaults.IsEnabled);
        dto.Defaults.OffenseWindowHours.Should().Be(EscalationPolicyDefaults.OffenseWindowHours);
        dto.Defaults.CountAutoModViolations.Should()
            .Be(EscalationPolicyDefaults.CountAutoModViolations);
        dto.Defaults.DefaultTimeoutSeconds.Should()
            .Be(EscalationPolicyDefaults.DefaultTimeoutSeconds);
        dto.Defaults.Ladder.Select(s => (s.AtOffense, s.Action, s.TimeoutSeconds))
            .Should()
            .Equal(EscalationPolicyDefaults.Ladder);
        dto.Limits.Should().Be(new EscalationPolicyLimitsDto(1, 8760, 1, 1_209_600, 1, 50));
    }

    [Fact]
    public async Task Reading_an_unsaved_policy_carries_the_defaults_and_the_limits()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (ModerationEscalationService sut, _) = Build(database);

        Result<ModerationEscalationPolicyDto> read = await sut.GetPolicyAsync(Channel);

        read.IsSuccess.Should().BeTrue();
        AssertDefaultsAndLimits(read.Value);
    }

    [Fact]
    public async Task Reading_a_saved_policy_still_carries_the_defaults_and_the_limits()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (ModerationEscalationService sut, _) = Build(database);
        (await sut.UpsertPolicyAsync(Channel, Valid())).IsSuccess.Should().BeTrue();

        Result<ModerationEscalationPolicyDto> read = await sut.GetPolicyAsync(Channel);

        read.Value.OffenseWindowHours.Should().Be(24);
        read.Value.DefaultTimeoutSeconds.Should().Be(300);
        AssertDefaultsAndLimits(read.Value);
    }

    public static TheoryData<UpsertEscalationPolicyRequest, string, string> Refusals() =>
        new()
        {
            {
                Valid() with
                {
                    OffenseWindowHours = 0,
                },
                EscalationPolicyViolation.WindowOutOfRange,
                "offenseWindowHours"
            },
            {
                Valid() with
                {
                    OffenseWindowHours = 8761,
                },
                EscalationPolicyViolation.WindowOutOfRange,
                "offenseWindowHours"
            },
            {
                Valid() with
                {
                    DefaultTimeoutSeconds = 0,
                },
                EscalationPolicyViolation.TimeoutOutOfRange,
                "defaultTimeoutSeconds"
            },
            {
                Valid() with
                {
                    DefaultTimeoutSeconds = 1_209_601,
                },
                EscalationPolicyViolation.TimeoutOutOfRange,
                "defaultTimeoutSeconds"
            },
            { Valid() with { Ladder = [] }, EscalationPolicyViolation.LadderEmpty, "ladder" },
            {
                Valid() with
                {
                    Ladder = Enumerable
                        .Range(1, 51)
                        .Select(i => new EscalationLadderStep(i, "warn", null))
                        .ToList(),
                },
                EscalationPolicyViolation.LadderTooLong,
                "ladder"
            },
            {
                Valid() with
                {
                    Ladder = [new(2, "warn", null), new(2, "ban", null)],
                },
                EscalationPolicyViolation.StepsMustAscend,
                "ladder[1].atOffense"
            },
            {
                Valid() with
                {
                    Ladder = [new(1, "warn", null), new(2, "mute", null)],
                },
                EscalationPolicyViolation.UnknownAction,
                "ladder[1].action"
            },
            {
                Valid() with
                {
                    Ladder = [new(1, "timeout", null)],
                },
                EscalationPolicyViolation.TimeoutStepOutOfRange,
                "ladder[0].timeoutSeconds"
            },
            {
                Valid() with
                {
                    Ladder = [new(1, "timeout", 1_209_601)],
                },
                EscalationPolicyViolation.TimeoutStepOutOfRange,
                "ladder[0].timeoutSeconds"
            },
        };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task A_refused_save_names_the_failing_field_in_its_error_data(
        UpsertEscalationPolicyRequest request,
        string key,
        string field
    )
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (ModerationEscalationService sut, EventStoreTestDbContext db) = Build(database);

        Result<ModerationEscalationPolicyDto> saved = await sut.UpsertPolicyAsync(Channel, request);

        saved.IsFailure.Should().BeTrue();
        saved.ErrorCode.Should().Be("VALIDATION_FAILED");
        saved.ErrorData.Should().Be(new EscalationPolicyViolation(key, field));
        db.ModerationEscalationPolicies.Count().Should().Be(0);
    }
}
