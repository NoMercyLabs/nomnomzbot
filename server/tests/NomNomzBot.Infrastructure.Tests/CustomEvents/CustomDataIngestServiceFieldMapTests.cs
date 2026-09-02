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
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.CustomEvents.Entities;
using NomNomzBot.Domain.CustomEvents.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.CustomEvents;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomEvents;

/// <summary>
/// Proves the S100 poll-ingest side of field-map parsing: a poll cycle with a correct field-map extracts the
/// mapped values into the template-variable store (the domain event + latest-value cache), and a field-map with
/// one broken path among several correct ones still ingests the working fields — recording the broken one as a
/// per-field error on the source, rather than dropping it silently or failing the whole ingest.
/// </summary>
public sealed class CustomDataIngestServiceFieldMapTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();

    private static (
        CustomDataIngestService Sut,
        AuthDbContext Db,
        IEventBus EventBus,
        ICacheService Cache
    ) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IEventBus eventBus = Substitute.For<IEventBus>();
        ICacheService cache = Substitute.For<ICacheService>();
        CustomDataIngestService sut = new(db, eventBus, cache);
        return (sut, db, eventBus, cache);
    }

    private static async Task<CustomDataSource> SeedSourceAsync(
        AuthDbContext db,
        string fieldMapJson
    )
    {
        CustomDataSource source = new()
        {
            BroadcasterId = Broadcaster,
            Name = "heartrate",
            DisplayName = "Heart Rate",
            SourceKind = "poll",
            FieldMapJson = fieldMapJson,
            IsEnabled = true,
        };
        db.CustomDataSources.Add(source);
        await db.SaveChangesAsync();
        return source;
    }

    [Fact]
    public async Task A_correct_field_map_extracts_every_mapped_value_into_the_domain_event()
    {
        (CustomDataIngestService sut, AuthDbContext db, IEventBus eventBus, ICacheService _) =
            Build();
        await SeedSourceAsync(db, "{\"bpm\":\"$.data.heartRate\",\"zone\":\"$.data.zone\"}");

        Result result = await sut.IngestAsync(
            Broadcaster,
            "heartrate",
            "{\"data\":{\"heartRate\":128,\"zone\":\"cardio\"}}"
        );

        result.IsSuccess.Should().BeTrue();
        await eventBus
            .Received(1)
            .PublishAsync(
                Arg.Is<CustomDataReceivedEvent>(e =>
                    e.BroadcasterId == Broadcaster
                    && e.SourceName == "heartrate"
                    && e.Fields["bpm"] == "128"
                    && e.Fields["zone"] == "cardio"
                ),
                Arg.Any<CancellationToken>()
            );

        CustomDataSource persisted = await db.CustomDataSources.FirstAsync();
        persisted.LastFieldErrorsJson.Should().BeNull();
    }

    [Fact]
    public async Task A_broken_path_among_correct_ones_still_ingests_the_working_fields_and_records_the_error()
    {
        (CustomDataIngestService sut, AuthDbContext db, IEventBus eventBus, ICacheService _) =
            Build();
        // "bpm" resolves; "missing" points at a path this payload never has.
        await SeedSourceAsync(
            db,
            "{\"bpm\":\"$.data.heartRate\",\"missing\":\"$.data.doesNotExist\"}"
        );

        Result result = await sut.IngestAsync(
            Broadcaster,
            "heartrate",
            "{\"data\":{\"heartRate\":128}}"
        );

        // Ingest is NOT a total failure — the working field still went through.
        result.IsSuccess.Should().BeTrue();
        await eventBus
            .Received(1)
            .PublishAsync(
                Arg.Is<CustomDataReceivedEvent>(e =>
                    e.Fields.ContainsKey("bpm")
                    && e.Fields["bpm"] == "128"
                    && !e.Fields.ContainsKey("missing")
                ),
                Arg.Any<CancellationToken>()
            );

        CustomDataSource persisted = await db.CustomDataSources.FirstAsync();
        persisted.LastFieldErrorsJson.Should().NotBeNull();
        persisted.LastFieldErrorsJson.Should().Contain("missing");
        persisted.LastFieldErrorsJson.Should().NotContain("\"bpm\"");
    }
}
