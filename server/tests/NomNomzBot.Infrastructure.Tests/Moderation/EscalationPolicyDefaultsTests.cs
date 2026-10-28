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

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Tests.EventStore;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves every escalation-policy setting has ONE default (<see cref="EscalationPolicyDefaults"/>) read by
/// the entity, the EF mapping, the API request and the service; that a read of a channel with no row
/// resolves to those defaults and writes nothing; that each field survives PUT then GET; and that
/// out-of-range values are refused.
/// </summary>
public sealed class EscalationPolicyDefaultsTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000fe01");
    private static readonly DateTimeOffset T0 = new(2026, 10, 19, 7, 0, 0, TimeSpan.Zero);

    private static (ModerationEscalationService Sut, EventStoreTestDbContext Db) Build(
        SqliteTestDatabase database
    )
    {
        EventStoreTestDbContext db = database.NewContext();
        return (new(db, new FakeTimeProvider(T0)), db);
    }

    private static UpsertEscalationPolicyRequest Valid() =>
        new(
            IsEnabled: true,
            Ladder: [new(1, "warn", null), new(2, "timeout", 60), new(3, "ban", null)],
            OffenseWindowHours: 168,
            CountAutoModViolations: false
        );

    [Fact]
    public async Task A_channel_with_no_row_reads_the_defaults_and_the_read_writes_nothing()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (ModerationEscalationService sut, EventStoreTestDbContext db) = Build(database);

        Result<ModerationEscalationPolicyDto> read = await sut.GetPolicyAsync(Channel);

        read.IsSuccess.Should().BeTrue();
        read.Value.IsEnabled.Should().Be(EscalationPolicyDefaults.IsEnabled);
        read.Value.OffenseWindowHours.Should().Be(EscalationPolicyDefaults.OffenseWindowHours);
        read.Value.CountAutoModViolations.Should()
            .Be(EscalationPolicyDefaults.CountAutoModViolations);
        read.Value.DefaultTimeoutSeconds.Should()
            .Be(EscalationPolicyDefaults.DefaultTimeoutSeconds);
        read.Value.Ladder.Select(s => (s.AtOffense, s.Action, s.TimeoutSeconds))
            .Should()
            .Equal(EscalationPolicyDefaults.Ladder);
        (await db.ModerationEscalationPolicies.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task The_entity_the_ef_mapping_and_the_request_share_the_one_default()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (_, EventStoreTestDbContext db) = Build(database);
        ModerationEscalationPolicy entity = new();
        UpsertEscalationPolicyRequest request = new(false, [], 1, false);
        IProperty mapped = db
            .Model.FindEntityType(typeof(ModerationEscalationPolicy))!
            .FindProperty(nameof(ModerationEscalationPolicy.DefaultTimeoutSeconds))!;

        entity.IsEnabled.Should().Be(EscalationPolicyDefaults.IsEnabled);
        entity.OffenseWindowHours.Should().Be(EscalationPolicyDefaults.OffenseWindowHours);
        entity.CountAutoModViolations.Should().Be(EscalationPolicyDefaults.CountAutoModViolations);
        entity.DefaultTimeoutSeconds.Should().Be(EscalationPolicyDefaults.DefaultTimeoutSeconds);
        request.DefaultTimeoutSeconds.Should().Be(EscalationPolicyDefaults.DefaultTimeoutSeconds);
        mapped.GetDefaultValue().Should().Be(EscalationPolicyDefaults.DefaultTimeoutSeconds);
    }

    [Fact]
    public async Task Every_field_round_trips_put_then_get_with_non_default_values()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (ModerationEscalationService sut, _) = Build(database);
        UpsertEscalationPolicyRequest request = new(
            IsEnabled: true,
            Ladder: [new(2, "warn", null), new(5, "timeout", 123), new(9, "ban", null)],
            OffenseWindowHours: 37,
            CountAutoModViolations: true,
            DefaultTimeoutSeconds: 4321
        );

        (await sut.UpsertPolicyAsync(Channel, request)).IsSuccess.Should().BeTrue();
        ModerationEscalationPolicyDto read = (await sut.GetPolicyAsync(Channel)).Value;

        read.IsEnabled.Should().BeTrue();
        read.OffenseWindowHours.Should().Be(37);
        read.CountAutoModViolations.Should().BeTrue();
        read.DefaultTimeoutSeconds.Should().Be(4321);
        read.Ladder.Should().Equal(request.Ladder);
    }

    [Fact]
    public async Task The_limits_themselves_are_accepted_and_one_past_each_is_refused()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (ModerationEscalationService sut, _) = Build(database);

        (
            await sut.UpsertPolicyAsync(
                Channel,
                Valid() with
                {
                    OffenseWindowHours = EscalationPolicyDefaults.MaxOffenseWindowHours,
                    DefaultTimeoutSeconds = EscalationPolicyDefaults.MaxTimeoutSeconds,
                    Ladder = [new(1, "timeout", EscalationPolicyDefaults.MaxTimeoutSeconds)],
                }
            )
        )
            .IsSuccess.Should()
            .BeTrue();

        List<UpsertEscalationPolicyRequest> refused =
        [
            Valid() with
            {
                OffenseWindowHours = EscalationPolicyDefaults.MaxOffenseWindowHours + 1,
            },
            Valid() with
            {
                OffenseWindowHours = 0,
            },
            Valid() with
            {
                DefaultTimeoutSeconds = EscalationPolicyDefaults.MaxTimeoutSeconds + 1,
            },
            Valid() with
            {
                DefaultTimeoutSeconds = 0,
            },
            Valid() with
            {
                Ladder = [new(1, "timeout", EscalationPolicyDefaults.MaxTimeoutSeconds + 1)],
            },
            Valid() with
            {
                Ladder = Enumerable
                    .Range(1, EscalationPolicyDefaults.MaxLadderSteps + 1)
                    .Select(n => new EscalationLadderStep(n, "warn", null))
                    .ToList(),
            },
            Valid() with
            {
                Ladder = null!,
            },
        ];
        foreach (UpsertEscalationPolicyRequest request in refused)
        {
            Result<ModerationEscalationPolicyDto> result = await sut.UpsertPolicyAsync(
                Channel,
                request
            );
            result.IsSuccess.Should().BeFalse();
            result.ErrorCode.Should().Be("VALIDATION_FAILED");
        }

        // The refused writes changed nothing: the accepted limits are still stored.
        ModerationEscalationPolicyDto stored = (await sut.GetPolicyAsync(Channel)).Value;
        stored.OffenseWindowHours.Should().Be(EscalationPolicyDefaults.MaxOffenseWindowHours);
        stored.DefaultTimeoutSeconds.Should().Be(EscalationPolicyDefaults.MaxTimeoutSeconds);
    }
}
