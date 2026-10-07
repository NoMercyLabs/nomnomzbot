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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Domain.CustomEvents.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.CustomEvents;
using NomNomzBot.Infrastructure.Platform.Caching;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomEvents;

/// <summary>
/// Proves the custom_data widget gets its last value back on join: a value ingested through the real ingest
/// service is replayed as the same <c>custom.&lt;source&gt;</c> frame the live broadcast sends, and a source with
/// nothing cached (or a widget with no source) gives no frame.
/// </summary>
public sealed class CustomDataSeedProviderTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();

    private readonly AuthDbContext _db = AuthTestBuilder.NewContext();
    private readonly ICacheService _cache = new MemoryCacheService(
        new MemoryCache(new MemoryCacheOptions()),
        NullLogger<MemoryCacheService>.Instance
    );

    private async Task IngestAsync(string sourceName, string rawPayload)
    {
        _db.CustomDataSources.Add(
            new CustomDataSource
            {
                BroadcasterId = Broadcaster,
                Name = sourceName,
                DisplayName = sourceName,
                SourceKind = "poll",
                FieldMapJson = "{\"bpm\":\"$.data.heartRate\"}",
                IsEnabled = true,
            }
        );
        await _db.SaveChangesAsync();

        CustomDataIngestService ingest = new(_db, Substitute.For<IEventBus>(), _cache);
        Result result = await ingest.IngestAsync(Broadcaster, sourceName, rawPayload);
        result.IsSuccess.Should().BeTrue();
    }

    private static Widget WidgetWith(Dictionary<string, object> settings) =>
        new() { BroadcasterId = Broadcaster, Settings = settings };

    [Fact]
    public async Task Seed_replays_the_last_ingested_value_as_the_live_custom_frame()
    {
        await IngestAsync("heartrate", "{\"data\":{\"heartRate\":128}}");
        CustomDataSeedProvider sut = new(_cache);

        IReadOnlyList<WidgetSeedFrame> frames = await sut.SeedAsync(
            Broadcaster,
            WidgetWith(new Dictionary<string, object> { ["source"] = "heartrate" }),
            CancellationToken.None
        );

        frames.Should().ContainSingle();
        frames[0].EventType.Should().Be("custom.heartrate");
        JObject wire = JObject.Parse(JsonConvert.SerializeObject(frames[0].Data));
        wire["fields"]!["bpm"]!.Value<string>().Should().Be("128");
        frames[0].OccurredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Seed_gives_no_frame_for_a_source_with_no_cached_value()
    {
        await IngestAsync("heartrate", "{\"data\":{\"heartRate\":128}}");
        CustomDataSeedProvider sut = new(_cache);

        IReadOnlyList<WidgetSeedFrame> frames = await sut.SeedAsync(
            Broadcaster,
            WidgetWith(new Dictionary<string, object> { ["source"] = "power" }),
            CancellationToken.None
        );

        frames.Should().BeEmpty();
    }

    [Fact]
    public async Task Seed_gives_no_frame_when_the_widget_has_no_source_setting()
    {
        await IngestAsync("heartrate", "{\"data\":{\"heartRate\":128}}");
        CustomDataSeedProvider sut = new(_cache);

        IReadOnlyList<WidgetSeedFrame> frames = await sut.SeedAsync(
            Broadcaster,
            WidgetWith(new Dictionary<string, object>()),
            CancellationToken.None
        );

        frames.Should().BeEmpty();
    }
}
