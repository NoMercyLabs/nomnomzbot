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
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NomNomzBot.Infrastructure.Platform.Persistence.Converters;

namespace NomNomzBot.Infrastructure.Tests.Platform.Persistence;

/// <summary>
/// The <c>[VC:JSON]</c> converter for a <c>Dictionary&lt;string, object&gt;</c> column (widget settings) must read
/// untyped values back as plain CLR, not Newtonsoft JTokens. If it returns a JArray/JObject, a later
/// System.Text.Json pass (the API response, the overlay <c>window.WIDGET_SETTINGS</c> injection) collapses every
/// nested array to <c>[]</c> and every nested object to <c>{}</c> — which silently broke the alerts and event-ticker
/// widgets, whose <c>events</c> list read back as <c>[[], …]</c> so their event filter dropped everything.
/// </summary>
public sealed class JsonValueConverterTests
{
    private static readonly ValueConverter<Dictionary<string, object>, string> Converter =
        JsonValueConverter.Converter<Dictionary<string, object>>();

    private static Dictionary<string, object> ReadBack(string columnJson) =>
        (Dictionary<string, object>)Converter.ConvertFromProvider(columnJson)!;

    [Fact]
    public void Array_valued_setting_survives_a_round_trip_through_system_text_json()
    {
        // The exact shape the alerts widget stores: a list of event keys plus scalars.
        Dictionary<string, object> model = ReadBack(
            """{"events":["follow","cheer","raid"],"durationMs":6000,"accentColor":"#9146ff"}"""
        );

        // The value is a real list of strings the widget's `indexOf` filter can match — not a Newtonsoft JArray.
        model["events"].Should().BeAssignableTo<IEnumerable<object?>>();
        ((IEnumerable<object?>)model["events"]).Should().Equal("follow", "cheer", "raid");

        // The regression guard: re-serialized by System.Text.Json (as the overlay injection does), the array is
        // itself — NOT the [[],[],[]] that a JArray-of-JValue collapses to.
        string reSerialized = JsonSerializer.Serialize(model);
        reSerialized.Should().Contain("\"events\":[\"follow\",\"cheer\",\"raid\"]");
        reSerialized.Should().NotContain("[[]");
    }

    [Fact]
    public void Nested_object_and_scalars_read_back_as_plain_clr()
    {
        Dictionary<string, object> model = ReadBack(
            """{"colors":{"bar":"#fff","track":"#000"},"target":100,"ratio":0.5,"enabled":true}"""
        );

        model["colors"].Should().BeOfType<Dictionary<string, object?>>();
        model["target"].Should().Be(100L);
        model["ratio"].Should().Be(0.5d);
        model["enabled"].Should().Be(true);

        // A nested object serializes as its members, not the empty {} a JObject collapses to under System.Text.Json.
        string reSerialized = JsonSerializer.Serialize(model);
        reSerialized.Should().Contain("\"colors\":{\"bar\":\"#fff\",\"track\":\"#000\"}");
    }

    [Fact]
    public void Round_trip_to_the_column_and_back_preserves_the_array()
    {
        Dictionary<string, object> model = ReadBack("""{"events":["follow","cheer"]}""");

        // Persisting the read-back model must write the array back verbatim (the comparer/snapshot path).
        string column = (string)Converter.ConvertToProvider(model)!;
        column.Should().Contain("\"events\":[\"follow\",\"cheer\"]");
    }
}
