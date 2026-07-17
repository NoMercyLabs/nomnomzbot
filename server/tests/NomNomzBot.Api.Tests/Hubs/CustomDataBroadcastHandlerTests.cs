// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.CustomEvents.Events;
using NomNomzBot.Domain.Widgets.Entities;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves an ingested custom-data payload reaches the <c>custom_data</c> overlay surface under its
/// source-derived dynamic event name: <c>custom.&lt;source&gt;</c> — the exact string a widget binds — carrying
/// the extracted-fields map, and that a widget bound to a DIFFERENT source hears nothing.
/// </summary>
public sealed class CustomDataBroadcastHandlerTests
{
    [Fact]
    public async Task Ingested_payload_routes_under_the_dynamic_source_name_with_its_fields()
    {
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        Widget gauge = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Name = "Heart rate",
            IsEnabled = true,
            EventSubscriptions = ["custom.heartrate"],
        };
        Widget otherSource = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Name = "Weather",
            IsEnabled = true,
            EventSubscriptions = ["custom.weather"],
        };
        db.Widgets.AddRange(gauge, otherSource);
        await db.SaveChangesAsync();
        CustomDataBroadcastHandler handler = new(db, widgets);

        await handler.HandleAsync(
            new CustomDataReceivedEvent
            {
                BroadcasterId = channel,
                SourceName = "heartrate",
                Fields = new Dictionary<string, string> { ["bpm"] = "72" },
                RawPayload = """{"bpm":72}""",
            }
        );

        // The heartrate-bound widget gets custom.heartrate with the extracted fields map…
        await widgets
            .Received(1)
            .SendWidgetEventAsync(
                channel.ToString(),
                gauge.Id.ToString(),
                Arg.Is<WidgetEventDto>(evt =>
                    evt.EventType == "custom.heartrate" && FieldsMatch(evt.Data)
                ),
                Arg.Any<CancellationToken>()
            );
        // …and the widget bound to a different source stays silent.
        await widgets
            .DidNotReceive()
            .SendWidgetEventAsync(
                channel.ToString(),
                otherSource.Id.ToString(),
                Arg.Any<WidgetEventDto>(),
                Arg.Any<CancellationToken>()
            );
    }

    /// <summary>Asserts the anonymous-typed payload carries { fields: { bpm: "72" } } — the shape the SFC reads.</summary>
    private static bool FieldsMatch(object? data)
    {
        if (data is null)
            return false;
        JsonElement json = JsonSerializer.SerializeToElement(data);
        return json.GetProperty("fields").GetProperty("bpm").GetString() == "72";
    }
}
