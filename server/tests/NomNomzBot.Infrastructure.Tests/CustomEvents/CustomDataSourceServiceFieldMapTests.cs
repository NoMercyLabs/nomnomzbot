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
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.CustomEvents.Services;
using NomNomzBot.Infrastructure.CustomEvents;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomEvents;

/// <summary>
/// Proves the S100 save-time field-map validation: <see cref="CustomDataSourceService.CreateAsync"/> and
/// <see cref="CustomDataSourceService.UpdateAsync"/> reject a field-map entry whose JSONPath expression is
/// syntactically malformed, with a clear per-field error, instead of silently persisting a mapping that would
/// throw on every future poll ingest.
/// </summary>
public sealed class CustomDataSourceServiceFieldMapTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();

    private static CustomDataSourceService Build(CustomDataSourceServiceTestDbContext db) =>
        new(db, Substitute.For<ITokenProtector>(), Substitute.For<ICustomDataIngestService>(), []);

    private static UpsertCustomDataSourceRequest Request(
        IReadOnlyDictionary<string, string> fieldMap
    ) =>
        new(
            Name: "sensor",
            DisplayName: "Sensor Feed",
            SourceKind: "poll",
            PresetKey: null,
            EndpointUrl: null,
            AuthSecret: null,
            FieldMap: fieldMap,
            PollIntervalSeconds: 30,
            IsEnabled: true
        );

    [Fact]
    public async Task CreateAsync_rejects_a_malformed_field_map_path_and_persists_nothing()
    {
        CustomDataSourceServiceTestDbContext db = CustomDataSourceServiceTestDbContext.New();
        CustomDataSourceService service = Build(db);

        Result<CustomDataSourceDto> result = await service.CreateAsync(
            Broadcaster,
            Guid.CreateVersion7(),
            // "$.data[" is an unterminated bracket — invalid JSONPath syntax.
            Request(new Dictionary<string, string> { ["bpm"] = "$.data[" })
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("INVALID_FIELD_MAP");
        result.ErrorMessage.Should().Contain("bpm");
        db.CustomDataSources.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_accepts_a_well_formed_field_map_and_persists_it()
    {
        CustomDataSourceServiceTestDbContext db = CustomDataSourceServiceTestDbContext.New();
        CustomDataSourceService service = Build(db);

        Result<CustomDataSourceDto> result = await service.CreateAsync(
            Broadcaster,
            Guid.CreateVersion7(),
            Request(new Dictionary<string, string> { ["bpm"] = "$.data.heartRate" })
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.FieldMap.Should().ContainKey("bpm").WhoseValue.Should().Be("$.data.heartRate");
        db.CustomDataSources.Should().ContainSingle(s => s.Id == result.Value.Id);
    }

    [Fact]
    public async Task UpdateAsync_rejects_changing_the_field_map_to_a_malformed_path()
    {
        CustomDataSourceServiceTestDbContext db = CustomDataSourceServiceTestDbContext.New();
        CustomDataSourceService service = Build(db);

        Result<CustomDataSourceDto> created = await service.CreateAsync(
            Broadcaster,
            Guid.CreateVersion7(),
            Request(new Dictionary<string, string> { ["bpm"] = "$.data.heartRate" })
        );
        created.IsSuccess.Should().BeTrue();

        Result<CustomDataSourceDto> updated = await service.UpdateAsync(
            Broadcaster,
            created.Value.Id,
            Guid.CreateVersion7(),
            Request(new Dictionary<string, string> { ["bpm"] = "$.data[" })
        );

        updated.IsFailure.Should().BeTrue();
        updated.ErrorCode.Should().Be("INVALID_FIELD_MAP");
        // The original valid field-map must survive untouched — the update never wrote through.
        (await db.CustomDataSources.FindAsync(created.Value.Id))!
            .FieldMapJson.Should()
            .Contain("$.data.heartRate");
    }
}
