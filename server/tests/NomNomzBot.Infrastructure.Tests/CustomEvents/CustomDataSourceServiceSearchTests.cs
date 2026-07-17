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
using NomNomzBot.Domain.CustomEvents.Entities;
using NomNomzBot.Infrastructure.CustomEvents;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomEvents;

/// <summary>
/// Proves <see cref="CustomDataSourceService.SearchAsync"/> autocompletes the channel's custom data sources:
/// it filters on <c>Name</c> OR <c>DisplayName</c> (so a match on either surfaces the row), scopes to the
/// broadcaster, and honours the result limit — the behaviour the "pick a source" inputs depend on.
/// </summary>
public sealed class CustomDataSourceServiceSearchTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();

    private static CustomDataSourceService Build(CustomDataSourceServiceTestDbContext db) =>
        new(db, Substitute.For<ITokenProtector>(), Substitute.For<ICustomDataIngestService>(), []);

    [Fact]
    public async Task SearchAsync_matches_on_name_or_display_name_and_scopes_to_the_channel()
    {
        CustomDataSourceServiceTestDbContext db = CustomDataSourceServiceTestDbContext.New();

        // Match via DISPLAY NAME only ("Heart Rate Monitor"), name is the slug "hrm".
        CustomDataSource displayMatch = Seed(db, Broadcaster, "hrm", "Heart Rate Monitor");
        // Match via NAME only ("heartbeat"), display name is unrelated.
        CustomDataSource nameMatch = Seed(db, Broadcaster, "heartbeat", "BPM Feed");
        // Matches neither term.
        Seed(db, Broadcaster, "weather", "Weather Widget");
        // Would match by name, but belongs to a DIFFERENT channel — must never leak across tenants.
        Seed(db, Guid.CreateVersion7(), "heartrate", "Other Channel Heart");

        await db.SaveChangesAsync();

        CustomDataSourceService service = Build(db);

        Result<IReadOnlyList<CustomDataSourceOptionDto>> result = await service.SearchAsync(
            Broadcaster,
            "heart",
            20
        );

        result.IsSuccess.Should().BeTrue();
        List<CustomDataSourceOptionDto> options = result.Value.ToList();

        // Exactly the two in-channel rows that match on either field — no cross-tenant leak, no non-match.
        options.Select(o => o.Id).Should().BeEquivalentTo([displayMatch.Id, nameMatch.Id]);

        // DTO shape carries the entity id (Guid), slug name, and display name.
        CustomDataSourceOptionDto display = options.Single(o => o.Id == displayMatch.Id);
        display.Name.Should().Be("hrm");
        display.DisplayName.Should().Be("Heart Rate Monitor");
    }

    [Fact]
    public async Task SearchAsync_respects_the_limit()
    {
        CustomDataSourceServiceTestDbContext db = CustomDataSourceServiceTestDbContext.New();

        for (int i = 0; i < 5; i++)
            Seed(db, Broadcaster, $"sensor{i}", $"Sensor Feed {i}");
        await db.SaveChangesAsync();

        CustomDataSourceService service = Build(db);

        Result<IReadOnlyList<CustomDataSourceOptionDto>> result = await service.SearchAsync(
            Broadcaster,
            "sensor",
            2
        );

        result.IsSuccess.Should().BeTrue();
        // Five sources match the query; the limit caps the result at two.
        result.Value.Should().HaveCount(2);
    }

    private static CustomDataSource Seed(
        CustomDataSourceServiceTestDbContext db,
        Guid broadcasterId,
        string name,
        string displayName
    )
    {
        CustomDataSource source = new()
        {
            BroadcasterId = broadcasterId,
            Name = name,
            DisplayName = displayName,
            SourceKind = "push",
            CreatedByUserId = Guid.CreateVersion7(),
        };
        db.CustomDataSources.Add(source);
        return source;
    }
}
